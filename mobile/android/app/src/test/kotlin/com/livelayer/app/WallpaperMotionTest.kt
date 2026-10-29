package com.livelayer.app
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class WallpaperMotionTest {
    @Test fun onlyGyroSelectionStartsAndroidMotionAndMasterOffWins() {
        assertTrue(WallpaperMotion.usesGyro(JSONObject()))
        assertFalse(WallpaperMotion.usesGyro(JSONObject("{motionEnabled:true,mouseEnabled:true,gyroEnabled:false}")))
        assertTrue(WallpaperMotion.usesGyro(JSONObject("{motionEnabled:true,mouseEnabled:false,gyroEnabled:true}")))
        assertFalse(WallpaperMotion.usesGyro(JSONObject("{motionEnabled:false,gyroEnabled:true}")))
    }
    @Test fun cachedPixelsReceiveFreshMotionSettingsRatherThanKeepingOldDisabledMetadata() {
        val local=JSONObject("{motionEnabled:false,ownerId:test,layers:[]}")
        val refreshed=WallpaperMotion.refreshSettings(local,JSONObject("{motionEnabled:true,mouseEnabled:false,gyroEnabled:true}"))
        assertTrue(WallpaperMotion.usesGyro(refreshed));assertEquals("test",refreshed.getString("ownerId"));assertTrue(refreshed.has("layers"))
    }
    @Test fun rawGyroFallbackIntegratesRemapsClampsAndResets() {
        val gyro=GyroIntegration();assertEquals(0f to 0f,gyro.update(1f,1f,1_000_000_000L,0))
        val point=gyro.update(1f,2f,1_050_000_000L,0);assertEquals(.1f,point.first,.001f);assertEquals(.05f,point.second,.001f)
        assertEquals(point,gyro.update(Float.NaN,1f,2_000_000_000L,0))
        repeat(100){gyro.update(10f,10f,2_000_000_000L+it*20_000_000L,0)};assertTrue(gyro.update(10f,10f,5_000_000_000L,0).first<=1.05f)
        gyro.reset();assertEquals(0f to 0f,gyro.update(1f,1f,6_000_000_000L,1));assertTrue(gyro.update(1f,1f,6_050_000_000L,1).first<0)
    }
}
