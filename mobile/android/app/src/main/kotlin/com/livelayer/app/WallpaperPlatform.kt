package com.livelayer.app

import java.io.InputStream

/** OS boundary shared by companion wallpaper operations and their tests. */
internal interface WallpaperPlatform {
    val sdkInt: Int
    fun apply(input: InputStream, target: WallpaperTarget)
    fun clear(target: WallpaperTarget)
}
