package com.livelayer.app

import org.junit.Assert.*
import org.junit.Test

class WallpaperEngineLifecycleTest {
    private class Harness {
        var selection = "old"; var loaded = ""; var starts = 0; var stops = 0; var frames = 0; var cancels = 0
        val engine = WallpaperEngineLifecycle({ loaded = selection }, { starts++ }, { stops++ }, { frames++ }, { cancels++ })
    }

    @Test fun commitsBeforeActualSurfaceExistsAreLoadedWhenItAppears() {
        val h = Harness()
        h.engine.visibilityChanged(true, false)
        h.selection = "new-live2d"
        h.engine.selectionChanged()
        assertEquals(0, h.starts)
        h.engine.surfaceCreated()
        assertEquals("new-live2d", h.loaded)
        assertEquals(1, h.starts)
        assertTrue(h.frames > 0)
    }

    @Test fun recreatedSurfaceRestartsSensorEvenWithoutAnotherVisibilityCallback() {
        val h = Harness()
        h.engine.surfaceCreated()
        h.engine.visibilityChanged(true, true)
        h.engine.surfaceDestroyed()
        assertEquals(1, h.stops)
        h.engine.surfaceCreated()
        assertEquals(2, h.starts)
        assertTrue(h.engine.surfaceReady)
    }

    @Test fun repeatedVisibleAndResizeEventsDoNotContinuouslyRecalibrateTilt() {
        val h = Harness()
        h.engine.visibilityChanged(true, true)
        repeat(10) { h.engine.visibilityChanged(true, true); h.engine.surfaceCreated() }
        assertEquals(1, h.starts)
        h.engine.visibilityChanged(false, true)
        assertEquals(1, h.stops)
        h.engine.visibilityChanged(true, true)
        assertEquals(2, h.starts)
    }

    @Test fun returningHomeRefreshesSelectionEvenWhenPreferenceNotificationWasMissed() {
        val h = Harness()
        h.engine.visibilityChanged(true, true)
        h.engine.visibilityChanged(false, true)
        h.selection = "chosen-live2d"
        h.engine.visibilityChanged(true, true)
        assertEquals("chosen-live2d", h.loaded)
        h.engine.destroy()
        assertFalse(h.engine.visible)
        assertFalse(h.engine.surfaceReady)
        assertEquals(2, h.stops)
    }

    @Test fun closingPreviewDoesNotStopActualWallpaperSensor() {
        val preview = Harness(); val actual = Harness()
        preview.engine.visibilityChanged(true, true)
        actual.engine.visibilityChanged(true, true)
        preview.engine.destroy()
        assertEquals(1, preview.stops)
        assertEquals(0, actual.stops)
        assertTrue(actual.engine.visible)
    }
}
