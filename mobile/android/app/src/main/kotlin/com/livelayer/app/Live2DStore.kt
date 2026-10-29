package com.livelayer.app

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.security.MessageDigest
import java.util.UUID

/** Drafts are disposable; saved layers and active wallpaper copies are app-private. */
internal class Live2DStore(private val context: Context) {
    private val drafts get() = File(context.cacheDir, "live_2d_drafts")
    private val saved get() = File(context.filesDir, "live_2d")

    private fun safeId(id: String): String {
        require(id.matches(Regex("[A-Za-z0-9_-]{1,128}"))) { "Live 2D ID가 올바르지 않습니다." }
        return id
    }

    private fun ownerDirectory(owner: String): File {
        val key = MessageDigest.getInstance("SHA-256").digest(owner.toByteArray(Charsets.UTF_8))
            .joinToString("") { "%02x".format(it) }
        return File(saved, key)
    }

    private fun privateLayers(owner: String, id: String): File =
        File(context.filesDir, "image_library/${GalleryImageMetadata.ownerKey(owner)}/${safeId(id)}/live2d")

    private fun savedLayers(owner: String, id: String): File = privateLayers(owner, id).let {
        if (it.isDirectory) it else File(ownerDirectory(owner), safeId(id))
    }

    /** Import cloud-generated layers. This device never runs segmentation inference. */
    fun importCloud(source: File, backgroundUrl: String, foregroundUrl: String, width: Int, height: Int): Map<String, Any> {
        require(width in 1..32768 && height in 1..32768 && width.toLong() * height <= 64_000_000) { "클라우드 레이어 크기가 올바르지 않습니다." }
        val id = UUID.randomUUID().toString()
        val directory = File(drafts, id)
        try {
            check(directory.mkdirs())
            for ((url, name) in listOf(backgroundUrl to "background.png", foregroundUrl to "foreground.png")) {
                val uri = java.net.URI(url)
                require(uri.scheme == "https" && uri.userInfo == null) { "클라우드 다운로드 주소가 올바르지 않습니다." }
                val connection = uri.toURL().openConnection() as java.net.HttpURLConnection
                try {
                    connection.connectTimeout = 15000; connection.readTimeout = 60000; connection.instanceFollowRedirects = false
                    require(connection.responseCode == 200) { "클라우드 레이어를 내려받지 못했습니다." }
                    connection.inputStream.use { input -> File(directory, name).outputStream().use { output ->
                        val buffer = ByteArray(65536); var total = 0L
                        while (true) { val count = input.read(buffer); if (count < 0) break
                            total += count; require(total <= 256L * 1024 * 1024) { "클라우드 레이어 용량이 너무 큽니다." }
                            output.write(buffer, 0, count)
                        }
                    } }
                } finally { connection.disconnect() }
                val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                BitmapFactory.decodeFile(File(directory, name).absolutePath, bounds)
                require(bounds.outMimeType == "image/png" && bounds.outWidth == width && bounds.outHeight == height) { "클라우드 레이어 규격이 일치하지 않습니다." }
            }
            source.copyTo(File(directory, "original.img"))
            val layers = JSONArray()
                .put(JSONObject().put("fileName", "background.png").put("role", "Background").put("layerOrder", 0).put("movementX", 0).put("movementY", 0))
                .put(JSONObject().put("fileName", "foreground.png").put("role", "Body").put("layerOrder", 1).put("movementX", 1).put("movementY", 1))
            File(directory, "manifest.json").writeText(JSONObject().put("renderer", "live2d")
                .put("projectId", id).put("motionEnabled", true).put("sensitivity", 1).put("width", width).put("height", height)
                .put("layers", layers).toString())
            return mapOf("draftId" to id, "backgroundPath" to File(directory, "background.png").absolutePath,
                "foregroundPath" to File(directory, "foreground.png").absolutePath, "width" to width, "height" to height)
        } catch (error: Exception) { directory.deleteRecursively(); throw error }
    }

    fun setMotion(id: String, enabled: Boolean) {
        val file = File(File(drafts, safeId(id)), "manifest.json")
        file.writeText(JSONObject(file.readText()).put("motionEnabled", enabled).toString())
    }

    private fun write(bitmap: Bitmap, output: File) {
        output.outputStream().use { check(bitmap.compress(Bitmap.CompressFormat.PNG, 100, it)) }
    }

    /** Import both edited PNGs atomically; leave the raw separation reversible. */
    fun edit(draftId: String, background: File, foreground: File): Map<String, Any> {
        val original = File(drafts, safeId(draftId))
        return copyDraft(original, background, foreground)
    }

    fun cloneDraft(id: String): Map<String, Any> = File(drafts, safeId(id)).let {
        copyDraft(it, File(it, "background.png"), File(it, "foreground.png"))
    }

    fun editableLayers(owner: String, id: String): Map<String, Any> = savedLayers(owner, id).let {
        copyDraft(it, File(it, "background.png"), File(it, "foreground.png"))
    }

