package com.livelayer.app

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.view.Surface

/** Rotation vectors use Android's gyro sensor fusion and avoid integration drift. */
internal class TiltSensor(context: Context, private val rotation: () -> Int,
    private val changed: (Float, Float) -> Unit) : SensorEventListener {
    private val manager = context.getSystemService(Context.SENSOR_SERVICE) as SensorManager
    private val sensor = if (manager.getDefaultSensor(Sensor.TYPE_GYROSCOPE) == null) null
        else manager.getDefaultSensor(Sensor.TYPE_GAME_ROTATION_VECTOR)
            ?: manager.getDefaultSensor(Sensor.TYPE_ROTATION_VECTOR)
    private val matrix = FloatArray(9)
    private val remapped = FloatArray(9)
    private val angles = FloatArray(3)
    private val motion = TiltMotion()
    private var lastRotation = -1
    private var started = false

    fun start() {
        if (started) return
        motion.reset()
        changed(0f, 0f)
        sensor?.let { started = manager.registerListener(this, it, SensorManager.SENSOR_DELAY_GAME) }
    }

    fun stop() {
        if (started) manager.unregisterListener(this)
        started = false
        lastRotation = -1
    }

    override fun onSensorChanged(event: SensorEvent) {
        if (!started) return
        val displayRotation = rotation()
        if (displayRotation != lastRotation) { motion.reset(); lastRotation = displayRotation }
        SensorManager.getRotationMatrixFromVector(matrix, event.values)
        val axes = when (displayRotation) {
            Surface.ROTATION_90 -> SensorManager.AXIS_Y to SensorManager.AXIS_MINUS_X
            Surface.ROTATION_180 -> SensorManager.AXIS_MINUS_X to SensorManager.AXIS_MINUS_Y
            Surface.ROTATION_270 -> SensorManager.AXIS_MINUS_Y to SensorManager.AXIS_X
            else -> SensorManager.AXIS_X to SensorManager.AXIS_Y
        }
        SensorManager.remapCoordinateSystem(matrix, axes.first, axes.second, remapped)
        SensorManager.getOrientation(remapped, angles)
        val (x, y) = motion.update(angles[2], angles[1])
        changed(x, y)
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit
}
