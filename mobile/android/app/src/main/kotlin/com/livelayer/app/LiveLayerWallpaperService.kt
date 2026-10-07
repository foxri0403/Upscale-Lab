package com.livelayer.app

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Handler
import android.os.Looper
import android.service.wallpaper.WallpaperService
import android.view.SurfaceHolder
import org.json.JSONObject
import java.io.File
import kotlin.math.max

class LiveLayerWallpaperService : WallpaperService() {
    override fun onCreateEngine(): Engine = LiveLayerEngine()

    private data class Layer(
        val bitmap: Bitmap,
        val order: Int,
        val positionX: Float,
        val positionY: Float,
        val rotation: Float,
        val scale: Float,
        val movementX: Float,
        val movementY: Float,
    )

    private inner class LiveLayerEngine : Engine(), SensorEventListener {
        private val handler = Handler(Looper.getMainLooper())
        private val sensorManager = getSystemService(SENSOR_SERVICE) as SensorManager
        private val accelerometer = sensorManager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)
        private val paint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
        private var visible = false
        private var filteredX = 0f
        private var filteredY = 0f
        private var sensitivity = 1f
        private var layers: List<Layer> = emptyList()
        private var fallback: Bitmap? = null
        private val drawFrame = Runnable { draw() }

        override fun onVisibilityChanged(isVisible: Boolean) {
            visible = isVisible
            if (visible) {
                accelerometer?.let {
                    sensorManager.registerListener(this, it, SensorManager.SENSOR_DELAY_GAME)
                }
                draw()
            } else {
                sensorManager.unregisterListener(this)
                handler.removeCallbacks(drawFrame)
            }
        }

        override fun onSurfaceChanged(
            holder: SurfaceHolder,
            format: Int,
            width: Int,
            height: Int,
        ) {
            super.onSurfaceChanged(holder, format, width, height)
            if (layers.isEmpty() && fallback == null) {
                loadActiveProject(width, height)
            }
            draw()
        }

        override fun onSurfaceDestroyed(holder: SurfaceHolder) {
            super.onSurfaceDestroyed(holder)
            visible = false
            sensorManager.unregisterListener(this)
            handler.removeCallbacks(drawFrame)
        }

        override fun onDestroy() {
            layers.forEach { it.bitmap.recycle() }
            fallback?.recycle()
            layers = emptyList()
            fallback = null
            super.onDestroy()
        }

        override fun onSensorChanged(event: SensorEvent) {
            val alpha = 0.12f
            filteredX += alpha * (event.values[0] - filteredX)
            filteredY += alpha * (event.values[1] - filteredY)
        }

        override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit

        private fun loadActiveProject(targetWidth: Int, targetHeight: Int) {
            val manifestPath = getSharedPreferences("live_layer", MODE_PRIVATE)
                .getString("manifest_path", null) ?: return
            runCatching {
                val manifestFile = File(manifestPath)
                val directory = manifestFile.parentFile ?: return@runCatching
                val manifest = JSONObject(manifestFile.readText())
                sensitivity = manifest.optDouble("sensitivity", 1.0).toFloat().coerceIn(0f, 2f)
                fallback = decodeSampledBitmap(
                    File(directory, manifest.optString("originalFileName", "original.img")),
                    targetWidth,
                    targetHeight,
                )
                val items = manifest.getJSONArray("layers")
                layers = buildList {
                    for (index in 0 until items.length()) {
                        val item = items.getJSONObject(index)
                        val bitmap = decodeSampledBitmap(
                            File(directory, item.getString("fileName")),
                            targetWidth,
                            targetHeight,
                        ) ?: continue
                        add(
                            Layer(
                                bitmap = bitmap,
                                order = item.optInt("layerOrder", index),
                                positionX = item.optDouble("positionX", 0.0).toFloat(),
                                positionY = item.optDouble("positionY", 0.0).toFloat(),
                                rotation = item.optDouble("rotation", 0.0).toFloat(),
                                scale = item.optDouble("scale", 1.0).toFloat(),
                                movementX = item.optDouble("movementX", 0.0).toFloat(),
                                movementY = item.optDouble("movementY", 0.0).toFloat(),
                            ),
                        )
                    }
                }.sortedBy { it.order }
            }
        }

        private fun decodeSampledBitmap(
            file: File,
            targetWidth: Int,
            targetHeight: Int,
        ): Bitmap? {
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeFile(file.absolutePath, bounds)
            if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null

            var sampleSize = 1
            while (
                bounds.outWidth / (sampleSize * 2) >= targetWidth &&
                bounds.outHeight / (sampleSize * 2) >= targetHeight
            ) {
                sampleSize *= 2
            }
            return BitmapFactory.decodeFile(
                file.absolutePath,
                BitmapFactory.Options().apply { inSampleSize = sampleSize },
            )
        }

        private fun draw() {
            var canvas: Canvas? = null
            try {
                canvas = surfaceHolder.lockCanvas() ?: return
                canvas.drawColor(Color.BLACK)
                if (layers.isEmpty()) {
                    fallback?.let { drawBitmap(canvas, it, 0f, 0f, 0f, 1f) }
                } else {
                    layers.forEach { layer ->
                        drawBitmap(
                            canvas,
                            layer.bitmap,
                            layer.positionX - filteredX * layer.movementX * sensitivity,
                            layer.positionY + filteredY * layer.movementY * sensitivity,
                            layer.rotation,
                            layer.scale,
                        )
                    }
                }
            } finally {
                if (canvas != null) surfaceHolder.unlockCanvasAndPost(canvas)
            }
            handler.removeCallbacks(drawFrame)
            if (visible) handler.postDelayed(drawFrame, 33L)
        }

        private fun drawBitmap(
            canvas: Canvas,
            bitmap: Bitmap,
            offsetX: Float,
            offsetY: Float,
            rotation: Float,
            layerScale: Float,
        ) {
            val coverScale = max(
                canvas.width.toFloat() / bitmap.width,
                canvas.height.toFloat() / bitmap.height,
            )
            val scale = coverScale * layerScale
            canvas.save()
            canvas.translate(canvas.width / 2f + offsetX, canvas.height / 2f + offsetY)
            canvas.rotate(rotation)
            canvas.scale(scale, scale)
            canvas.drawBitmap(bitmap, -bitmap.width / 2f, -bitmap.height / 2f, paint)
            canvas.restore()
        }
    }
}
