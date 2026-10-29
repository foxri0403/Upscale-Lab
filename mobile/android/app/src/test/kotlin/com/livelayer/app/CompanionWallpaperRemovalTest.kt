package com.livelayer.app

import java.io.InputStream
import java.io.IOException
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class CompanionWallpaperRemovalTest {
    private class Platform : WallpaperPlatform {
        override val sdkInt = 24
        val cleared = mutableListOf<WallpaperTarget>()
        var reject = false
        override fun apply(input: InputStream, target: WallpaperTarget) = error("not used")
        override fun clear(target: WallpaperTarget) { if (reject) throw IOException("OS rejected removal"); cleared.add(target) }
    }
    @Test fun removesOnlyRequestedScreenAndRetryDoesNotClearTwice() {
        val platform = Platform()
        val applied = JSONObject().put("Home", JSONObject().put("assetId", "home")).put("Lock", JSONObject().put("assetId", "lock"))
        var persisted: JSONObject? = null
        repeat(2) { CompanionWallpaperRemoval.clear(WallpaperTarget.HOME, "clear-id", applied, platform, { 42 }, {}, { persisted = it }) }
        assertEquals(listOf(WallpaperTarget.HOME), platform.cleared)
        assertTrue(CompanionWallpaperRemoval.isCleared(persisted!!.getJSONObject("Home"), 42))
        assertFalse(CompanionWallpaperRemoval.isCleared(persisted!!.getJSONObject("Home"), 43))
        assertEquals("lock", persisted!!.getJSONObject("Lock").getString("assetId"))
        assertFalse(persisted!!.getJSONObject("Home").has("assetId"))
    }
    @Test fun failedOsRemovalPreservesSavedWallpaperAndDoesNotPersistCompletion() {
        val applied = JSONObject().put("Lock", JSONObject().put("assetId", "original"))
        val platform = Platform().apply { reject = true }
        assertThrows(IOException::class.java) { CompanionWallpaperRemoval.clear(WallpaperTarget.LOCK, "id", applied, platform, { 5 }, { fail("not cleared") }, { fail("not persisted") }) }
        assertEquals("original", applied.getJSONObject("Lock").getString("assetId"))
    }
}
