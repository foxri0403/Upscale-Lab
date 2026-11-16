package com.livelayer.app

import android.content.Context
import android.graphics.Point
import android.util.DisplayMetrics
import android.view.Display
import org.json.JSONObject

internal class CompanionFoldScreens(context: Context) {
    private val preferences = context.getSharedPreferences("companion_display_modes", Context.MODE_PRIVATE)
    private val hinge = context.packageManager.hasSystemFeature("android.hardware.sensor.hinge_angle")

    fun read(display: Display): List<JSONObject> {
        val metrics = DisplayMetrics().also { display.getRealMetrics(it) }
        val size = Point().also { display.getRealSize(it) }
        val current = JSONObject().put("width", size.x).put("height", size.y)
            .put("scale", metrics.density.toDouble().coerceIn(.25, 8.0)).put("rotation", display.rotation * 90)
            .put("refreshRateHz", display.mode.refreshRate.toDouble().takeIf { it.isFinite() && it in 1.0..1000.0 } ?: JSONObject.NULL)
        val kind = classify(size.x, size.y, metrics.density.toDouble())
        val previousCover = saved(COVER)
        val previousInner = saved(INNER)
        val cover = if (kind == COVER) current else previousCover
        val inner = if (kind == INNER) current else previousInner
        val foldable = preferences.getBoolean("bookFold", false) ||
            (inner != null && (hinge || (cover != null && distinctPanels(cover, inner))))

        if (kind != null && preferences.getString(kind, null) != current.toString()) {
            preferences.edit().putString(kind, current.toString()).putBoolean("bookFold", foldable).apply()
        }
        if (!foldable || kind == null) return listOf(current.put("nativeId", BUILT_IN).put("name", display.name.take(120)))
        val other = if (kind == COVER) inner else cover
        val otherId = if (kind == COVER) INNER else COVER
        return listOfNotNull(current.put("nativeId", kind).put("name", name(kind)),
            other?.put("nativeId", otherId)?.put("name", name(otherId)))
    }

    private fun saved(key: String) = preferences.getString(key, null)?.let { JSONObject(it) }

    companion object {
        const val BUILT_IN = "android-built-in"
        const val COVER = "android-fold-cover"
        const val INNER = "android-fold-inner"
        fun isFoldScreen(id: String) = id == COVER || id == INNER
        private fun name(id: String) = if (id == COVER) "외부 화면" else "내부 화면"

        // Use full display metrics, not the app window, so rotation, split screen
        // and letterboxing do not change the reported panel identity.
        fun classify(width: Int, height: Int, density: Double): String? {
            val short = minOf(width, height)
            if (short <= 0) return null
            val ratio = maxOf(width, height).toDouble() / short
            return when {
                ratio <= 1.55 && short / density >= 480 -> INNER
                ratio >= 1.65 && short / density < 640 -> COVER
                else -> null
            }
        }

        private fun shorter(mode: JSONObject) = minOf(mode.getInt("width"), mode.getInt("height"))
        private fun distinctPanels(cover: JSONObject, inner: JSONObject) = shorter(inner) >= shorter(cover) * 1.4
    }
}
