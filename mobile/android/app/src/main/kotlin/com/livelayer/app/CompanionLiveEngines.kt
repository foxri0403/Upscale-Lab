package com.livelayer.app

import android.app.WallpaperManager

/** In-process evidence from installed engines. Preview engines never register. */
internal object CompanionLiveEngines {
    private data class Attachment(val flags: Int, val frames: MutableMap<Int, String> = mutableMapOf())
    private val engines = mutableMapOf<Any, Attachment>()

    @Synchronized fun attach(engine: Any, flags: Int) {
        if (engines[engine]?.flags != flags) engines[engine] = Attachment(flags)
    }

    @Synchronized fun detach(engine: Any) { engines.remove(engine) }
    @Synchronized fun activeFlags() = engines.values.fold(0) { flags, engine -> flags or engine.flags }

    @Synchronized fun rendered(engine: Any, target: Int, path: String): Boolean {
        val attachment = engines[engine] ?: return false
        if (attachment.flags and target == 0) return false
        return attachment.frames.put(target, path) != path
    }

    @Synchronized fun hasRendered(target: Int, path: String) =
        engines.values.any { it.frames[target] == path }

    @Synchronized fun reset(target: Int) {
        engines.values.forEach { it.frames.remove(target) }
    }

    fun selectionKey(target: Int) = if (target == WallpaperManager.FLAG_LOCK) "companion_live_lock" else "companion_live_home"
}
