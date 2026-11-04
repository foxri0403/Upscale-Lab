package com.livelayer.app

import android.content.Context

internal class CompanionPreferences(context: Context) {
    private val values = context.getSharedPreferences("companion_settings", Context.MODE_PRIVATE)
    private fun key(owner: String, name: String) = BuildConfig.COMPANION_API_BASE_URL + ":" + owner + ":" + name
    fun live(owner: String) = values.getBoolean(key(owner, "live"), false)
    fun alarms(owner: String) = values.getBoolean(key(owner, "alarms"), true)
    fun setLive(owner: String, enabled: Boolean) { check(values.edit().putBoolean(key(owner, "live"), enabled).commit()) }
    fun setAlarms(owner: String, enabled: Boolean) { check(values.edit().putBoolean(key(owner, "alarms"), enabled).commit()) }
}

internal object LiveActivationPolicy {
    fun needsPicker(enabled: Boolean, hasManifest: Boolean, active: Boolean, attempted: Boolean) = enabled && hasManifest && !active && !attempted
    fun mayAnimate(enabled: Boolean, matchingSession: Boolean, validUntil: Long, now: Long) = enabled && matchingSession && validUntil > now
}
