package com.livelayer.app

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.IBinder
import androidx.core.content.ContextCompat

class CompanionSyncService : Service() {
    override fun onBind(intent: Intent?): IBinder? = null
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        CompanionRuntime.initialize(this)
        val manager = getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= 26) manager.createNotificationChannel(NotificationChannel("companion_sync", "배경화면 동기화", NotificationManager.IMPORTANCE_LOW))
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val stop = PendingIntent.getService(this, 1, Intent(this, CompanionSyncService::class.java).setAction("stop"), PendingIntent.FLAG_IMMUTABLE)
        val builder = if (Build.VERSION.SDK_INT >= 26) Notification.Builder(this, "companion_sync") else Notification.Builder(this)
        startForeground(2101, builder.setContentTitle("Upscale Lab Connect").setContentText("배경화면 요청과 화면 정보를 동기화하고 있습니다.")
            .setSmallIcon(android.R.drawable.ic_menu_gallery).setContentIntent(open).setOngoing(true)
            .addAction(Notification.Action.Builder(null, "동기화 중지", stop).build()).build())
        if (intent?.action == "stop") {
            CompanionRuntime.action("disable", emptyMap<Any, Any>()) { _, _ -> stopSelf() }; return START_NOT_STICKY
        }
        if (!CompanionRuntime.resume()) { stopSelf(); return START_NOT_STICKY }
        return START_STICKY
    }
    override fun onDestroy() { CompanionRuntime.invalidate(); super.onDestroy() }
}

class CompanionBootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != Intent.ACTION_BOOT_COMPLETED) return
        val state = CompanionStore(context).read()
        if (state.optBoolean("remember") && state.optBoolean("enabled") && state.has("session"))
            runCatching { ContextCompat.startForegroundService(context, Intent(context, CompanionSyncService::class.java)) }
    }
}
