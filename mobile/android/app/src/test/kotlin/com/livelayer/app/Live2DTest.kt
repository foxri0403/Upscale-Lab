package com.livelayer.app

import org.junit.Assert.*
import org.junit.Test
import kotlin.math.PI

class Live2DTest {
    @Test fun foregroundRetainsColourAndSoftAlphaWithoutBackground() {
        val pixels = intArrayOf(0xff336699.toInt(), 0xffabcdef.toInt(), 0x80334455.toInt())
        val output = LayerPixels.foreground(pixels, floatArrayOf(0f, 1f, .5f))
        assertEquals(0, output[0] ushr 24)
        assertEquals(pixels[1], output[1])
        assertEquals(64, output[2] ushr 24)
        assertEquals(0x334455, output[2] and 0xffffff)
    }

    @Test fun backgroundHoleUsesOnlyObservedBackgroundAndRemovesSubject() {
        val blue = 0xff103080.toInt()
        val pixels = IntArray(25) { blue }
        val holes = BooleanArray(25)
        for (y in 1..3) for (x in 1..3) { pixels[y * 5 + x] = 0xffff0000.toInt(); holes[y * 5 + x] = true }
        val plate = LayerPixels.inpaint(pixels, holes, 5, 5)
        assertTrue(plate.all { it == blue })
        assertEquals(0xffff0000.toInt(), pixels[12]) // Source array is never overwritten.
    }

    @Test(expected = IllegalArgumentException::class)
    fun rejectsImagesWithNoBackgroundInsteadOfReturningAnUnchangedSubject() {
        LayerPixels.inpaint(IntArray(4), BooleanArray(4) { true }, 2, 2)
    }

    @Test fun tiltCalibratesBoundsWrapsAndResets() {
        val motion = TiltMotion()
        assertEquals(0f to 0f, motion.update(1f, -.4f))
        repeat(100) { motion.update(2f, -2f) }
        assertEquals(1f, motion.x, .001f)
        assertEquals(-1f, motion.y, .001f)
        motion.reset()
        assertEquals(0f to 0f, motion.update(-2f, .4f))
        assertEquals(0f to 0f, motion.update(Float.NaN, 0f))
        motion.reset()
        motion.update((PI - .01).toFloat(), 0f)
        val value = motion.update((-PI + .01).toFloat(), 0f)
        assertTrue(value.first > 0f && value.first < .02f)
    }
}
