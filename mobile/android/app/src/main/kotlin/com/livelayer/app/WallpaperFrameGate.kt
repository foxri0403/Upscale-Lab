package com.livelayer.app

import kotlin.math.abs

/** Event-driven wallpaper animation: render while the gyro target is changing
 * or the visible pose has not settled, but do not redraw an idle wallpaper. */
internal object WallpaperFrameGate {
    fun targetChanged(oldX: Float, oldY: Float, newX: Float, newY: Float): Boolean =
        abs(newX - oldX) > .001f || abs(newY - oldY) > .001f

    fun needsAnotherFrame(canAnimate: Boolean, hasLayers: Boolean,
                          currentX: Float, currentY: Float, targetX: Float, targetY: Float): Boolean =
        canAnimate && hasLayers &&
            (abs(targetX - currentX) > .0008f || abs(targetY - currentY) > .0008f)
}
