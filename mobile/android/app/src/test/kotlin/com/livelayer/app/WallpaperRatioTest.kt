package com.livelayer.app

import org.junit.Assert.*
import org.junit.Test

class WallpaperRatioTest {
    @Test fun automaticUsesApplyingDisplayRegardlessOfSourceOrManualOrientation() {
        val ratio = WallpaperRatio(automatic = true, preset = "wide", portrait = false)
        assertEquals(1080.0 / 2400, ratio.aspectRatio(4000, 2000, 1080, 2400), 1e-9)
        assertEquals(2400.0 / 1080, ratio.aspectRatio(2000, 4000, 2400, 1080), 1e-9)
        assertEquals(1.0, ratio.aspectRatio(4000, 2000, 1800, 1800), 1e-9)
    }

    @Test fun manualChoiceIsIndependentOfDisplayAndSurvivesRoundTrip() {
        for ((preset, portrait, expected) in listOf(
            Triple("wide", true, 9.0 / 16), Triple("wide", false, 16.0 / 9),
            Triple("standard", true, 3.0 / 4), Triple("standard", false, 4.0 / 3),
            Triple("square", true, 1.0), Triple("original", true, 2.0),
        )) {
            val ratio = WallpaperRatio.fromMap(WallpaperRatio(false, preset, portrait).toMap())
            for ((width, height) in listOf(1080 to 2400, 2400 to 1080, 1800 to 1800)) {
                assertEquals(expected, ratio.aspectRatio(4000, 2000, width, height), 1e-9)
            }
        }
        val free = WallpaperRatio.fromMap(mapOf("mode" to "manual", "preset" to "free", "freeAspectRatio" to 1.7))
        assertEquals(1.7, free.aspectRatio(4000, 2000, 1080, 2400), 1e-9)
        assertTrue(WallpaperRatio.fromMap(null).automatic)
        assertNull(WallpaperRatio.fromMap(mapOf("freeAspectRatio" to Double.NaN)).freeAspectRatio)
    }

    @Test fun centeredCropCoversScreenWithoutStretchingLandscapePortraitOrSquareSources() {
        for ((width, height) in listOf(4000 to 2000, 2000 to 4000, 3000 to 3000)) {
            for (aspect in listOf(1080.0 / 2400, 2400.0 / 1080, 1.0)) {
                val crop = WallpaperRatio.centerCrop(width, height, aspect)
                assertEquals(aspect, crop.width / crop.height, 1e-9)
                assertEquals(width / 2.0, crop.left + crop.width / 2, 1e-9)
                assertEquals(height / 2.0, crop.top + crop.height / 2, 1e-9)
                assertTrue(crop.left >= 0 && crop.top >= 0)
                assertTrue(crop.left + crop.width <= width && crop.top + crop.height <= height)
            }
        }
    }
}
