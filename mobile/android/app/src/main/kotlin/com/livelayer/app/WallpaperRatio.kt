package com.livelayer.app

import kotlin.math.min

/** Matches the persisted Flutter settings; automatic mode is resolved per apply. */
data class WallpaperRatio(
    val automatic: Boolean = true,
    val preset: String = "original",
    val portrait: Boolean = true,
    val freeAspectRatio: Double? = null,
) {
    fun aspectRatio(sourceWidth: Int, sourceHeight: Int, displayWidth: Int, displayHeight: Int): Double {
        require(sourceWidth > 0 && sourceHeight > 0 && displayWidth > 0 && displayHeight > 0)
        if (automatic) return displayWidth.toDouble() / displayHeight
        return when (preset) {
            "wide" -> if (portrait) 9.0 / 16 else 16.0 / 9
            "standard" -> if (portrait) 3.0 / 4 else 4.0 / 3
            "square" -> 1.0
            "free" -> freeAspectRatio ?: sourceWidth.toDouble() / sourceHeight
            else -> sourceWidth.toDouble() / sourceHeight
        }
    }

    fun toMap(): Map<String, Any?> = mapOf(
        "mode" to if (automatic) "automatic" else "manual",
        "preset" to preset,
        "portrait" to portrait,
        "freeAspectRatio" to freeAspectRatio,
    )

    companion object {
        fun fromMap(value: Map<*, *>?): WallpaperRatio {
            val free = (value?.get("freeAspectRatio") as? Number)?.toDouble()
            return WallpaperRatio(
                automatic = value?.get("mode") != "manual",
                preset = (value?.get("preset") as? String)
                    ?.takeIf { it in setOf("original", "free", "wide", "standard", "square") } ?: "original",
                portrait = value?.get("portrait") != false,
                freeAspectRatio = free?.takeIf { it.isFinite() && it > 0 },
            )
        }

        /** A centered source crop without stretching or uncovered edges. */
        fun centerCrop(width: Int, height: Int, aspect: Double): Crop {
            require(width > 0 && height > 0 && aspect.isFinite() && aspect > 0)
            val cropWidth = min(width.toDouble(), height * aspect)
            val cropHeight = cropWidth / aspect
            return Crop((width - cropWidth) / 2, (height - cropHeight) / 2, cropWidth, cropHeight)
        }
    }

    data class Crop(val left: Double, val top: Double, val width: Double, val height: Double)
}
