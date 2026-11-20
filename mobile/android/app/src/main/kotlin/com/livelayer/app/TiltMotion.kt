package com.livelayer.app

import kotlin.math.PI
import kotlin.math.exp

/** Relative tilt, calibrated whenever a preview/wallpaper becomes visible. */
internal class TiltMotion {
    private var baseRoll: Float? = null
    private var basePitch = 0f
    private var lastSampleNanos = 0L
    var x = 0f; private set
    var y = 0f; private set

    fun reset() { baseRoll = null; lastSampleNanos = 0L; x = 0f; y = 0f }

    /** Time-based smoothing prevents the sensor callback rate from changing how motion feels. */
    fun update(roll: Float, pitch: Float, timestampNanos: Long = 0L): Pair<Float, Float> {
        if (!roll.isFinite() || !pitch.isFinite()) return x to y
        if (baseRoll == null) { baseRoll = roll; basePitch = pitch }
        fun relative(value: Float): Float {
            var angle = value.toDouble()
            while (angle > PI) angle -= 2 * PI
            while (angle < -PI) angle += 2 * PI
            return (angle / (PI / 6)).toFloat().coerceIn(-1f, 1f)
        }
        val seconds = if (timestampNanos > lastSampleNanos && lastSampleNanos > 0L)
            ((timestampNanos - lastSampleNanos) / 1_000_000_000.0).coerceIn(0.001, 0.1) else 1.0 / 60.0
        if (timestampNanos > 0L) lastSampleNanos = timestampNanos
        val alpha = (1.0 - exp(-seconds / .045)).toFloat()
        x += alpha * (relative(roll - baseRoll!!) - x)
        y += alpha * (relative(pitch - basePitch) - y)
        return x to y
    }
}
