package com.livelayer.app

import org.json.JSONObject

internal object WallpaperMotion {
    fun usesGyro(manifest: JSONObject): Boolean {
        if (!manifest.optBoolean("motionEnabled", true)) return false
        val explicit = !manifest.isNull("mouseEnabled") || !manifest.isNull("gyroEnabled")
        return if (explicit) manifest.optBoolean("gyroEnabled", false) else true
    }
    fun refreshSettings(local: JSONObject, remote: JSONObject): JSONObject = local
        .put("motionEnabled", remote.optBoolean("motionEnabled", true))
        .put("mouseEnabled", remote.opt("mouseEnabled") ?: JSONObject.NULL)
        .put("gyroEnabled", remote.opt("gyroEnabled") ?: JSONObject.NULL)
}
