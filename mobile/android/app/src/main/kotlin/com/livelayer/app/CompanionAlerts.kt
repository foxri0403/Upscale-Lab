package com.livelayer.app

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat

internal object CompanionAlerts {
    fun cancel(context: Context) = context.getSystemService(NotificationManager::class.java).cancel(2102)
    fun show(context: Context, enabled: Boolean, text: String) {
        if (!enabled || (Build.VERSION.SDK_INT >= 33 && ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)) return
        val manager = context.getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= 26) manager.createNotificationChannel(NotificationChannel("companion_events", "연결 앱 알람", NotificationManager.IMPORTANCE_DEFAULT))
        val open = PendingIntent.getActivity(context, 0, Intent(context, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        manager.notify(2102, NotificationCompat.Builder(context, "companion_events").setSmallIcon(android.R.drawable.ic_menu_gallery)
            .setContentTitle("Upscale Lab Connect").setContentText(text).setContentIntent(open).setAutoCancel(true).build())
    }
}
