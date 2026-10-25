package com.livelayer.app

/** Bound each decoded plane independently of wallpaper surface dimensions. */
internal object WallpaperBitmapBudget {
    fun sampleSize(width: Int, height: Int, maxSide: Int): Int {
        require(maxSide > 0)
        if (width <= 0 || height <= 0) return 1
        var sample = 1
        while ((width.toLong() + sample - 1) / sample > maxSide ||
            (height.toLong() + sample - 1) / sample > maxSide) {
            sample *= 2
        }
        return sample
    }
}
