package com.livelayer.app

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

class LiveLayerWallpaperService : WallpaperService() {
    override fun onCreateEngine(): Engine = LiveLayerEngine()

    private inner class LiveLayerEngine : Engine(), SensorEventListener {
        private val handler = Handler(Looper.getMainLooper())
        private val sensorManager = getSystemService(SENSOR_SERVICE) as SensorManager
        private val accelerometer = sensorManager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)
        private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
        private var visible = false
        private var filteredX = 0f
        private var filteredY = 0f
        private val drawFrame = Runnable { draw() }

        override fun onVisibilityChanged(isVisible: Boolean) {
            visible = isVisible
            if (visible) {
                accelerometer?.let { sensorManager.registerListener(this, it, SensorManager.SENSOR_DELAY_GAME) }
                draw()
            } else {
                sensorManager.unregisterListener(this)
                handler.removeCallbacks(drawFrame)
            }
        }

        override fun onSurfaceDestroyed(holder: SurfaceHolder) {
            super.onSurfaceDestroyed(holder)
            visible = false
            sensorManager.unregisterListener(this)
            handler.removeCallbacks(drawFrame)
        }

        override fun onSensorChanged(event: SensorEvent) {
            val alpha = 0.12f
            filteredX += alpha * (event.values[0] - filteredX)
            filteredY += alpha * (event.values[1] - filteredY)
        }

        override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit

        private fun draw() {
            var canvas: Canvas? = null
            try {
                canvas = surfaceHolder.lockCanvas()
                if (canvas == null) return
                canvas.drawColor(Color.rgb(16, 14, 28))
                paint.color = Color.rgb(122, 92, 255)
                canvas.drawCircle(
                    canvas.width / 2f - filteredX * 24f,
                    canvas.height / 2f + filteredY * 20f,
                    canvas.width.coerceAtMost(canvas.height) * 0.22f,
                    paint,
                )
                paint.color = Color.WHITE
                paint.textAlign = Paint.Align.CENTER
                paint.textSize = 42f
                val projectId = getSharedPreferences("live_layer", MODE_PRIVATE)
                    .getString("project_id", "")
                    .orEmpty()
                val label = if (projectId.isBlank()) "LiveLayer" else "LiveLayer • ${projectId.take(8)}"
                canvas.drawText(label, canvas.width / 2f, canvas.height * 0.78f, paint)
            } finally {
                if (canvas != null) surfaceHolder.unlockCanvasAndPost(canvas)
            }
            handler.removeCallbacks(drawFrame)
            if (visible) handler.postDelayed(drawFrame, 33L)
        }
    }
}
