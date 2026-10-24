package com.livelayer.app

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import com.google.android.gms.common.moduleinstall.ModuleInstall
import com.google.android.gms.common.moduleinstall.ModuleInstallRequest
import com.google.android.gms.tasks.Tasks
import com.google.android.gms.tasks.Task
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.segmentation.subject.SubjectSegmentation
import com.google.mlkit.vision.segmentation.subject.SubjectSegmenterOptions
import com.google.mlkit.vision.segmentation.subject.SubjectSegmentationResult
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.TimeUnit
import kotlin.math.max
import kotlin.math.roundToInt

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

    fun split(source: File): Map<String, Any> {
        val segmenter = SubjectSegmentation.getClient(SubjectSegmenterOptions.Builder()
            .enableForegroundConfidenceMask().build())
        var bitmap: Bitmap? = null
        var inference: Task<SubjectSegmentationResult>? = null
        var directory: File? = null
        try {
            val modules = ModuleInstall.getClient(context)
            if (!Tasks.await(modules.areModulesAvailable(segmenter), 15, TimeUnit.SECONDS).areModulesAvailable()) {
                Tasks.await(modules.installModules(ModuleInstallRequest.newBuilder().addApi(segmenter).build()),
                    15, TimeUnit.SECONDS)
                error("이미지 분리 모델을 다운로드하고 있습니다. 인터넷 연결을 확인하고 잠시 후 다시 시도해 주세요.")
            }
            val sourceBitmap = WallpaperImageProcessor.decode(source, 1600)
            bitmap = sourceBitmap
            val task = segmenter.process(InputImage.fromBitmap(sourceBitmap, 0))
            inference = task
            val result = Tasks.await(task, 60, TimeUnit.SECONDS)
            val buffer = result.foregroundConfidenceMask ?: error("이미지 분리 결과가 없습니다. 다시 시도해 주세요.")
            val width = sourceBitmap.width
            val height = sourceBitmap.height
            val mask = FloatArray(width * height)
            require(buffer.capacity() == mask.size) { "이미지 분리 결과 크기가 다릅니다." }
            buffer.rewind(); buffer.get(mask)
            val count = mask.count { it > .5f }
            require(count > max(8, mask.size / 2000) && count < mask.size * .98) {
                "분리할 인물·사물 또는 배경을 찾지 못했습니다. 피사체가 뚜렷한 사진을 선택해 주세요."
            }
            val pixels = IntArray(mask.size)
            sourceBitmap.getPixels(pixels, 0, width, 0, 0, width, height)
            val foreground = Bitmap.createBitmap(LayerPixels.foreground(pixels, mask), width, height, Bitmap.Config.ARGB_8888)
            val id = UUID.randomUUID().toString()
            val outputDirectory = File(drafts, id)
            directory = outputDirectory
            check(outputDirectory.mkdirs())
            try { write(foreground, File(outputDirectory, "foreground.png")) } finally { foreground.recycle() }

            // Reconstruct a small plate, then preserve the original unmasked background
            // at full processing resolution. This is spatial inpainting, not generative AI.
            val factor = 256.0 / max(width, height)
            val smallWidth = max(1, (width * factor).roundToInt())
            val smallHeight = max(1, (height * factor).roundToInt())
            val small = Bitmap.createScaledBitmap(sourceBitmap, smallWidth, smallHeight, true)
            val smallPixels = IntArray(smallWidth * smallHeight)
            small.getPixels(smallPixels, 0, smallWidth, 0, 0, smallWidth, smallHeight)
            if (small !== sourceBitmap) small.recycle()
            val holes = BooleanArray(smallPixels.size)
            // Include the entire footprint and dilate to remove subject-coloured edges.
            for (i in mask.indices) if (mask[i] > .02f) {
                val x = (i % width * smallWidth / width).coerceAtMost(smallWidth - 1)
                val y = (i / width * smallHeight / height).coerceAtMost(smallHeight - 1)
                for (dy in -1..1) for (dx in -1..1) {
                    val nx = x + dx; val ny = y + dy
                    if (nx in 0 until smallWidth && ny in 0 until smallHeight) holes[ny * smallWidth + nx] = true
                }
            }
            val plate = Bitmap.createBitmap(LayerPixels.inpaint(smallPixels, holes, smallWidth, smallHeight),
                smallWidth, smallHeight, Bitmap.Config.ARGB_8888)
            val expanded = Bitmap.createScaledBitmap(plate, width, height, true)
            if (plate !== expanded) plate.recycle()
            val fillPixels = IntArray(pixels.size)
            expanded.getPixels(fillPixels, 0, width, 0, 0, width, height)
            expanded.recycle()
            for (i in pixels.indices) {
                val x = i % width * smallWidth / width
                val y = i / width * smallHeight / height
                if (holes[y * smallWidth + x]) pixels[i] = fillPixels[i]
            }
            val background = Bitmap.createBitmap(pixels, width, height, Bitmap.Config.ARGB_8888)
            try { write(background, File(outputDirectory, "background.png")) } finally { background.recycle() }
            val layers = JSONArray()
                .put(JSONObject().put("fileName", "background.png").put("layerOrder", 0).put("movementX", 0).put("movementY", 0))
                .put(JSONObject().put("fileName", "foreground.png").put("layerOrder", 1).put("movementX", 1).put("movementY", 1))
            File(outputDirectory, "manifest.json").writeText(JSONObject()
                .put("renderer", "live2d").put("projectId", id).put("sensitivity", 1)
                .put("layers", layers).toString())
            return mapOf("draftId" to id, "backgroundPath" to File(outputDirectory, "background.png").absolutePath,
                "foregroundPath" to File(outputDirectory, "foreground.png").absolutePath,
                "width" to width, "height" to height)
        } catch (error: Exception) {
            directory?.deleteRecursively()
            throw error
        } finally {
            val input = bitmap
            val task = inference
            if (task != null && !task.isComplete) {
                task.addOnCompleteListener { input?.recycle(); segmenter.close() }
            } else { input?.recycle(); segmenter.close() }
        }
    }

    private fun write(bitmap: Bitmap, output: File) {
        output.outputStream().use { check(bitmap.compress(Bitmap.CompressFormat.PNG, 100, it)) }
    }

    /** Import both edited PNGs atomically; leave the raw separation reversible. */
    fun edit(draftId: String, background: File, foreground: File): Map<String, Any> {
        val original = File(drafts, safeId(draftId))
        val manifest = JSONObject(File(original, "manifest.json").readText())
        val sizes = listOf(background, foreground).map { file ->
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeFile(file.absolutePath, bounds)
            require(bounds.outMimeType == "image/png" && bounds.outWidth in 1..1600 && bounds.outHeight in 1..1600) {
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
                "width" to sizes[0].first, "height" to sizes[0].second)
        } catch (error: Exception) {
            destination.deleteRecursively()
            throw error
        }
    }

    fun discard(id: String) { File(drafts, safeId(id)).deleteRecursively() }

    fun attach(owner: String, imageId: String, draftId: String) {
        val source = File(drafts, safeId(draftId))
        require(File(source, "manifest.json").isFile) { "분리된 이미지가 만료되었습니다. 다시 분리해 주세요." }
        val destination = File(ownerDirectory(owner), safeId(imageId))
        val staging = File(ownerDirectory(owner), ".${safeId(imageId)}-${UUID.randomUUID()}")
        val previous = File(ownerDirectory(owner), ".previous-${UUID.randomUUID()}")
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

    fun delete(owner: String, imageId: String) { File(ownerDirectory(owner), safeId(imageId)).deleteRecursively() }

    fun layers(owner: String, imageId: String): Map<String, Any> {
        val directory = File(ownerDirectory(owner), safeId(imageId))
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
        val source = File(ownerDirectory(owner), safeId(imageId))
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