    private fun copyDraft(original: File, background: File, foreground: File): Map<String, Any> {
        val manifest = JSONObject(File(original, "manifest.json").readText())
        val sizes = listOf(background, foreground).map { file ->
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeFile(file.absolutePath, bounds)
            require(bounds.outMimeType == "image/png" && bounds.outWidth in 1..32768 && bounds.outHeight in 1..32768 && bounds.outWidth.toLong() * bounds.outHeight <= 64_000_000) {
                "편집한 Live 2D 이미지가 올바르지 않습니다."
            }
            bounds.outWidth to bounds.outHeight
        }
        require(sizes[0] == sizes[1]) { "Live 2D 레이어 크기가 다릅니다." }
        val id = UUID.randomUUID().toString()
        val destination = File(drafts, id)
        try {
            check(destination.mkdirs())
            background.copyTo(File(destination, "background.png"))
            foreground.copyTo(File(destination, "foreground.png"))
            File(destination, "manifest.json").writeText(manifest.put("projectId", id).toString())
            return mapOf("draftId" to id, "backgroundPath" to File(destination, "background.png").absolutePath,
                "foregroundPath" to File(destination, "foreground.png").absolutePath,
                "width" to sizes[0].first, "height" to sizes[0].second,
                "motionEnabled" to manifest.optBoolean("motionEnabled", true))
        } catch (error: Exception) {
            destination.deleteRecursively()
            throw error
        }
    }

    fun discard(id: String) { File(drafts, safeId(id)).deleteRecursively() }

    fun attach(owner: String, imageId: String, draftId: String) {
        val source = File(drafts, safeId(draftId))
        require(File(source, "manifest.json").isFile) { "분리된 이미지가 만료되었습니다. 다시 분리해 주세요." }
        val destination = privateLayers(owner, imageId)
        val staging = File(destination.parentFile, ".live2d-${UUID.randomUUID()}")
        val previous = File(destination.parentFile, ".previous-${UUID.randomUUID()}")
        try {
            check(staging.mkdirs())
            for (name in listOf("background.png", "foreground.png", "manifest.json")) {
                File(source, name).copyTo(File(staging, name))
            }
            if (destination.exists()) check(destination.renameTo(previous))
            check(staging.renameTo(destination))
            previous.deleteRecursively()
        } catch (error: Exception) {
            staging.deleteRecursively()
            if (previous.exists() && !destination.exists()) previous.renameTo(destination)
            throw error
        }
    }

    fun delete(owner: String, imageId: String) {
        privateLayers(owner, imageId).deleteRecursively()
        File(ownerDirectory(owner), safeId(imageId)).deleteRecursively()
    }

    fun copyDraftLayers(draftId: String, destination: File) {
        val source = File(drafts, safeId(draftId))
        require(listOf("manifest.json", "background.png", "foreground.png").all { File(source, it).isFile }) {
            "Live 2D 레이어가 만료되었습니다. 다시 분리해 주세요."
        }
        check(source.copyRecursively(destination))
    }

    fun layers(owner: String, imageId: String): Map<String, Any> {
        val directory = savedLayers(owner, imageId)
        require(listOf("manifest.json", "background.png", "foreground.png").all { File(directory, it).isFile }) {
            "Live 2D 레이어가 없습니다. 원본 사진에서 다시 분리해 주세요."
        }
        val sizes = listOf("background.png", "foreground.png").map { name ->
            val options = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeFile(File(directory, name).absolutePath, options)
            require(options.outWidth > 0 && options.outHeight > 0) { "Live 2D 이미지가 손상되었습니다. 다시 분리해 주세요." }
            options.outWidth to options.outHeight
        }
        require(sizes[0] == sizes[1]) { "Live 2D 레이어 크기가 다릅니다. 다시 분리해 주세요." }
        return mapOf("backgroundPath" to File(directory, "background.png").absolutePath,
            "foregroundPath" to File(directory, "foreground.png").absolutePath)
    }

    fun activate(owner: String, imageId: String): File {
        layers(owner, imageId)
        val source = savedLayers(owner, imageId)
        return snapshot(source)
    }

    fun activateDraft(draftId: String): File = snapshot(File(drafts, safeId(draftId)))

    private fun snapshot(source: File): File {
        require(listOf("manifest.json", "background.png", "foreground.png").all { File(source, it).isFile }) {
            "Live 2D 레이어가 없습니다. 다시 분리해 주세요."
        }
        // The wallpaper must survive deletion/logout of the library item.
        val root = File(context.filesDir, "wallpapers")
        val destination = File(root, "live2d-${UUID.randomUUID()}")
        try {
            check(source.copyRecursively(destination))
            return stampSelection(File(destination, "manifest.json"))
        } catch (error: Exception) { destination.deleteRecursively(); throw error }
    }

    fun stampSelection(manifest: File): File {
        val preferences = context.getSharedPreferences("live_layer", Context.MODE_PRIVATE)
        val revision = preferences.getLong("selection_revision", 0) + 1
        check(preferences.edit().putLong("selection_revision", revision).commit())
        manifest.writeText(JSONObject(manifest.readText()).put("selectionRevision", revision).toString())
        return manifest
    }

    fun discardWallpaper(manifestPath: String?) {
        if (manifestPath == null) return
        val root = File(context.filesDir, "wallpapers").canonicalFile
        val directory = File(manifestPath).canonicalFile.parentFile ?: return
        if (directory.parentFile == root && directory.name.matches(Regex("live2d-[A-Fa-f0-9-]{36}"))) {
            directory.deleteRecursively()
        }
    }
}
