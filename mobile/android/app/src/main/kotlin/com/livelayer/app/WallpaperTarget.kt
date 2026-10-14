package com.livelayer.app

import android.app.WallpaperManager

enum class WallpaperTarget(val flags: Int) {
    HOME(WallpaperManager.FLAG_SYSTEM),
    LOCK(WallpaperManager.FLAG_LOCK),
    BOTH(WallpaperManager.FLAG_SYSTEM or WallpaperManager.FLAG_LOCK);

    val screens: List<WallpaperTarget>
        get() = if (this == BOTH) listOf(HOME, LOCK) else listOf(this)

    val cacheFileName: String
        get() = when (this) {
            HOME -> "home.img"
            LOCK -> "lock.img"
            BOTH -> error("A cache file belongs to one screen.")
        }

    fun requireSupported(sdkInt: Int) {
        if (sdkInt < 24 && this != HOME) {
            throw UnsupportedOperationException("잠금화면 분리 설정은 Android 7.0 이상에서 지원됩니다.")
        }
    }

    companion object {
        // Older Flutter callers did not send a target and always changed home.
        fun fromArgument(value: Any?): WallpaperTarget = when (value) {
            null, "home" -> HOME
            "lock" -> LOCK
            "both" -> BOTH
            else -> throw IllegalArgumentException("배경화면 대상은 home, lock, both 중 하나여야 합니다.")
        }
    }
}
