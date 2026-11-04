package com.livelayer.app

import org.junit.Assert.*
import org.junit.Test

class CompanionInventoryAccessTest {
    @Test fun detectionCallbackRunsOnlyAfterBothConsentAndAuthentication() {
        var reads = 0
        for (consent in listOf(false, true)) for (authenticated in listOf(false, true)) {
            val before = reads
            val value = CompanionInventoryAccess.read(consent, authenticated) { reads++; "display" }
            assertEquals(if (consent && authenticated) before + 1 else before, reads)
            assertEquals(if (consent && authenticated) "display" else null, value)
        }
        // Logout must gate the callback even when the previous consent remains valid.
        assertNull(CompanionInventoryAccess.read(true, false) { error("inventory after logout") })
    }
}
