package com.livelayer.app

import android.content.SharedPreferences
import android.app.ActivityManager
import android.app.wallpaper.WallpaperDescription
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.os.Handler
import android.os.Looper
import android.os.Build
import android.os.PersistableBundle
import androidx.annotation.RequiresApi
import android.service.wallpaper.WallpaperService
import android.util.Log
import android.view.SurfaceHolder
import android.view.WindowManager
import android.view.Choreographer
import org.json.JSONObject
import java.io.File
import kotlin.math.max

class LiveLayerWallpaperService : WallpaperService() {
    private val maxDecodedSide by lazy {
        if ((getSystemService(ACTIVITY_SERVICE) as ActivityManager).isLowRamDevice) 1024 else 1600
    }
    override fun onCreateEngine(): Engine = LiveLayerEngine()

    @RequiresApi(36)
    override fun onCreateEngine(description: WallpaperDescription): Engine =
        LiveLayerEngine(description.content.getString("manifest_path"))

    private data class Layer(val bitmap: Bitmap, val order: Int, val positionX: Float,
        val positionY: Float, val rotation: Float, val scale: Float, val movementX: Float, val movementY: Float, val background: Boolean = false)

    private inner class LiveLayerEngine(private val describedManifest: String? = null) : Engine(), SharedPreferences.OnSharedPreferenceChangeListener {
        private val handler = Handler(Looper.getMainLooper())
        private val preferences = getSharedPreferences("live_layer", MODE_PRIVATE)
        private val paint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
        private var loadedManifest: String? = null
        private var previewManifest: String? = null
        private var tiltX = 0f
        private var tiltY = 0f
        private var targetTiltX = 0f
        private var targetTiltY = 0f
        private var lastFrameNanos = 0L
        private var motionEnabled = true
        private val choreographer = Choreographer.getInstance()
        private var sensitivity = 1f
        private var live2D = false
        private var agent = false
        private var owner = ""
        private var canvasWidth = 1f
        private var canvasHeight = 1f
        private var decodeSide = maxDecodedSide
        private fun authorized() = !agent || (preferences.getString("companion_owner", "") == owner &&
            preferences.getLong("companion_valid_until", 0) > System.currentTimeMillis())
        private val leaseCheck = object : Runnable {
            override fun run() {
                if (agent && !authorized()) { sensor.stop(); tiltX = 0f; tiltY = 0f; scheduleDraw() }
                handler.postDelayed(this, 15000)
            }
        }
        private var layers: List<Layer> = emptyList()
        private var fallback: Bitmap? = null
        private var frameScheduled = false
        private val frame = object : Choreographer.FrameCallback {
            override fun doFrame(frameTimeNanos: Long) {
                frameScheduled = false
                if (!lifecycle.visible || !lifecycle.surfaceReady) return
                val elapsed = if (lastFrameNanos == 0L) 1.0 / 60 else
                    ((frameTimeNanos - lastFrameNanos) / 1_000_000_000.0).coerceIn(0.0, .1)
                lastFrameNanos = frameTimeNanos
                val amount = (1 - kotlin.math.exp(-elapsed / .065)).toFloat()
                if (motionEnabled && authorized()) {
                    tiltX += (targetTiltX - tiltX) * amount
                    tiltY += (targetTiltY - tiltY) * amount
                } else { tiltX = 0f; tiltY = 0f }
                draw()
                if (motionEnabled && authorized() && layers.isNotEmpty()) scheduleDraw()
            }
        }
        private val sensor = TiltSensor(this@LiveLayerWallpaperService, {
            if (Build.VERSION.SDK_INT >= 30) displayContext?.display?.rotation ?: 0
            else (getSystemService(WINDOW_SERVICE) as WindowManager).defaultDisplay.rotation
        }) { x, y ->
            targetTiltX = x; targetTiltY = y
        }
        private val lifecycle: WallpaperEngineLifecycle = WallpaperEngineLifecycle(
            refresh = { loadActiveProject(surfaceHolder.surfaceFrame.width(), surfaceHolder.surfaceFrame.height()) },
            startSensor = { if (authorized() && motionEnabled) sensor.start() }, stopSensor = { sensor.stop() },
            requestFrame = { scheduleDraw() }, cancelFrames = {
                choreographer.removeFrameCallback(frame); frameScheduled = false; lastFrameNanos = 0L
            })

        override fun onCreate(holder: SurfaceHolder) {
            super.onCreate(holder)
            if (isPreview) previewManifest = preferences.getString("preview_manifest_path", null)
            preferences.registerOnSharedPreferenceChangeListener(this)
            handler.postDelayed(leaseCheck, 15000)
        }

        override fun onSharedPreferenceChanged(preferences: SharedPreferences?, key: String?) {
            if (key == "companion_manifest_revision" && agent) {
                loadedManifest = null; lifecycle.selectionChanged()
            }
            if (key == "companion_valid_until" && agent) {
                if (authorized() && lifecycle.visible && motionEnabled) sensor.start() else { sensor.stop(); tiltX = 0f; tiltY = 0f }
                scheduleDraw()
            }
            if ((key == "manifest_path" || key == "direct_manifest_path") && !isPreview) {
                lifecycle.selectionChanged()
            }
        }

        override fun onVisibilityChanged(isVisible: Boolean) {
            lifecycle.visibilityChanged(isVisible, surfaceHolder.surface.isValid)
        }

        override fun onSurfaceCreated(holder: SurfaceHolder) {
            super.onSurfaceCreated(holder)
            if (Build.VERSION.SDK_INT >= 30) {
                // Hint the compositor to avoid choosing a 30Hz mode while this wallpaper moves.
                val rate = (displayContext?.display?.refreshRate ?: 60f).coerceAtLeast(60f)
                runCatching { holder.surface.setFrameRate(rate, android.view.Surface.FRAME_RATE_COMPATIBILITY_DEFAULT) }
            }
            lifecycle.surfaceCreated()
        }

        override fun onSurfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
            super.onSurfaceChanged(holder, format, width, height)
            lifecycle.surfaceCreated()
            draw()
        }

