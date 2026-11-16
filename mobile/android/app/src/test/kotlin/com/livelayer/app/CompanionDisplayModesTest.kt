package com.livelayer.app

import android.app.WallpaperManager
import org.junit.Assert.*
import org.junit.Test

class CompanionDisplayModesTest {
    @Test fun foldPanelsAreIdentifiedFromFullDisplayDimensionsInEitherOrientation() {
        assertEquals(CompanionFoldScreens.COVER, CompanionFoldScreens.classify(904, 2316, 2.5))
        assertEquals(CompanionFoldScreens.COVER, CompanionFoldScreens.classify(2316, 904, 2.5))
        assertEquals(CompanionFoldScreens.INNER, CompanionFoldScreens.classify(1812, 2176, 2.75))
        assertEquals(CompanionFoldScreens.INNER, CompanionFoldScreens.classify(2176, 1812, 2.75))
        assertNull(CompanionFoldScreens.classify(1600, 2560, 2.0))
    }

    @Test fun onlyAnAttachedEngineCanConfirmAFrameForTheRequestedTarget() {
        val engine = Any()
        val home = WallpaperManager.FLAG_SYSTEM
        val lock = WallpaperManager.FLAG_LOCK
        try {
            assertFalse(CompanionLiveEngines.rendered(engine, home, "photo"))
            CompanionLiveEngines.attach(engine, home or lock)
            assertEquals(home or lock, CompanionLiveEngines.activeFlags())
            assertTrue(CompanionLiveEngines.rendered(engine, home, "photo"))
            assertTrue(CompanionLiveEngines.rendered(engine, lock, "character"))
            assertFalse(CompanionLiveEngines.hasRendered(lock, "photo"))
            CompanionLiveEngines.reset(home)
            assertFalse(CompanionLiveEngines.hasRendered(home, "photo"))
            assertTrue(CompanionLiveEngines.hasRendered(lock, "character"))
            CompanionLiveEngines.detach(engine)
            assertFalse(CompanionLiveEngines.hasRendered(lock, "character"))
        } finally {
            CompanionLiveEngines.detach(engine)
        }
    }
}
