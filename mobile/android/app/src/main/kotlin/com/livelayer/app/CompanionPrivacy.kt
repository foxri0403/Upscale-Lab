package com.livelayer.app

import android.content.Context

/** Consent survives logout, but does not transfer to a different server or notice version. */
internal class CompanionPrivacy(context: Context) {
    private val preferences = context.getSharedPreferences("companion_privacy", Context.MODE_PRIVATE)
    val accepted: Boolean get() = preferences.getInt("version", 0) == 1 &&
        preferences.getString("server", "") == BuildConfig.COMPANION_API_BASE_URL &&
        preferences.getLong("acceptedAtUtcMillis", 0) > 0
    fun accept() {
        check(preferences.edit().putInt("version", 1).putString("server", BuildConfig.COMPANION_API_BASE_URL)
            .putLong("acceptedAtUtcMillis", System.currentTimeMillis()).commit()) { "동의를 저장하지 못했습니다. 다시 시도해 주세요." }
    }
}

internal object CompanionInventoryAccess {
    fun <T> read(consent: Boolean, authenticated: Boolean, reader: () -> T): T? =
        if (consent && authenticated) reader() else null
}
