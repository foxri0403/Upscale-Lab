package com.livelayer.app

import android.app.WallpaperManager
import android.content.ComponentName
import android.content.Intent
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "live_layer/wallpaper")
            .setMethodCallHandler { call, result ->
                if (call.method != "openWallpaperPicker") {
                    result.notImplemented()
                    return@setMethodCallHandler
                }

                val projectId = call.argument<String>("projectId")
                getSharedPreferences("live_layer", MODE_PRIVATE)
                    .edit()
                    .putString("project_id", projectId)
                    .apply()
                val intent = Intent(WallpaperManager.ACTION_CHANGE_LIVE_WALLPAPER).apply {
                    putExtra(
                        WallpaperManager.EXTRA_LIVE_WALLPAPER_COMPONENT,
                        ComponentName(this@MainActivity, LiveLayerWallpaperService::class.java),
                    )
                }
                startActivity(intent)
                result.success(null)
            }
    }
}
