package com.livelayer.app

import org.junit.Assert.*
import org.junit.Test

class LiveActivationPolicyTest {
    @Test fun pickerRequiresUserEnabledPendingContentAndIsNotRepeated() {
        assertTrue(LiveActivationPolicy.needsPicker(true, true, false, false))
        assertFalse(LiveActivationPolicy.needsPicker(false, true, false, false))
        assertFalse(LiveActivationPolicy.needsPicker(true, false, false, false))
        assertFalse(LiveActivationPolicy.needsPicker(true, true, true, false))
        assertFalse(LiveActivationPolicy.needsPicker(true, true, false, true))
    }
    @Test fun motionStopsOnSettingOffLogoutWrongOwnerOrLeaseExpiry() {
        assertTrue(LiveActivationPolicy.mayAnimate(true, true, 101, 100))
        assertFalse(LiveActivationPolicy.mayAnimate(false, true, 101, 100))
        assertFalse(LiveActivationPolicy.mayAnimate(true, false, 101, 100))
        assertFalse(LiveActivationPolicy.mayAnimate(true, true, 100, 100))
        assertFalse(LiveActivationPolicy.mayAnimate(true, true, 99, 100))
    }
}
