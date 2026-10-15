package com.livelayer.app

import android.content.ContentUris
import android.content.ContentValues
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import android.media.MediaScannerConnection
import androidx.exifinterface.media.ExifInterface
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.UUID

/**
 * Album is the source of truth; cacheDir only contains disposable working copies.
 * Ownership and details are embedded in each exported image's EXIF UserComment,
 * which survives app uninstall. No cloud DB or per-image SharedPreferences needed.
 *
 * This is *not* a secure ownership boundary: photo editors may remove EXIF and
 * anyone with access to a picture can read/change its metadata.
 */
internal class GalleryLocalImageStore(private val context: Context) {
    private val resolver get() = context.contentResolver
    private val cacheRoot get() = File(context.cacheDir, "upscale_album_cache")
    private val albumName = "Upscale Lab"
    private val prefix = "UPSCALE_LAB_V1:"

    internal class MediaAccessRequired(val mediaUri: Uri, val deleting: Boolean, cause: Throwable) :
        Exception("미디어 파일 수정 권한이 필요합니다.", cause)

    private data class OwnedMedia(val uri: Uri, val metadata: JSONObject, val extension: String)

    /** Stable identity is derived ONLY from the JWT sub, not the API host address. */
    private fun ownerKey(ownerId: String): String = GalleryImageMetadata.ownerKey(ownerId)

    fun list(ownerId: String, legacyScopeHash: String?): List<Map<String, Any?>> {
        migrateLegacy(ownerId, legacyScopeHash)
        return findOwned(ownerId).map { record ->
            // Other application code consumes local File paths for wallpaper and upload.
            // These are regenerated when the app is reinstalled or the cache is cleared.
            toPlatformMap(record)
        }.sortedByDescending { it["createdAt"] as String }
    }

    fun item(ownerId: String, id: String): JSONObject {
        val record = findOwned(ownerId).firstOrNull { it.metadata.optString("id") == id }
            ?: error("로그인한 사용자의 이미지가 없습니다.")
        return JSONObject(record.metadata.toString()).put("path", workingCopy(record).absolutePath)
    }

    fun importImage(
        ownerId: String,
        source: File,
        requestedName: String,
        title: String,
        tags: List<String>,
        width: Int,
        height: Int,
        mode: String,
        ratio: JSONObject,
        existingMetadata: JSONObject? = null,
    ): Map<String, Any?> {
        require(source.isFile) { "이미지를 찾을 수 없습니다." }
        val item = existingMetadata?.let { JSONObject(it.toString()) } ?: JSONObject().apply {
            put("id", UUID.randomUUID().toString())
            put("title", title.take(160))
            put("tag", tags.firstOrNull() ?: "장르 설정되지 않음")
            put("tags", JSONArray(tags))
            put("width", width)
            put("height", height)
            put("mode", mode)
            put("ratio", ratio)
            put("createdAt", timestamp())
            put("cloudProjectId", JSONObject.NULL)
            put("galleryPostId", JSONObject.NULL)
        }
        // Avoid embedding obsolete private paths or legacy namespace details.
        item.remove("path")
        item.remove("ownerScope")
        item.remove("albumMigrated")
        item.put("ownerKey", ownerKey(ownerId))
        item.put("schema", 1)
        // Image codecs unsupported by ExifInterface are normalized to PNG.
        val extension = source.extension.lowercase(Locale.US).takeIf { it in setOf("jpg", "jpeg", "png") } ?: "png"
        check(cacheRoot.exists() || cacheRoot.mkdirs())
        val temporary = File.createTempFile("prepared-", ".$extension", cacheRoot)
        var mediaUri: Uri? = null
        try {
            if (extension == "png" && source.extension.lowercase(Locale.US) !in setOf("png", "jpg", "jpeg")) {
                val bitmap = BitmapFactory.decodeFile(source.absolutePath)
                    ?: error("이 이미지 형식은 갤러리 등록을 지원하지 않습니다.")
                try {
                    FileOutputStream(temporary).use { out ->
                        check(bitmap.compress(Bitmap.CompressFormat.PNG, 100, out))
                    }
                } finally { bitmap.recycle() }
            } else {
                source.inputStream().use { input -> temporary.outputStream().use { output -> input.copyTo(output) } }
            }
            embed(temporary, item)
            // Fail the import if the marker was not actually saved.
            check(GalleryImageMetadata.matches(item, readMetadata(temporary))) {
                "이미지에 사용자 정보를 저장할 수 없습니다."
            }
            val fileName = "UpscaleLab_${item.getString("id")}.$extension"
            val published = publish(temporary, fileName)
            mediaUri = published
            val record = OwnedMedia(published, item, extension)
            return toPlatformMap(record)
        } catch (failure: Exception) {
            // A failed registration must not leave a visible ownerless gallery image.
            if (mediaUri != null) runCatching { resolver.delete(mediaUri, null, null) }
            throw failure
        } finally {
            temporary.delete()
        }
    }

