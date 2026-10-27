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
    fun read(applied: JSONObject): JSONArray {
        val displays = (context.getSystemService(Context.DISPLAY_SERVICE) as DisplayManager).displays
            .filter { it.isValid && (it.flags and Display.FLAG_PRIVATE) == 0 }
        require(displays.size <= 16) { "최대 16개 화면을 연결할 수 있습니다." }
        val manager = WallpaperManager.getInstance(context)
        val output = JSONArray()
        for (display in displays) {
            val primary = display.displayId == Display.DEFAULT_DISPLAY
            val metrics = DisplayMetrics().also { display.getRealMetrics(it) }
            val size = Point().also { display.getRealSize(it) }
            val reports = JSONArray()
            if (primary) for (target in listOf("Home", "Lock")) {
                val flag = if (target == "Home") WallpaperManager.FLAG_SYSTEM else WallpaperManager.FLAG_LOCK
                val saved = applied.optJSONObject(target)
                val id = runCatching { manager.getWallpaperId(flag) }.getOrDefault(-1)
                val live = activeFlags() and flag != 0
                val activePath = context.getSharedPreferences("live_layer", Context.MODE_PRIVATE).getString("direct_manifest_path", null)
                val managed = saved != null && (if (saved.optString("mode") == "Live2D") live && activePath == saved.optString("manifest")
                    else !live && id > 0 && id == saved.optInt("wallpaperId"))
                reports.put(JSONObject().put("target", target).put("observation", if (managed) "Managed" else if (id > 0 || live) "External" else "Unavailable")
                    .put("assetId", if (managed) saved!!.getString("assetId") else JSONObject.NULL))
            }
            output.put(JSONObject().put("nativeId", if (primary) "android-built-in" else "android-display-${display.displayId}")
                .put("name", display.name.take(120)).put("width", size.x).put("height", size.y)
                .put("scale", metrics.density.toDouble().coerceIn(.25, 8.0)).put("rotation", display.rotation * 90)
                .put("isPrimary", primary).put("isConnected", true).put("offsetX", 0).put("offsetY", 0)
                .put("refreshRateHz", display.mode.refreshRate.toDouble().takeIf { it.isFinite() && it in 1.0..1000.0 } ?: JSONObject.NULL)
                .put("canApplyWallpaper", primary).put("wallpapers", reports))
        }
        return output
    }
    fun activeFlags(): Int {
        val manager = WallpaperManager.getInstance(context)
        val component = ComponentName(context, LiveLayerWallpaperService::class.java)
        val home = runCatching { manager.wallpaperInfo?.component == component }.getOrDefault(false)
        val lock = runCatching {
            if (Build.VERSION.SDK_INT >= 34) manager.getWallpaperInfo(WallpaperManager.FLAG_LOCK)?.component == component ||
                (home && manager.getWallpaperId(WallpaperManager.FLAG_LOCK) < 0)
            else home && manager.getWallpaperId(WallpaperManager.FLAG_LOCK) < 0
        }.getOrDefault(false)
        return (if (home) WallpaperManager.FLAG_SYSTEM else 0) or (if (lock) WallpaperManager.FLAG_LOCK else 0)
    }
}
