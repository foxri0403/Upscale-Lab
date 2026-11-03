package com.livelayer.app

import android.Manifest
import android.app.WallpaperManager
import android.content.ComponentName
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine

class MainActivity : FlutterActivity() {
    private var liveSetupOpen = false
    private fun requestAlertsIfNeeded() {
        if (CompanionRuntime.alarmsEnabled() && Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 2102)
    }
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        CompanionRuntime.initialize(applicationContext)
        val saved = CompanionStore(applicationContext).read()
        if (CompanionPrivacy(applicationContext).accepted && saved.optBoolean("remember") && saved.has("session"))
            androidx.core.content.ContextCompat.startForegroundService(this, Intent(this, CompanionSyncService::class.java))
        io.flutter.plugin.common.MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "upscale/companion").setMethodCallHandler { call, result ->
            if (call.method == "status") { result.success(CompanionRuntime.status()); return@setMethodCallHandler }
            if (call.method in listOf("login", "logout")) {
                CompanionRuntime.invalidate()
                stopService(Intent(this, CompanionSyncService::class.java))
            }
            CompanionRuntime.action(call.method, call.arguments as? Map<*, *> ?: emptyMap<Any, Any>()) { value, error ->
                runOnUiThread {
                    if (error != null) result.error("companion_failed", error, null)
                    else try {
                        if (call.method == "activateLive" && (value as? Map<*, *>)?.get("openLivePicker") == true && !liveSetupOpen) {
                            liveSetupOpen = true
                            startActivityForResult(Intent(WallpaperManager.ACTION_CHANGE_LIVE_WALLPAPER)
                                .putExtra(WallpaperManager.EXTRA_LIVE_WALLPAPER_COMPONENT, ComponentName(this, LiveLayerWallpaperService::class.java)), 2103)
                        } else if (call.method in listOf("login", "enable")) {
                            requestAlertsIfNeeded()
                            androidx.core.content.ContextCompat.startForegroundService(this, Intent(this, CompanionSyncService::class.java))
                        } else if (call.method == "setAlarms") requestAlertsIfNeeded()
                        result.success(value)
                    } catch (failure: Exception) { liveSetupOpen = false; result.error("companion_failed", failure.message, null) }
                }
            }
        }
    }
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == 2103) {
            liveSetupOpen = false
            CompanionRuntime.action("completeLiveSetup", emptyMap<Any, Any>()) { _, _ -> }
        }
    }
    override fun onResume() { super.onResume(); CompanionRuntime.pollSoon() }
}
