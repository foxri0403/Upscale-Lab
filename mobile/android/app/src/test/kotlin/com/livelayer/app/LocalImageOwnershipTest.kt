package com.livelayer.app

import org.json.JSONArray
import org.junit.Assert.*
import org.junit.Test

class LocalImageOwnershipTest {
    private fun records() = JSONArray("""[
        {"id":"a","ownerScope":"server-account-a","tag":"동물"},
        {"id":"b","ownerScope":"server-account-b","tag":"풍경"},
        {"id":"legacy","tag":"SF"}
    ]""")

    @Test fun switchingAccountsOnlyShowsOwnedRecords() {
        val all = records()
        assertEquals("a", LocalImageOwnership.owned(all, "server-account-a").getJSONObject(0).getString("id"))
        assertEquals("b", LocalImageOwnership.owned(all, "server-account-b").getJSONObject(0).getString("id"))
        assertEquals(0, LocalImageOwnership.owned(all, "another-server-account-a").length())
    }

    @Test fun editingAndDeletingNeverChangesAnotherAccountOrLegacyRecords() {
        val all = records()
        val own = LocalImageOwnership.owned(all, "server-account-a")
        assertEquals(1, own.length())
        own.getJSONObject(0).put("tag", "레트로")
        val edited = LocalImageOwnership.replaceOwned(all, "server-account-a", own)
        assertEquals("풍경", LocalImageOwnership.owned(edited, "server-account-b").getJSONObject(0).getString("tag"))
        val deleted = LocalImageOwnership.replaceOwned(edited, "server-account-a", JSONArray())
        assertEquals(2, deleted.length())
        assertEquals(1, LocalImageOwnership.owned(deleted, "server-account-b").length())
        assertEquals(0, LocalImageOwnership.owned(deleted, "server-account-a").length())
        assertEquals("legacy", deleted.getJSONObject(1).getString("id"))
    }

    @Test(expected = IllegalArgumentException::class)
    fun missingAccountCannotReadUnownedRecords() {
        LocalImageOwnership.owned(records(), "")
    }
}
