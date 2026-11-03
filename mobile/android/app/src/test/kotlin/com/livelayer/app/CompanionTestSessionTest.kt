package com.livelayer.app

import org.junit.Assert.*
import org.junit.Test

class CompanionTestSessionTest {
    @Test fun anyIdentifierIncludingEmptyStartsAnInMemoryPreview() {
        for (identifier in listOf("", "anything", "임의 입력")) {
            val session = CompanionTestSession(true)
            assertFalse(session.loggedIn)
            session.action("login", identifier)
            assertTrue(session.loggedIn); assertTrue(session.enabled)
            assertEquals(identifier.ifEmpty { "테스트 사용자" }, session.account)
            session.action("disable"); assertFalse(session.enabled)
            session.action("enable"); session.action("reconnect"); assertTrue(session.enabled)
            session.action("logout"); assertFalse(session.loggedIn); assertNull(session.account)
            assertFalse(CompanionTestSession(true).loggedIn)
        }
    }
    @Test fun productionBuildRejectsEveryPreviewAction() {
        val session = CompanionTestSession(false)
        for (action in listOf("login", "enable", "reconnect", "activateLive")) {
            try { session.action(action, "anything"); fail("Must not allow production preview") }
            catch (_: IllegalStateException) { }
        }
        assertFalse(session.loggedIn); assertFalse(session.enabled)
    }
}
