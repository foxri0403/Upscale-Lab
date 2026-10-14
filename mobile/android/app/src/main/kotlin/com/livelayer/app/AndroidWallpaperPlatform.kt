package com.livelayer.app

import android.app.WallpaperManager
import android.content.Context
import android.os.Build
import java.io.InputStream

internal class AndroidWallpaperPlatform(context: Context) : WallpaperPlatform {
    private val manager = WallpaperManager.getInstance(context)
    override val sdkInt: Int get() = Build.VERSION.SDK_INT

    override fun apply(input: InputStream, target: WallpaperTarget) {
        requireWallpaperAllowed()
        if (sdkInt >= Build.VERSION_CODES.N) {
            check(manager.setStream(input, null, true, target.flags) > 0) {
                "Android에서 배경화면 적용을 완료하지 못했습니다."
            }
        } else {
            manager.setStream(input)
        }
    }

    override fun clear(target: WallpaperTarget) {
        requireWallpaperAllowed()
        if (sdkInt >= Build.VERSION_CODES.N) {
            manager.clear(target.flags)
        } else {
            manager.clear()
        }
    }

    private fun requireWallpaperAllowed() {
        if (sdkInt >= Build.VERSION_CODES.M) {
            check(manager.isWallpaperSupported) { "이 기기는 배경화면 설정을 지원하지 않습니다." }
        }
        if (sdkInt >= Build.VERSION_CODES.N) {
            check(manager.isSetWallpaperAllowed) { "이 기기에서 배경화면 변경이 허용되지 않습니다." }
        }
    }
}
