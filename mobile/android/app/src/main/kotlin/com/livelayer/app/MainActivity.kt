package com.livelayer.app

import android.Manifest
import android.app.WallpaperManager
import android.content.ComponentName
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        CompanionRuntime.initialize(applicationContext)
        val companionState = CompanionStore(applicationContext).read()
        if (CompanionPrivacy(applicationContext).accepted && companionState.optBoolean("remember") && companionState.optBoolean("enabled") && companionState.has("session"))
            androidx.core.content.ContextCompat.startForegroundService(this, Intent(this, CompanionSyncService::class.java))
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "upscale/companion").setMethodCallHandler { call, result ->
            if (call.method == "status") { result.success(CompanionRuntime.status()); return@setMethodCallHandler }
            if (call.method in listOf("login", "logout", "disable", "reconnect")) {
                CompanionRuntime.invalidate()
                stopService(Intent(this, CompanionSyncService::class.java))
            }
            CompanionRuntime.action(call.method, call.arguments as? Map<*, *> ?: emptyMap<Any, Any>()) { value, error ->
                runOnUiThread {
                    if (error != null) result.error("companion_failed", error, null)
                    else try {
                        if (call.method == "activateLive") {
                            startActivity(Intent(WallpaperManager.ACTION_CHANGE_LIVE_WALLPAPER)
                                .putExtra(WallpaperManager.EXTRA_LIVE_WALLPAPER_COMPONENT, ComponentName(this, LiveLayerWallpaperService::class.java)))
                        } else if (call.method in listOf("login", "enable", "reconnect")) {
                            if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
                                requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 2102)
                            androidx.core.content.ContextCompat.startForegroundService(this, Intent(this, CompanionSyncService::class.java))
                        }
                        result.success(value)
                    } catch (failure: Exception) { result.error("companion_failed", failure.message, null) }
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        CompanionRuntime.pollSoon()
    }
}