    fun update(ownerId: String, id: String, changes: (JSONObject) -> Unit) {
        val record = findOwned(ownerId).firstOrNull { it.metadata.optString("id") == id }
            ?: error("수정할 이미지를 찾지 못했습니다.")
        val updated = JSONObject(record.metadata.toString())
        changes(updated)
        updated.put("ownerKey", ownerKey(ownerId))
        updated.put("updatedAt", timestamp())
        try {
            resolver.openFileDescriptor(record.uri, "rw").use { descriptor ->
                checkNotNull(descriptor) { "이미지 파일에 접근할 수 없습니다." }
                val exif = ExifInterface(descriptor.fileDescriptor)
                exif.setAttribute(ExifInterface.TAG_USER_COMMENT, prefix + updated.toString())
                exif.saveAttributes()
            }
            // Verify that modified metadata is durable and readable.
            check(GalleryImageMetadata.matches(updated, readMetadata(record.uri))) {
                "갤러리 이미지 정보를 저장하지 못했습니다."
            }
        } catch (security: SecurityException) {
            throw MediaAccessRequired(record.uri, false, security)
        }
    }

    fun delete(ownerId: String, id: String) {
        val record = findOwned(ownerId).firstOrNull { it.metadata.optString("id") == id }
            ?: return // Delete requests can already have deleted the image.
        try {
            check(resolver.delete(record.uri, null, null) > 0) { "갤러리 이미지를 삭제하지 못했습니다." }
        } catch (security: SecurityException) {
            throw MediaAccessRequired(record.uri, true, security)
        }
        GalleryImageMetadata.cacheFile(cacheRoot, id, record.extension).delete()
    }

    private fun toPlatformMap(record: OwnedMedia): Map<String, Any?> {
        val metadata = record.metadata
        val tags = metadata.optJSONArray("tags") ?: JSONArray()
        return mapOf(
            "id" to metadata.getString("id"),
            "title" to metadata.optString("title", "이미지"),
            "path" to workingCopy(record).absolutePath,
            "width" to metadata.optInt("width", 0),
            "height" to metadata.optInt("height", 0),
            "mode" to metadata.optString("mode", "original"),
            "ratio" to ratioMap(metadata.optJSONObject("ratio")),
            "tag" to metadata.optString("tag", "장르 설정되지 않음"),
            "tags" to (0 until tags.length()).map { tags.getString(it) },
            "createdAt" to metadata.optString("createdAt", timestamp()),
            "cloudProjectId" to metadata.optString("cloudProjectId").takeIf { it.isNotBlank() && it != "null" },
            "galleryPostId" to metadata.optString("galleryPostId").takeIf { it.isNotBlank() && it != "null" },
        )
    }

