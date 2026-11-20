package com.livelayer.app

import org.junit.Assert.*
import org.junit.Test

class RigTranslationTest {
    @Test fun gazeAddsHorizontalAndVerticalTravelRelativeToTheHead() {
        val headX = RigTranslation.x("head", 1f, 0f, 1000f, 60, 35, true, true)
        val eyeX = RigTranslation.x("eye", 1f, 0f, 1000f, 60, 35, true, true)
        val headY = RigTranslation.y("head", 1f, 2000f, 60, 35, true, true)
        val eyeY = RigTranslation.y("eye", 1f, 2000f, 60, 35, true, true)
        assertEquals(4.8f, headX, .0001f)
        assertEquals(6.3f, eyeX - headX, .0001f)
        assertEquals(9.8f, eyeY - headY, .0001f)
        assertEquals(-eyeX, RigTranslation.x("eye", -1f, 0f, 1000f, 60, 35, true, true), .0001f)
        assertEquals(-eyeY, RigTranslation.y("eye", -1f, 2000f, 60, 35, true, true), .0001f)
    }
    @Test fun zeroEyeMoveFollowsTheHeadWithoutIndependentGaze() {
        for (part in listOf("eye", "frontHair", "backHair")) {
            assertEquals(RigTranslation.x("head", 1f, 0f, 1000f, 60, 0, true, true),
                RigTranslation.x(part, 1f, 0f, 1000f, 60, 0, true, true), .0001f)
        }
    }
    @Test fun fixedPartsAndDisabledFaceStayStillWhileFollowBodyStillMoves() {
        for (part in listOf("head", "eye", "frontHair", "backHair", "fixed")) {
            assertEquals(0f, RigTranslation.x(part, 1f, 2f, 1000f, 60, 35, false, true), 0f)
            assertEquals(0f, RigTranslation.y(part, 1f, 2000f, 60, 35, false, true), 0f)
        }
        assertTrue(RigTranslation.x("torso", 1f, 2f, 1000f, 60, 35, false, true) > 0f)
        assertEquals(0f, RigTranslation.x("torso", 1f, 0f, 1000f, 60, 35, true, false), 0f)
        assertEquals(0f, RigTranslation.y("torso", 1f, 2000f, 60, 35, true, false), 0f)
    }
    @Test fun offsetsScaleWithTheSharedCanvasAndClampInput() {
        val small = RigTranslation.x("eye", 1f, 0f, 1000f, 60, 35, true, true)
        assertEquals(small * 2, RigTranslation.x("eye", 5f, 0f, 2000f, 60, 35, true, true), .0001f)
    }
}