        override fun onSurfaceDestroyed(holder: SurfaceHolder) {
            lifecycle.surfaceDestroyed()
            super.onSurfaceDestroyed(holder)
        }

        override fun onDestroy() {
            lifecycle.destroy()
            handler.removeCallbacks(leaseCheck)
            preferences.unregisterOnSharedPreferenceChangeListener(this)
            recycle()
            super.onDestroy()
        }

        @RequiresApi(36)
        override fun onApplyWallpaper(which: Int): WallpaperDescription? {
            val path = previewManifest ?: loadedManifest ?: return null
            // Android persists this before it creates the real engine. Do not depend
            // on MainActivity's later result callback for the selected image.
            check(preferences.edit().putString("manifest_path", path).commit())
            val content = PersistableBundle().apply { putString("manifest_path", path) }
            return WallpaperDescription.Builder().setId(File(path).parentFile?.name ?: path)
                .setContent(content).build()
        }

        private fun recycle() {
            layers.forEach { it.bitmap.recycle() }; layers = emptyList()
            fallback?.recycle(); fallback = null
        }

        private fun loadActiveProject(width: Int, height: Int) {
            if (width <= 0 || height <= 0) return
            val base = (if (isPreview) previewManifest else describedManifest)
                ?: preferences.getString("manifest_path", null)
            val direct = if (isPreview) null else preferences.getString("direct_manifest_path", null)
            fun revision(path: String?): Long = path?.let {
                runCatching { JSONObject(File(it).readText()).optLong("selectionRevision", 0) }.getOrDefault(0)
            } ?: 0
            val path = LiveWallpaperSelection.choose(base, revision(base), direct, revision(direct)) ?: return
            if (loadedManifest == path) return
            recycle()
            loadedManifest = path // Retry failed content only when a new snapshot is selected.
            val loaded = mutableListOf<Layer>()
            try {
                val file = File(path)
                val directory = file.parentFile ?: return
                val manifest = JSONObject(file.readText())
                agent = manifest.optString("protocol") == "agent-v1"
                owner = manifest.optString("ownerId")
                canvasWidth = manifest.optInt("width", 1).toFloat().coerceAtLeast(1f)
                canvasHeight = manifest.optInt("height", 1).toFloat().coerceAtLeast(1f)
                live2D = manifest.optString("renderer") == "live2d"
                motionEnabled = live2D && WallpaperMotion.usesGyro(manifest)
                targetTiltX = 0f; targetTiltY = 0f; tiltX = 0f; tiltY = 0f
                if (!motionEnabled) sensor.stop() else if (lifecycle.visible && authorized()) sensor.start()
                sensitivity = manifest.optDouble("sensitivity", 1.0).toFloat().coerceIn(0f, 2f)
                fallback = decode(File(directory, manifest.optString("originalFileName", "original.img")))
                val items = manifest.getJSONArray("layers")
                decodeSide = minOf(maxDecodedSide, kotlin.math.sqrt(8_000_000.0 / items.length().coerceAtLeast(1)).toInt())
                for (index in 0 until items.length()) {
                    val item = items.getJSONObject(index)
                    val bitmap = decode(File(directory, item.getString("fileName")))
                        ?: error("레이어 이미지를 읽지 못했습니다.")
                    loaded.add(Layer(bitmap, item.optInt("layerOrder", index),
                        item.optDouble("positionX", 0.0).toFloat(), item.optDouble("positionY", 0.0).toFloat(),
                        item.optDouble("rotation", 0.0).toFloat(), item.optDouble("scale", 1.0).toFloat(),
                        item.optDouble("movementX", 0.0).toFloat(), item.optDouble("movementY", 0.0).toFloat(), item.optString("role") == "Background"))
                }
                layers = loaded.sortedBy { it.order }
            } catch (error: OutOfMemoryError) {
                loaded.forEach { it.bitmap.recycle() }
                recycle()
                Log.e("Live2D", "Wallpaper bitmap memory budget exhausted", error)
            } catch (error: Exception) {
                loaded.forEach { it.bitmap.recycle() }
                Log.w("Live2D", "Cannot load wallpaper layers", error)
            }
        }

