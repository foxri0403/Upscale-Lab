package com.livelayer.app

import java.io.File
import java.io.InputStream

internal interface WallpaperPlatform {
    val sdkInt: Int
    fun apply(input: InputStream, target: WallpaperTarget)
    fun clear(target: WallpaperTarget)
}

/** Keeps independent source copies; Android itself persists the applied images. */
internal class StaticWallpaperController(
    private val directory: File,
    private val platform: WallpaperPlatform,
) {
    // MainActivity serializes these operations on its existing IO executor.
    fun apply(target: WallpaperTarget, openSource: () -> InputStream) {
        target.requireSupported(platform.sdkInt)
        check(directory.isDirectory || directory.mkdirs()) { "배경화면 저장 폴더를 만들 수 없습니다." }
        val temporary = File.createTempFile("wallpaper-", ".img.part", directory)
        try {
            openSource().use { input -> temporary.outputStream().use { output -> input.copyTo(output) } }
            temporary.inputStream().use { platform.apply(it, target) }
            // Do not replace either previous source until Android accepts the image.
            target.screens.forEach { screen ->
                temporary.copyTo(File(directory, screen.cacheFileName), overwrite = true)
            }
        } finally {
            temporary.delete()
        }
    }

    fun clear(target: WallpaperTarget) {
        target.requireSupported(platform.sdkInt)
        platform.clear(target)
        target.screens.forEach { screen -> deleteCache(File(directory, screen.cacheFileName)) }
        if (WallpaperTarget.HOME in target.screens) {
            // The previous implementation kept only a home source at this path.
            deleteCache(File(directory, "current.img"))
        }
    }

    private fun deleteCache(file: File) {
        check(!file.exists() || file.delete()) { "보관된 배경화면 파일을 삭제할 수 없습니다." }
    }
}
