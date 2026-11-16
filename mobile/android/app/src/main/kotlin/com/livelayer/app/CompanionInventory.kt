package com.livelayer.app

import android.app.WallpaperManager
import android.content.ComponentName
import android.content.Context
import android.graphics.Point
import android.hardware.display.DisplayManager
import android.os.Build
import android.util.DisplayMetrics
import android.view.Display
import org.json.JSONArray
import org.json.JSONObject

internal class CompanionInventory(private val context: Context) {
    fun activeId(): String {
        val manager = context.getSystemService(Context.DISPLAY_SERVICE) as DisplayManager
        val display = manager.getDisplay(Display.DEFAULT_DISPLAY) ?: return CompanionFoldScreens.BUILT_IN
        return CompanionFoldScreens(context).read(display).first().getString("nativeId")
    }

    fun read(applied: JSONObject): JSONArray {
        check(CompanionRuntime.inventoryAllowed()) { "로그인과 정보 수집 동의 후에만 화면을 감지할 수 있습니다." }
        val displays = (context.getSystemService(Context.DISPLAY_SERVICE) as DisplayManager).displays
            .filter { it.isValid && (it.flags and Display.FLAG_PRIVATE) == 0 }
        val manager = WallpaperManager.getInstance(context)
        val liveFlags = activeFlags()
        val output = JSONArray()
        for (display in displays) {
            val primary = display.displayId == Display.DEFAULT_DISPLAY
            if (primary) {
                val profiles = CompanionFoldScreens(context).read(display)
                for ((index, profile) in profiles.withIndex()) {
                    val active = index == 0
                    val nativeId = profile.getString("nativeId")
                    val receipts = if (CompanionFoldScreens.isFoldScreen(nativeId)) applied.optJSONObject(nativeId) else applied
                    val reports = JSONArray()
                    if (active) for (target in listOf("Home", "Lock")) {
                        val flag = if (target == "Home") WallpaperManager.FLAG_SYSTEM else WallpaperManager.FLAG_LOCK
                        val saved = receipts?.optJSONObject(target)
                        val id = runCatching { manager.getWallpaperId(flag) }.getOrDefault(-1)
                        val live = liveFlags and flag != 0
                        val path = saved?.optString("manifest")
                        val cleared = CompanionWallpaperRemoval.isCleared(saved, id)
                        val managed = saved != null && !saved.optBoolean("cleared") && (if (saved.optString("mode") == "Live2D")
                            live && !path.isNullOrBlank() && CompanionLiveEngines.hasRendered(flag, path)
                        else !live && id > 0 && id == saved.optInt("wallpaperId"))
                        reports.put(JSONObject().put("target", target).put("observation", if (cleared) "Cleared" else if (managed) "Managed" else if (id > 0 || live) "External" else "Unavailable")
                            .put("assetId", if (managed) saved!!.getString("assetId") else JSONObject.NULL))
                    }
                    output.put(profile.put("isPrimary", active).put("isConnected", true).put("canApplyWallpaper", true)
                        .put("offsetX", 0).put("offsetY", 0).put("wallpapers", reports))
                }
                continue
            }
            val metrics = DisplayMetrics().also { display.getRealMetrics(it) }
            val size = Point().also { display.getRealSize(it) }
            output.put(JSONObject().put("nativeId", "android-display-${display.displayId}")
                .put("name", display.name.take(120)).put("width", size.x).put("height", size.y)
                .put("scale", metrics.density.toDouble().coerceIn(.25, 8.0)).put("rotation", display.rotation * 90)
                .put("isPrimary", false).put("isConnected", true).put("offsetX", 0).put("offsetY", 0)
                .put("refreshRateHz", display.mode.refreshRate.toDouble().takeIf { it.isFinite() && it in 1.0..1000.0 } ?: JSONObject.NULL)
                .put("canApplyWallpaper", false).put("wallpapers", JSONArray()))
        }
        require(output.length() <= 16) { "최대 16개 화면을 연결할 수 있습니다." }
        return output
    }
    fun activeFlags(): Int {
        val manager = WallpaperManager.getInstance(context)
        val component = ComponentName(context, LiveLayerWallpaperService::class.java)
        var flags = CompanionLiveEngines.activeFlags()
        if (Build.VERSION.SDK_INT >= 34) {
            for (target in listOf(WallpaperManager.FLAG_SYSTEM, WallpaperManager.FLAG_LOCK)) {
                if (runCatching { manager.getWallpaperInfo(target)?.component == component }.getOrDefault(false))
                    flags = flags or target
            }
        }
        val lockId = runCatching { manager.getWallpaperId(WallpaperManager.FLAG_LOCK) }.getOrNull()
        return LiveWallpaperDestinations.withInheritedLock(flags, lockId)
    }
}
