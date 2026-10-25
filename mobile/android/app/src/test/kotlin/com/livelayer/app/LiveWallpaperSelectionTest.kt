package com.livelayer.app

import org.junit.Assert.*
import org.junit.Test

class LiveWallpaperSelectionTest {
    @Test fun explicitSettingsCanChangeDestinationsAfterAnyPreviousSelection() {
        for (active in 0..3) {
            assertFalse(LiveWallpaperSelection.canActivateDirect(active, null, forcePreview = true))
            for (requested in 1..3) {
                assertFalse(LiveWallpaperSelection.canActivateDirect(active, requested, forcePreview = true))
            }
        }
    }
    @Test fun activationReusesAnActiveServiceWithoutOpeningAnotherPreview() {
        assertFalse(LiveWallpaperSelection.canActivateDirect(0, null))
        for (active in 1..3) {
            assertTrue(LiveWallpaperSelection.canActivateDirect(active, null))
            assertTrue(LiveWallpaperSelection.canActivateDirect(active, active))
        }
        assertFalse(LiveWallpaperSelection.canActivateDirect(1, 3))
        assertFalse(LiveWallpaperSelection.canActivateDirect(3, 2))
    }
    @Test fun directUpdatesRequireTheAlreadyActiveAndroidDestinations() {
        for (active in 0..3) for (requested in 1..3) {
            assertEquals(active != 0 && requested == active,
                LiveWallpaperSelection.canUpdate(requested, active))
        }
        // In particular, updating a shared engine must not silently change the other screen.
        assertFalse(LiveWallpaperSelection.canUpdate(1, 3))
        assertFalse(LiveWallpaperSelection.canUpdate(2, 3))
    }

    @Test fun directUpdatesSurviveEngineRecreationButNewExplicitSelectionsWin() {
        assertEquals("new", LiveWallpaperSelection.choose("old-description", 1, "new", 2))
        assertEquals("updated-again", LiveWallpaperSelection.choose("confirmed", 3, "updated-again", 4))
        assertEquals("confirmed", LiveWallpaperSelection.choose("confirmed", 3, "new", 2))
        assertEquals("confirmed", LiveWallpaperSelection.choose("confirmed", 3, null, 0))
        assertEquals("new", LiveWallpaperSelection.choose(null, 0, "new", 2))
        assertNull(LiveWallpaperSelection.choose(null, 0, null, 0))
    }
}
