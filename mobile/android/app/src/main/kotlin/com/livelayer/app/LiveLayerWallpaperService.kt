package com.livelayer.app

import android.content.SharedPreferences
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
import org.json.JSONObject
import java.io.File
import kotlin.math.max

class LiveLayerWallpaperService : WallpaperService() {
    override fun onCreateEngine(): Engine = LiveLayerEngine()

    @RequiresApi(36)
    override fun onCreateEngine(description: WallpaperDescription): Engine =
        LiveLayerEngine(description.content.getString("manifest_path"))

    private data class Layer(val bitmap: Bitmap, val order: Int, val positionX: Float,
        val positionY: Float, val rotation: Float, val scale: Float, val movementX: Float, val movementY: Float)

    private inner class LiveLayerEngine(private val describedManifest: String? = null) : Engine(), SharedPreferences.OnSharedPreferenceChangeListener {
        private val handler = Handler(Looper.getMainLooper())
        private val preferences = getSharedPreferences("live_layer", MODE_PRIVATE)
        private val paint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
        private var loadedManifest: String? = null
        private var previewManifest: String? = null
        private var tiltX = 0f
        private var tiltY = 0f
        private var sensitivity = 1f
        private var live2D = false
        private var layers: List<Layer> = emptyList()
        private var fallback: Bitmap? = null
        private var frameScheduled = false
        private val frame = Runnable { frameScheduled = false; draw() }
        private val sensor = TiltSensor(this@LiveLayerWallpaperService, {
            if (Build.VERSION.SDK_INT >= 30) displayContext?.display?.rotation ?: 0
            else (getSystemService(WINDOW_SERVICE) as WindowManager).defaultDisplay.rotation
        }) { x, y ->
            if (kotlin.math.abs(x - tiltX) + kotlin.math.abs(y - tiltY) > .0005f) {
                tiltX = x; tiltY = y
                scheduleDraw()
            }
        }
        private val lifecycle = WallpaperEngineLifecycle(
            refresh = { loadActiveProject(surfaceHolder.surfaceFrame.width(), surfaceHolder.surfaceFrame.height()) },
            startSensor = { sensor.start() }, stopSensor = { sensor.stop() },
            requestFrame = { scheduleDraw() }, cancelFrames = {
                handler.removeCallbacks(frame); frameScheduled = false
            })

        override fun onCreate(holder: SurfaceHolder) {
            super.onCreate(holder)
            if (isPreview) previewManifest = preferences.getString("preview_manifest_path", null)
            preferences.registerOnSharedPreferenceChangeListener(this)
        }

        override fun onSharedPreferenceChanged(preferences: SharedPreferences?, key: String?) {
            if (key == "manifest_path" && !isPreview) {
                loadedManifest = null
                lifecycle.selectionChanged()
            }
        }

        override fun onVisibilityChanged(isVisible: Boolean) {
            lifecycle.visibilityChanged(isVisible, surfaceHolder.surface.isValid)
        }

        override fun onSurfaceCreated(holder: SurfaceHolder) {
            super.onSurfaceCreated(holder)
            lifecycle.surfaceCreated()
        }

        override fun onSurfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
            super.onSurfaceChanged(holder, format, width, height)
            loadedManifest = null
            lifecycle.surfaceCreated()
            draw()
        }

        override fun onSurfaceDestroyed(holder: SurfaceHolder) {
            lifecycle.surfaceDestroyed()
            super.onSurfaceDestroyed(holder)
        }

        override fun onDestroy() {
            lifecycle.destroy()
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
            val path = (if (isPreview) previewManifest else describedManifest)
                ?: preferences.getString("manifest_path", null) ?: return
            if (loadedManifest == path && layers.isNotEmpty()) return
            recycle()
            try {
                val file = File(path)
                val directory = file.parentFile ?: return
                val manifest = JSONObject(file.readText())
                live2D = manifest.optString("renderer") == "live2d"
                sensitivity = manifest.optDouble("sensitivity", 1.0).toFloat().coerceIn(0f, 2f)
                fallback = decode(File(directory, manifest.optString("originalFileName", "original.img")), width, height)
                val items = manifest.getJSONArray("layers")
                val loaded = mutableListOf<Layer>()
                try {
                    for (index in 0 until items.length()) {
                        val item = items.getJSONObject(index)
                        val bitmap = decode(File(directory, item.getString("fileName")), width, height)
                            ?: error("레이어 이미지를 읽지 못했습니다.")
                        loaded.add(Layer(bitmap, item.optInt("layerOrder", index),
                            item.optDouble("positionX", 0.0).toFloat(), item.optDouble("positionY", 0.0).toFloat(),
                            item.optDouble("rotation", 0.0).toFloat(), item.optDouble("scale", 1.0).toFloat(),
                            item.optDouble("movementX", 0.0).toFloat(), item.optDouble("movementY", 0.0).toFloat()))
                    }
                    layers = loaded.sortedBy { it.order }
                    loadedManifest = path
                } catch (error: Exception) { loaded.forEach { it.bitmap.recycle() }; throw error }
            } catch (error: Exception) { Log.w("Live2D", "Cannot load wallpaper layers", error) }
        }

        private fun decode(file: File, width: Int, height: Int): Bitmap? {
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeFile(file.absolutePath, bounds)
            if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null
            var sample = 1
            while (bounds.outWidth / (sample * 2) >= max(1, width) &&
                bounds.outHeight / (sample * 2) >= max(1, height)) sample *= 2
            return BitmapFactory.decodeFile(file.absolutePath, BitmapFactory.Options().apply { inSampleSize = sample })
        }

        private fun scheduleDraw() {
            if (lifecycle.visible && lifecycle.surfaceReady && !frameScheduled) { frameScheduled = true; handler.postDelayed(frame, 16) }
        }

        private fun draw() {
            if (!lifecycle.surfaceReady || !surfaceHolder.surface.isValid) return
            var canvas: Canvas? = null
            try {
                canvas = surfaceHolder.lockCanvas() ?: return
                canvas.drawColor(Color.BLACK)
                if (layers.isEmpty()) fallback?.let { drawBitmap(canvas, it, 0f, 0f, 0f, 1f) }
                else for ((index, layer) in layers.withIndex()) {
                    // Live 2D anchors the background. Only the second layer moves.
                    val moving = !live2D || index > 0
                    val amplitudeX = if (live2D) canvas.width * .035f else layer.movementX * 4f
                    val amplitudeY = if (live2D) canvas.height * .035f else layer.movementY * 4f
                    drawBitmap(canvas, layer.bitmap,
                        layer.positionX + if (moving) -tiltX * amplitudeX * sensitivity else 0f,
                        layer.positionY + if (moving) tiltY * amplitudeY * sensitivity else 0f,
                        layer.rotation, layer.scale * if (live2D) 1.08f else 1f)
                }
            } catch (error: RuntimeException) { Log.w("Live2D", "Cannot draw wallpaper", error) }
            finally { if (canvas != null) surfaceHolder.unlockCanvasAndPost(canvas) }
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
