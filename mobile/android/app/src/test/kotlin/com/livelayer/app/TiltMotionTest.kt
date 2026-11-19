package com.livelayer.app

import org.junit.Assert.*
import org.junit.Test

class TiltMotionTest {
    @Test fun smoothingUsesElapsedSensorTimeAndResetsWithoutDrift() {
        val motion = TiltMotion()
        motion.update(0f, 0f, 1_000_000_000L)
        val a = motion.update(.5f, 0f, 1_020_000_000L)
        assertTrue(a.first > 0f && a.first < 1f)
        val b = motion.update(.5f, 0f, 1_040_000_000L)
        assertTrue(b.first > a.first && b.first <= 1f)
        motion.reset()
        assertEquals(0f, motion.x, 0f)
        assertEquals(0f, motion.y, 0f)
    }
}