        private fun decode(file: File): Bitmap? {
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeFile(file.absolutePath, bounds)
            if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null
            val sample = WallpaperBitmapBudget.sampleSize(bounds.outWidth, bounds.outHeight, decodeSide)
            return BitmapFactory.decodeFile(file.absolutePath, BitmapFactory.Options().apply { inSampleSize = sample })
        }

        private fun scheduleDraw() {
            if (lifecycle.visible && lifecycle.surfaceReady && !frameScheduled) { frameScheduled = true; choreographer.postFrameCallback(frame) }
        }

        private fun draw() {
            if (!lifecycle.surfaceReady || !surfaceHolder.surface.isValid) return
            val drawingSurface = surfaceHolder.surface
            val rendered = WallpaperFrameRenderer.render(
                acquire = { if (Build.VERSION.SDK_INT >= 26) surfaceHolder.lockHardwareCanvas()
                    else drawingSurface.lockHardwareCanvas() },
                draw = { canvas ->
                    canvas.drawColor(Color.BLACK)
                    if (layers.isEmpty()) fallback?.let { drawBitmap(canvas, it, 0f, 0f, 0f, 1f) }
                    else for ((index, layer) in layers.withIndex()) {
                        if (agent) {
                            val cover = max(canvas.width / canvasWidth, canvas.height / canvasHeight)
                            val moving = motionEnabled && authorized() && !layer.background
                            val x = if (moving) -tiltX.coerceIn(-1f, 1f) * layer.movementX else 0f
                            val y = if (moving) tiltY.coerceIn(-1f, 1f) * layer.movementY else 0f
                            canvas.save()
                            canvas.translate(canvas.width / 2f + (layer.positionX + x) * cover, canvas.height / 2f + (layer.positionY + y) * cover)
                            canvas.rotate(layer.rotation)
                            canvas.scale(cover * layer.scale, cover * layer.scale)
                            canvas.drawBitmap(layer.bitmap, null, android.graphics.RectF(-canvasWidth / 2, -canvasHeight / 2, canvasWidth / 2, canvasHeight / 2), paint)
                            canvas.restore()
                            continue
                        }
                        // Live 2D anchors the background. Only the second layer moves.
                        val moving = motionEnabled && (!live2D || index > 0)
                        val amplitudeX = if (live2D) canvas.width * .035f else layer.movementX * 4f
                        val amplitudeY = if (live2D) canvas.height * .035f else layer.movementY * 4f
                        drawBitmap(canvas, layer.bitmap,
                            layer.positionX + if (moving) -tiltX * amplitudeX * sensitivity else 0f,
                            layer.positionY + if (moving) tiltY * amplitudeY * sensitivity else 0f,
                            layer.rotation, layer.scale * if (live2D) 1.08f else 1f)
                    }
                },
                post = { if (Build.VERSION.SDK_INT >= 26) surfaceHolder.unlockCanvasAndPost(it)
                    else drawingSurface.unlockCanvasAndPost(it) },
                failed = { Log.w("Live2D", "Wallpaper surface changed while drawing", it) })
            if (rendered && layers.isNotEmpty() && agent && !isPreview && authorized() && preferences.getString("companion_rendered_manifest", null) != loadedManifest) {
                preferences.edit().putString("companion_rendered_manifest", loadedManifest).apply()
                CompanionRuntime.pollSoon()
            }
        }

        private fun drawBitmap(canvas: Canvas, bitmap: Bitmap, x: Float, y: Float, rotation: Float, layerScale: Float) {
            val scale = max(canvas.width.toFloat() / bitmap.width, canvas.height.toFloat() / bitmap.height) * layerScale
            canvas.save()
            canvas.translate(canvas.width / 2f + x, canvas.height / 2f + y)
            canvas.rotate(rotation); canvas.scale(scale, scale)
            canvas.drawBitmap(bitmap, -bitmap.width / 2f, -bitmap.height / 2f, paint)
            canvas.restore()
        }
    }
}
