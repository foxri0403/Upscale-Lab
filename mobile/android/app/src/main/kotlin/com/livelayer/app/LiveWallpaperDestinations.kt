package com.livelayer.app

import android.app.WallpaperManager.FLAG_LOCK
import android.app.WallpaperManager.FLAG_SYSTEM

internal object LiveWallpaperDestinations {
    const val BOTH = FLAG_SYSTEM or FLAG_LOCK

    fun withInheritedLock(flags: Int, lockWallpaperId: Int?): Int =
        if (flags and FLAG_SYSTEM != 0 && lockWallpaperId != null && lockWallpaperId < 0) flags or FLAG_LOCK else flags

    fun frameTarget(flags: Int, keyguardLocked: Boolean): Int =
        if (flags == FLAG_LOCK || flags == BOTH && keyguardLocked) FLAG_LOCK else FLAG_SYSTEM

    fun choiceIncludes(destination: Int, flags: Int) = destination == 0 || destination and flags != 0
}
