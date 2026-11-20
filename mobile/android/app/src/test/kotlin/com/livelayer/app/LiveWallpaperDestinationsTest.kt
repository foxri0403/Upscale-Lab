package com.livelayer.app

import android.app.WallpaperManager.FLAG_LOCK
import android.app.WallpaperManager.FLAG_SYSTEM
import org.junit.Assert.*
import org.junit.Test

class LiveWallpaperDestinationsTest {
    @Test fun sharedEngineSwitchesToLockWhenKeyguardBecomesVisible() {
        val shared = LiveWallpaperDestinations.withInheritedLock(FLAG_SYSTEM, -1)
        assertEquals(FLAG_SYSTEM or FLAG_LOCK, shared)
        assertEquals(FLAG_SYSTEM, LiveWallpaperDestinations.frameTarget(shared, false))
        assertEquals(FLAG_LOCK, LiveWallpaperDestinations.frameTarget(shared, true))
        assertEquals(FLAG_LOCK, LiveWallpaperDestinations.frameTarget(FLAG_LOCK, false))
    }

    @Test fun separateStaticLockDoesNotFallBackToHomeEngine() {
        for (lockId in listOf(42, null)) {
            val destinations = LiveWallpaperDestinations.withInheritedLock(FLAG_SYSTEM, lockId)
            assertEquals(FLAG_SYSTEM, destinations)
            assertEquals(FLAG_SYSTEM, LiveWallpaperDestinations.frameTarget(destinations, true))
        }
        assertEquals(0, LiveWallpaperDestinations.withInheritedLock(0, -1))
    }

    @Test fun pickerAndFrameEvidenceRespectRequestedDestination() {
        assertFalse(LiveWallpaperDestinations.choiceIncludes(FLAG_LOCK, FLAG_SYSTEM))
        assertTrue(LiveWallpaperDestinations.choiceIncludes(FLAG_LOCK, FLAG_LOCK))
        assertTrue(LiveWallpaperDestinations.choiceIncludes(FLAG_LOCK, FLAG_SYSTEM or FLAG_LOCK))
        val engine = Any()
        try {
            CompanionLiveEngines.attach(engine, FLAG_SYSTEM)
            assertFalse(CompanionLiveEngines.rendered(engine, FLAG_LOCK, "lock"))
            assertTrue(CompanionLiveEngines.rendered(engine, FLAG_SYSTEM, "home"))
            CompanionLiveEngines.attach(engine, FLAG_SYSTEM or FLAG_LOCK)
            assertTrue(CompanionLiveEngines.rendered(engine, FLAG_LOCK, "lock"))
            assertTrue(CompanionLiveEngines.hasRendered(FLAG_LOCK, "lock"))
        } finally {
            CompanionLiveEngines.detach(engine)
        }
    }
}
