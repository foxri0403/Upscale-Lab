package com.livelayer.app

/** Fallback for gyro-equipped devices which expose no fused rotation-vector sensor. */
internal class GyroIntegration {
    private var timestamp = 0L
    private var roll = 0f
    private var pitch = 0f
    fun reset() { timestamp = 0; roll = 0f; pitch = 0f }
    fun update(x: Float, y: Float, nanos: Long, rotation: Int): Pair<Float, Float> {
        if (!x.isFinite() || !y.isFinite() || nanos <= timestamp) return roll to pitch
        val elapsed = if (timestamp == 0L) 0f else ((nanos - timestamp) / 1_000_000_000f).coerceIn(0f, .1f)
        timestamp = nanos
        val axes = when (rotation) { 1 -> -x to y; 2 -> -y to -x; 3 -> x to -y; else -> y to x }
        roll = (roll + axes.first * elapsed).coerceIn(-1.05f, 1.05f)
        pitch = (pitch + axes.second * elapsed).coerceIn(-1.05f, 1.05f)
        return roll to pitch
    }
}
