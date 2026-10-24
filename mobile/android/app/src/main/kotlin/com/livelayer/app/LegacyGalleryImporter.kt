package com.livelayer.app

import android.Manifest
import android.content.ContentUris
import android.content.Context
import android.content.IntentSender
import android.app.RecoverableSecurityException
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.MediaStore
import androidx.exifinterface.media.ExifInterface
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

/** Read/remove compatibility adapter only. New images never enter MediaStore. */
internal class LegacyGalleryImporter(private val context: Context, private val store: PrivateImageStore) {
    private val resolver get() = context.contentResolver
    private val preferences get() = context.getSharedPreferences("private_image_migration", Context.MODE_PRIVATE)
    private val prefix = "UPSCALE_LAB_V1:"
    private val migration = LegacyImageMigration(store)
    private val deletionConsents = mutableMapOf<String, IntentSender>()
    fun deletionConsent(key: String?): IntentSender? = deletionConsents[key]

    fun hasReadPermission(): Boolean = when {
        Build.VERSION.SDK_INT >= 33 -> context.checkSelfPermission(Manifest.permission.READ_MEDIA_IMAGES) == PackageManager.PERMISSION_GRANTED
        else -> context.checkSelfPermission(Manifest.permission.READ_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED
    }

    fun migrate(owner: String, scope: String, force: Boolean = false): LegacyImageMigration.Result {
        val marker = GalleryImageMetadata.ownerKey(owner)
        if ((!force && preferences.getBoolean(marker, false)) || !hasReadPermission()) {
            migrateOldPrivate(owner, scope)
            return LegacyImageMigration.Result(0, 0, emptyList(), 0)
        }
        val temporary = File(context.cacheDir, "legacy_image_migration")
        check(temporary.isDirectory || temporary.mkdirs())
        val candidates = mutableListOf<LegacyImageMigration.Candidate>()
        val id = MediaStore.Images.Media._ID
        val name = MediaStore.Images.Media.DISPLAY_NAME
        @Suppress("DEPRECATION")
        val path = if (Build.VERSION.SDK_INT >= 29) MediaStore.Images.Media.RELATIVE_PATH else MediaStore.Images.Media.DATA
        val selection = if (Build.VERSION.SDK_INT >= 29) "$path = ?" else "$path LIKE ?"
        val args = if (Build.VERSION.SDK_INT >= 29) arrayOf("Pictures/Upscale Lab/") else arrayOf("%/Pictures/Upscale Lab/%")
        var unreadable = 0
        try {
            checkNotNull(resolver.query(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, arrayOf(id, name, path), selection, args, null)).use { cursor ->
                while (cursor.moveToNext()) {
                    val fileName = cursor.getString(cursor.getColumnIndexOrThrow(name))
                    if (!fileName.startsWith("UpscaleLab_")) continue
                    val uri = ContentUris.withAppendedId(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, cursor.getLong(cursor.getColumnIndexOrThrow(id)))
                    val file = File.createTempFile("legacy-", ".img", temporary)
                    try {
                        resolver.openInputStream(uri).use { input ->
                            checkNotNull(input); file.outputStream().use { output -> input.copyTo(output) }
                        }
                        val metadata = readMetadata(file) ?: continue
                        if (!GalleryImageMetadata.isOwned(metadata, marker)) continue
                        candidates.add(LegacyImageMigration.Candidate(uri.toString(), metadata, file, fileName))
                    } catch (_: Exception) { unreadable++ }
                    finally { if (candidates.none { it.source == file }) file.delete() }
                }
            }
            deletionConsents.clear()
            val result = migration.migrate(owner, candidates) { key ->
                try { resolver.delete(Uri.parse(key), null, null) > 0 }
                catch (security: SecurityException) {
                    if (Build.VERSION.SDK_INT == 29 && security is RecoverableSecurityException) {
                        deletionConsents[key] = security.userAction.actionIntent.intentSender
                    }
                    throw security
                }
            }
            val complete = result.failed == 0 && result.pending.isEmpty() && unreadable == 0
            if (complete) check(preferences.edit().putBoolean(marker, true).commit())
            migrateOldPrivate(owner, scope)
            return result.copy(failed = result.failed + unreadable)
        } finally { candidates.forEach { it.source.delete() } }
    }

    private fun readMetadata(file: File): JSONObject? {
        val marker = ImageOwnerMetadata.read(file)
            ?: runCatching { ExifInterface(file).getAttribute(ExifInterface.TAG_USER_COMMENT) }.getOrNull()
        if (marker == null || !marker.startsWith(prefix)) return null
        return JSONObject(marker.removePrefix(prefix))
    }

    private fun migrateOldPrivate(owner: String, scope: String) {
        val prefs = context.getSharedPreferences("local_images", Context.MODE_PRIVATE)
        val items = JSONArray(prefs.getString("items", "[]") ?: "[]")
        var changed = false
        for (index in 0 until items.length()) {
            val item = items.optJSONObject(index) ?: continue
            if (item.optString("ownerScope") != scope || item.optBoolean("privateMigrated")) continue
            // Album metadata may contain newer edits than this obsolete private record.
            if (item.optBoolean("albumMigrated") && runCatching { store.item(owner, item.getString("id")) }.isFailure) continue
            val source = File(item.optString("path")).canonicalFile
            // Never copy an arbitrary path from legacy metadata.
            if (!source.path.startsWith(context.filesDir.canonicalPath + File.separator) || !source.isFile) continue
            try {
                val tags = item.optJSONArray("tags") ?: JSONArray().put(item.optString("tag", "장르 설정되지 않음"))
                store.importImage(owner, source, source.name, item.optString("title", "이미지"),
                    (0 until tags.length()).map { tags.getString(it) }, item.optInt("width"), item.optInt("height"),
                    item.optString("mode", "original"), item.optJSONObject("ratio") ?: JSONObject(), item)
                item.put("privateMigrated", true); changed = true
            } catch (_: Exception) { /* Preserve the previous private source for retry. */ }
        }
        if (changed) check(prefs.edit().putString("items", items.toString()).commit())
    }
}
