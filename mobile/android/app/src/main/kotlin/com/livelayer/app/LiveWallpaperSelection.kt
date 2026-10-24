package com.livelayer.app

/** A direct update cannot attach a service or change its Android destinations. */
internal object LiveWallpaperSelection {
    fun canUpdate(targetFlags: Int, activeFlags: Int): Boolean =
        activeFlags != 0 && targetFlags == activeFlags

    fun choose(basePath: String?, baseRevision: Long, directPath: String?, directRevision: Long): String? =
        if (directPath != null && (basePath == null || directRevision > baseRevision)) directPath else basePath
}