    private fun ratioMap(ratio: JSONObject?): Map<String, Any?> =
        ratio?.keys()?.asSequence()?.associateWith { ratio.opt(it).takeUnless { value -> value == JSONObject.NULL } }
            ?: emptyMap()

    private fun workingCopy(record: OwnedMedia): File {
        check(cacheRoot.exists() || cacheRoot.mkdirs()) { "이미지 캐시를 만들 수 없습니다." }
        val destination = GalleryImageMetadata.cacheFile(cacheRoot,
            record.metadata.getString("id"), record.extension)
        // Always refresh to avoid stale previews after editing an image in Gallery.
        resolver.openInputStream(record.uri).use { input ->
            checkNotNull(input) { "갤러리 이미지에 접근할 수 없습니다." }
            destination.outputStream().use { output -> input.copyTo(output) }
        }
        return destination
    }

    private fun findOwned(ownerId: String): List<OwnedMedia> {
        val key = ownerKey(ownerId)
        val results = mutableListOf<OwnedMedia>()
        val columns = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            arrayOf(MediaStore.Images.Media._ID, MediaStore.Images.Media.DISPLAY_NAME, MediaStore.Images.Media.RELATIVE_PATH)
        } else {
            arrayOf(MediaStore.Images.Media._ID, MediaStore.Images.Media.DISPLAY_NAME, MediaStore.Images.Media.DATA)
        }
        // Selection limits scanning to Upscale Lab's public album; the EXIF
        // owner marker is the authoritative filter for this logged-in account.
        val selection: String
        val args: Array<String>
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            selection = "${MediaStore.Images.Media.RELATIVE_PATH} LIKE ?"
            args = arrayOf("%$albumName%")
        } else {
            selection = "${MediaStore.Images.Media.DATA} LIKE ?"
            args = arrayOf("%/$albumName/%")
        }
        resolver.query(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, columns, selection, args, null)?.use { cursor ->
            val idCol = cursor.getColumnIndexOrThrow(MediaStore.Images.Media._ID)
            val nameCol = cursor.getColumnIndexOrThrow(MediaStore.Images.Media.DISPLAY_NAME)
            val pathCol = cursor.getColumnIndexOrThrow(
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) MediaStore.Images.Media.RELATIVE_PATH
                else MediaStore.Images.Media.DATA
            )
            while (cursor.moveToNext()) {
                val folder = cursor.getString(pathCol).orEmpty().replace('\\', '/')
                val validFolder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    folder.trimEnd('/').endsWith("/$albumName", ignoreCase = true)
                } else {
                    folder.contains("/$albumName/", ignoreCase = true)
                }
                if (!validFolder) continue
                val uri = ContentUris.withAppendedId(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, cursor.getLong(idCol))
                val metadata = runCatching { readMetadata(uri) }.getOrNull() ?: continue
                if (!GalleryImageMetadata.isOwned(metadata, key)) continue
                val extension = cursor.getString(nameCol).substringAfterLast('.', "png").lowercase(Locale.US)
                if (extension !in setOf("jpg", "jpeg", "png")) continue
                results.add(OwnedMedia(uri, metadata, extension))
            }
        }
        return results
    }

    private fun readMetadata(uri: Uri): JSONObject? =
        resolver.openFileDescriptor(uri, "r").use { descriptor ->
            if (descriptor == null) return null
            val value = ExifInterface(descriptor.fileDescriptor).getAttribute(ExifInterface.TAG_USER_COMMENT)
                ?: return null
            if (!value.startsWith(prefix)) return null
            JSONObject(value.removePrefix(prefix))
        }

    private fun readMetadata(file: File): JSONObject? {
        val value = ExifInterface(file.absolutePath).getAttribute(ExifInterface.TAG_USER_COMMENT)
            ?: return null
        return if (value.startsWith(prefix)) JSONObject(value.removePrefix(prefix)) else null
    }

    private fun embed(file: File, metadata: JSONObject) {
        ExifInterface(file.absolutePath).apply {
            setAttribute(ExifInterface.TAG_USER_COMMENT, prefix + metadata.toString())
            saveAttributes()
        }
    }

    private fun publish(source: File, name: String): Uri {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val values = ContentValues().apply {
                put(MediaStore.Images.Media.DISPLAY_NAME, name)
                put(MediaStore.Images.Media.MIME_TYPE, if (name.endsWith(".png")) "image/png" else "image/jpeg")
                put(MediaStore.Images.Media.RELATIVE_PATH, "${Environment.DIRECTORY_PICTURES}/$albumName/")
                put(MediaStore.Images.Media.IS_PENDING, 1)
            }
            val uri = resolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, values)
                ?: error("갤러리에 이미지를 생성할 수 없습니다.")
            try {
                resolver.openOutputStream(uri, "w").use { out ->
                    checkNotNull(out)
                    source.inputStream().use { input -> input.copyTo(out) }
                }
                values.clear()
                values.put(MediaStore.Images.Media.IS_PENDING, 0)
                resolver.update(uri, values, null, null)
                return uri
            } catch (error: Exception) {
                resolver.delete(uri, null, null)
                throw error
            }
        }
        @Suppress("DEPRECATION")
        val folder = File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_PICTURES), albumName)
        check(folder.exists() || folder.mkdirs())
        val target = File(folder, name)
        try {
            source.inputStream().use { input -> target.outputStream().use { out -> input.copyTo(out) } }
            val values = ContentValues().apply {
                put(MediaStore.Images.Media.DISPLAY_NAME, name)
                put(MediaStore.Images.Media.MIME_TYPE, if (name.endsWith(".png")) "image/png" else "image/jpeg")
                @Suppress("DEPRECATION")
                put(MediaStore.Images.Media.DATA, target.absolutePath)
            }
            // Register synchronously so an immediate wallpaper lookup sees it.
            val uri = resolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, values)
                ?: error("갤러리에 이미지를 등록할 수 없습니다.")
            MediaScannerConnection.scanFile(context, arrayOf(target.absolutePath), null, null)
            return uri
        } catch (failure: Exception) {
            target.delete()
            throw failure
        }
    }

    /** One-time upgrade: copy only this account's old private images into the album. */
    private fun migrateLegacy(ownerId: String, legacyScopeHash: String?) {
        if (legacyScopeHash.isNullOrBlank()) return
        val prefs = context.getSharedPreferences("local_images", Context.MODE_PRIVATE)
        val data = JSONArray(prefs.getString("items", "[]") ?: "[]")
        var modified = false
        val existingIds = findOwned(ownerId).map { it.metadata.optString("id") }.toSet()
        for (i in 0 until data.length()) {
            val old = data.optJSONObject(i) ?: continue
            if (old.optString("ownerScope") != legacyScopeHash || old.optBoolean("albumMigrated", false)) continue
            if (old.optString("id") in existingIds) {
                old.put("albumMigrated", true)
                modified = true
                continue
            }
            val file = File(old.optString("path"))
            if (!file.isFile) continue
            val tagsJson = old.optJSONArray("tags") ?: JSONArray().put(old.optString("tag", "장르 설정되지 않음"))
            val tags = (0 until tagsJson.length()).map { tagsJson.getString(it) }
            try {
                importImage(ownerId, file, file.name, old.optString("title", "이미지"), tags,
                    old.optInt("width"), old.optInt("height"), old.optString("mode", "original"),
                    old.optJSONObject("ratio") ?: JSONObject(), old)
                old.put("albumMigrated", true)
                modified = true
            } catch (_: Exception) {
                // Leave this item intact to retry on the next launch.
            }
        }
        if (modified) check(prefs.edit().putString("items", data.toString()).commit())
    }

    private fun timestamp(): String = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.US)
        .apply { timeZone = TimeZone.getTimeZone("UTC") }.format(Date())
}
