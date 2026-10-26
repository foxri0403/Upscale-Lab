package com.livelayer.app

import org.junit.Assert.*
import org.junit.Test

class WallpaperBitmapBudgetTest {
    private fun checkBounds(width: Int, height: Int, limit: Int) {
        val sample = WallpaperBitmapBudget.sampleSize(width, height, limit)
        assertTrue(sample > 0)
        assertEquals(0, sample and (sample - 1))
        assertTrue((width.toLong() + sample - 1) / sample <= limit)
        assertTrue((height.toLong() + sample - 1) / sample <= limit)
    }

    @Test fun panoramaAndTallImagesStayBoundedOnEverySurfaceShape() {
        checkBounds(32000, 400, 1600)
        checkBounds(400, 32000, 1600)
        checkBounds(1601, 1599, 1600)
    }

    @Test fun lowMemoryDevicesUseTheSmallerBudget() {
        checkBounds(1600, 1600, 1024)
        assertTrue(WallpaperBitmapBudget.sampleSize(1600, 1600, 1024) > 1)
    }

    @Test fun corruptLargeDimensionsDoNotOverflowTheBudgetCalculation() {
        checkBounds(Int.MAX_VALUE, Int.MAX_VALUE, 1600)
    }

    @Test fun smallLayersKeepTheirOriginalResolution() {
        assertEquals(1, WallpaperBitmapBudget.sampleSize(720, 1280, 1600))
    }
}
