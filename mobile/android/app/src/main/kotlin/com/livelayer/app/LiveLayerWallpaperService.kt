package com.livelayer.app

import android.content.SharedPreferences
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.os.Handler
import android.os.Looper
import android.service.wallpaper.WallpaperService
import android.util.Log
import android.view.SurfaceHolder
import android.view.WindowManager
import org.json.JSONObject
import java.io.File
import kotlin.math.max

class LiveLayerWallpaperService : WallpaperService() {
    override fun onCreateEngine(): Engine = LiveLayerEngine()

    private data class Layer(val bitmap: Bitmap, val order: Int, val positionX: Float,
        val positionY: Float, val rotation: Float, val scale: Float, val movementX: Float, val movementY: Float)

    private inner class LiveLayerEngine : Engine(), SharedPreferences.OnSharedPreferenceChangeListener {
        private val handler = Handler(Looper.getMainLooper())
        private val preferences = getSharedPreferences("live_layer", MODE_PRIVATE)
        private val paint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
        private var visible = false
        private var ready = false
        private var tiltX = 0f
        private var tiltY = 0f
        private var sensitivity = 1f
        private var live2D = false
        private var layers: List<Layer> = emptyList()
        private var fallback: Bitmap? = null
        private var frameScheduled = false
        private val frame = Runnable { frameScheduled = false; draw() }
        private val sensor = TiltSensor(this@LiveLayerWallpaperService, {
            (getSystemService(WINDOW_SERVICE) as WindowManager).defaultDisplay.rotation
        }) { x, y ->
            if (kotlin.math.abs(x - tiltX) + kotlin.math.abs(y - tiltY) > .0005f) {
                tiltX = x; tiltY = y
                scheduleDraw()
            }
        }

        override fun onCreate(holder: SurfaceHolder) {
            super.onCreate(holder)
            preferences.registerOnSharedPreferenceChangeListener(this)
        }

        override fun onSharedPreferenceChanged(preferences: SharedPreferences?, key: String?) {
            if (key == "manifest_path" && !isPreview && ready) {
                loadActiveProject(surfaceHolder.surfaceFrame.width(), surfaceHolder.surfaceFrame.height())
                scheduleDraw()
            }
        }

        override fun onVisibilityChanged(isVisible: Boolean) {
            visible = isVisible
            if (visible) { sensor.start(); scheduleDraw() }
            else { sensor.stop(); handler.removeCallbacks(frame); frameScheduled = false }
        }

        override fun onSurfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
            super.onSurfaceChanged(holder, format, width, height)
            ready = true
            loadActiveProject(width, height)
            if (visible) sensor.start()
            draw()
        }

        override fun onSurfaceDestroyed(holder: SurfaceHolder) {
            ready = false
            sensor.stop()
            handler.removeCallbacks(frame); frameScheduled = false
            super.onSurfaceDestroyed(holder)
        }

        override fun onDestroy() {
            sensor.stop()
            handler.removeCallbacks(frame)
            preferences.unregisterOnSharedPreferenceChangeListener(this)
            recycle()
            super.onDestroy()
        }

        private fun recycle() {
            layers.forEach { it.bitmap.recycle() }; layers = emptyList()
            fallback?.recycle(); fallback = null
        }

        private fun loadActiveProject(width: Int, height: Int) {
            val path = (if (isPreview) preferences.getString("preview_manifest_path", null) else null)
                ?: preferences.getString("manifest_path", null) ?: return
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
            if (visible && ready && !frameScheduled) { frameScheduled = true; handler.postDelayed(frame, 16) }
        }

        private fun draw() {
            if (!ready || !surfaceHolder.surface.isValid) return
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
