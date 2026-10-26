package com.livelayer.app

import kotlin.math.PI

/** Relative tilt, calibrated whenever a preview/wallpaper becomes visible. */
internal class TiltMotion {
    private var baseRoll: Float? = null
    private var basePitch = 0f
    var x = 0f; private set
    var y = 0f; private set

    fun reset() { baseRoll = null; x = 0f; y = 0f }

    fun update(roll: Float, pitch: Float): Pair<Float, Float> {
        if (!roll.isFinite() || !pitch.isFinite()) return x to y
        if (baseRoll == null) { baseRoll = roll; basePitch = pitch }
        fun relative(value: Float): Float {
            var angle = value.toDouble()
            while (angle > PI) angle -= 2 * PI
            while (angle < -PI) angle += 2 * PI
            return (angle / (PI / 6)).toFloat().coerceIn(-1f, 1f)
        }
        x += .18f * (relative(roll - baseRoll!!) - x)
        y += .18f * (relative(pitch - basePitch) - y)
        return x to y
    }
}
