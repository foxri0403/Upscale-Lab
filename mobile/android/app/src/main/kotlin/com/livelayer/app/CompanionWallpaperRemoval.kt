package com.livelayer.app

import org.json.JSONObject

internal object CompanionWallpaperRemoval {
    fun isCleared(receipt: JSONObject?, wallpaperId: Int): Boolean =
        receipt?.optBoolean("cleared") == true && receipt.optInt("wallpaperId", Int.MIN_VALUE) == wallpaperId

    fun clear(target: WallpaperTarget, commandId: String, applied: JSONObject, platform: WallpaperPlatform,
        readWallpaperId: () -> Int, afterRemoval: () -> Unit, persist: (JSONObject) -> Unit) {
        require(target != WallpaperTarget.BOTH) { "한 번에 한 화면을 삭제해 주세요." }
        val name = if (target == WallpaperTarget.HOME) "Home" else "Lock"
        val old = applied.optJSONObject(name)
        if (old?.optString("commandId") != commandId || !isCleared(old, readWallpaperId())) platform.clear(target)
        afterRemoval()
        val receipt = JSONObject().put("cleared", true).put("commandId", commandId).put("wallpaperId", readWallpaperId())
        persist(JSONObject(applied.toString()).put(name, receipt))
        applied.put(name, receipt)
    }
}
