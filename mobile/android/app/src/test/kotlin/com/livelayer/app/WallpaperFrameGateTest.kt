package com.livelayer.app

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class WallpaperFrameGateTest {
    @Test fun stillSensorDoesNotWakeRendering() {
        assertFalse(WallpaperFrameGate.targetChanged(.25f, -.2f, .2504f, -.2002f))
        assertTrue(WallpaperFrameGate.targetChanged(.25f, -.2f, .27f, -.2f))
    }

    @Test fun frameLoopStopsWhenSettledAndResumesForMotion() {
        assertFalse(WallpaperFrameGate.needsAnotherFrame(true, true, .5f, 0f, .5001f, 0f))
        assertTrue(WallpaperFrameGate.needsAnotherFrame(true, true, .5f, 0f, .75f, 0f))
        assertFalse(WallpaperFrameGate.needsAnotherFrame(false, true, .5f, 0f, .75f, 0f))
        assertFalse(WallpaperFrameGate.needsAnotherFrame(true, false, .5f, 0f, .75f, 0f))
    }
}
