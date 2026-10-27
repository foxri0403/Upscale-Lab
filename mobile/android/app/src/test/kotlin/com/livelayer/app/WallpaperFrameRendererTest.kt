package com.livelayer.app

import org.junit.Assert.*
import org.junit.Test

class WallpaperFrameRendererTest {
    @Test fun failedDrawingOrPostingCannotBeAcknowledgedAsAnAppliedLiveWallpaper() {
        assertFalse(WallpaperFrameRenderer.render({ Any() }, { throw IllegalStateException("draw failed") }, {}, {}))
        assertFalse(WallpaperFrameRenderer.render({ Any() }, {}, { throw IllegalArgumentException("post failed") }, {}))
        assertFalse(WallpaperFrameRenderer.render<Any>({ null }, {}, {}, {}))
        assertTrue(WallpaperFrameRenderer.render({ Any() }, {}, {}, {}))
    }
    @Test fun missingSurfaceDoesNotDrawOrPost() {
        WallpaperFrameRenderer.render<Any>({ null }, { fail("draw") }, { fail("post") }, { fail("error") })
    }

    @Test fun surfaceLossWhileAcquiringDoesNotEscapeTheEngine() {
        val failure = IllegalStateException("surface destroyed")
        val failures = mutableListOf<RuntimeException>()
        WallpaperFrameRenderer.render<Any>({ throw failure }, { fail("draw") }, { fail("post") }, { failures.add(it) })
        assertEquals(listOf(failure), failures)
    }

    @Test fun drawingFailureStillReleasesTheAcquiredFrame() {
        val frame = Any()
        var posted: Any? = null
        var failed = false
        WallpaperFrameRenderer.render({ frame }, { throw IllegalStateException("recycled bitmap") },
            { posted = it }, { failed = true })
        assertSame(frame, posted)
        assertTrue(failed)
    }

    @Test fun surfaceLossWhenPostingDoesNotCrashTheWallpaperProcess() {
        var failed = false
        WallpaperFrameRenderer.render({ Any() }, {}, { throw IllegalArgumentException("invalid surface") }, { failed = true })
        assertTrue(failed)
    }

    @Test fun normalFrameIsDrawnThenPostedExactlyOnce() {
        val events = mutableListOf<String>()
        WallpaperFrameRenderer.render({ Any() }, { events.add("draw") }, { events.add("post") }, { fail("error") })
        assertEquals(listOf("draw", "post"), events)
    }
}
