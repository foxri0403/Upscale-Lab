package com.livelayer.app

import android.app.WallpaperManager
import java.io.File
import java.io.IOException
import java.io.InputStream
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class StaticWallpaperControllerTest {
    @get:Rule val temporary = TemporaryFolder()

    private class FakePlatform(override val sdkInt: Int = 24) : WallpaperPlatform {
        val applied = mutableMapOf<WallpaperTarget, String>()
        val calls = mutableListOf<Int>()
        var fail = false

        override fun apply(input: InputStream, target: WallpaperTarget) {
            if (fail) throw IOException("Android rejected the image")
            val image = input.bufferedReader().readText()
            calls.add(target.flags)
            target.screens.forEach { applied[it] = image }
        }

        override fun clear(target: WallpaperTarget) {
            if (fail) throw IOException("Android rejected removal")
            calls.add(target.flags)
            target.screens.forEach { applied.remove(it) }
        }
    }

    @Test fun targetsUseAndroidFlagsAndRejectInvalidArguments() {
        assertEquals(WallpaperManager.FLAG_SYSTEM, WallpaperTarget.fromArgument(null).flags)
        assertEquals(WallpaperManager.FLAG_SYSTEM, WallpaperTarget.fromArgument("home").flags)
        assertEquals(WallpaperManager.FLAG_LOCK, WallpaperTarget.fromArgument("lock").flags)
        assertEquals(WallpaperManager.FLAG_SYSTEM or WallpaperManager.FLAG_LOCK,
            WallpaperTarget.fromArgument("both").flags)
        listOf("", "HOME", "unknown", 3, true).forEach { invalid ->
            assertThrows(IllegalArgumentException::class.java) { WallpaperTarget.fromArgument(invalid) }
        }
    }

    @Test fun preparedCropsUseSelectedTargetsAndKeepTheOriginalCache() {
        for (target in WallpaperTarget.entries) {
            val directory = temporary.newFolder()
            val source = temporary.newFile().apply { writeText("original") }
            val prepared = temporary.newFile()
            val platform = FakePlatform()
            val controller = StaticWallpaperController(directory, platform)
            controller.apply(target, prepareSource = { input ->
                assertEquals("original", input.readText())
                prepared.apply { writeText("screen crop") }
            }) { source.inputStream() }
            assertEquals(listOf(target.flags), platform.calls)
            for (screen in target.screens) {
                assertEquals("screen crop", platform.applied[screen])
                assertEquals("original", File(directory, screen.cacheFileName).readText())
            }
            assertEquals("original", source.readText())
            assertFalse(prepared.exists())
            assertFalse(directory.listFiles()!!.any { it.name.endsWith(".part") })
        }
    }

    @Test fun cropFailurePreservesPreviousWallpapersAndSourceCopies() {
        val directory = temporary.newFolder()
        val platform = FakePlatform()
        val controller = StaticWallpaperController(directory, platform)
        controller.apply(WallpaperTarget.BOTH) { "previous".byteInputStream() }
        assertThrows(IOException::class.java) {
            controller.apply(WallpaperTarget.BOTH, prepareSource = {
                throw IOException("crop failed")
            }) { "new original".byteInputStream() }
        }
        assertEquals(listOf(WallpaperTarget.BOTH.flags), platform.calls)
        assertEquals("previous", File(directory, "home.img").readText())
        assertEquals("previous", File(directory, "lock.img").readText())
        assertFalse(directory.listFiles()!!.any { it.name.endsWith(".part") })
    }

    @Test fun settingEitherScreenPreservesTheOtherAndDoesNotChangeTheSource() {
        val directory = temporary.newFolder()
        val source = temporary.newFile().apply { writeText("home one") }
        val platform = FakePlatform()
        val controller = StaticWallpaperController(directory, platform)
        controller.apply(WallpaperTarget.HOME) { source.inputStream() }
        controller.apply(WallpaperTarget.LOCK) { "lock one".byteInputStream() }
        controller.apply(WallpaperTarget.HOME) { "home two".byteInputStream() }
        assertEquals("lock one", platform.applied[WallpaperTarget.LOCK])
        assertEquals("lock one", File(directory, "lock.img").readText())
        controller.apply(WallpaperTarget.LOCK) { "lock two".byteInputStream() }
        assertEquals("home two", platform.applied[WallpaperTarget.HOME])
        assertEquals("home two", File(directory, "home.img").readText())
        assertEquals("home one", source.readText())
        assertFalse(directory.listFiles()!!.any { it.name.endsWith(".part") })
    }

    @Test fun bothAppliesInOnePlatformCallAndEachScreenCanThenBeCleared() {
        listOf(WallpaperTarget.HOME, WallpaperTarget.LOCK).forEach { selected ->
            val directory = temporary.newFolder()
            val platform = FakePlatform()
            val controller = StaticWallpaperController(directory, platform)
            controller.apply(WallpaperTarget.BOTH) { "shared".byteInputStream() }
            assertEquals(listOf(WallpaperTarget.BOTH.flags), platform.calls)
            assertEquals("shared", File(directory, "home.img").readText())
            assertEquals("shared", File(directory, "lock.img").readText())
            controller.clear(selected)
            val other = if (selected == WallpaperTarget.HOME) WallpaperTarget.LOCK else WallpaperTarget.HOME
            assertFalse(File(directory, selected.cacheFileName).exists())
            assertEquals("shared", File(directory, other.cacheFileName).readText())
            assertEquals("shared", platform.applied[other])
            assertFalse(platform.applied.containsKey(selected))
        }
    }

    @Test fun removingBothClearsBothCopiesAndLeavesLibraryImagesUntouched() {
        val directory = temporary.newFolder()
        val libraryImage = temporary.newFile().apply { writeText("private library image") }
        val platform = FakePlatform()
        val controller = StaticWallpaperController(directory, platform)
        controller.apply(WallpaperTarget.BOTH) { libraryImage.inputStream() }
        controller.clear(WallpaperTarget.BOTH)
        assertTrue(platform.applied.isEmpty())
        assertTrue(directory.listFiles()!!.isEmpty())
        assertEquals("private library image", libraryImage.readText())
        assertEquals(listOf(WallpaperTarget.BOTH.flags, WallpaperTarget.BOTH.flags), platform.calls)
        controller.clear(WallpaperTarget.BOTH) // Removing an already cleared wallpaper is safe.
    }

    @Test fun lockRemovalPreservesLegacyHomeSourceUntilHomeIsRemoved() {
        val directory = temporary.newFolder()
        val legacyHome = File(directory, "current.img").apply { writeText("legacy home") }
        val controller = StaticWallpaperController(directory, FakePlatform())
        controller.apply(WallpaperTarget.LOCK) { "lock".byteInputStream() }
        controller.clear(WallpaperTarget.LOCK)
        assertEquals("legacy home", legacyHome.readText())
        controller.clear(WallpaperTarget.HOME)
        assertFalse(legacyHome.exists())
    }

    @Test fun platformFailuresKeepPreviousCopiesAndCleanStagingFiles() {
        val directory = temporary.newFolder()
        val platform = FakePlatform()
        val controller = StaticWallpaperController(directory, platform)
        controller.apply(WallpaperTarget.BOTH) { "previous".byteInputStream() }
        platform.fail = true
        assertThrows(IOException::class.java) {
            controller.apply(WallpaperTarget.HOME) { "replacement".byteInputStream() }
        }
        assertThrows(IOException::class.java) { controller.clear(WallpaperTarget.BOTH) }
        assertEquals("previous", File(directory, "home.img").readText())
        assertEquals("previous", File(directory, "lock.img").readText())
        assertEquals(2, platform.applied.size)
        assertFalse(directory.listFiles()!!.any { it.name.endsWith(".part") })
    }

    @Test fun unreadableSourceDoesNotChangeEitherWallpaper() {
        val directory = temporary.newFolder()
        val platform = FakePlatform()
        val controller = StaticWallpaperController(directory, platform)
        controller.apply(WallpaperTarget.BOTH) { "previous".byteInputStream() }
        assertThrows(IOException::class.java) {
            controller.apply(WallpaperTarget.BOTH) { throw IOException("download failed") }
        }
        assertEquals(1, platform.calls.size)
        assertEquals("previous", File(directory, "home.img").readText())
        assertEquals("previous", File(directory, "lock.img").readText())
        assertFalse(directory.listFiles()!!.any { it.name.endsWith(".part") })
    }

    @Test fun unsupportedTargetsFailBeforeDownloadingOrRemovingAnything() {
        val directory = File(temporary.root, "not-created")
        val platform = FakePlatform(sdkInt = 23)
        val controller = StaticWallpaperController(directory, platform)
        listOf(WallpaperTarget.LOCK, WallpaperTarget.BOTH).forEach { target ->
            assertThrows(UnsupportedOperationException::class.java) {
                controller.apply(target) { error("Must not download") }
            }
            assertThrows(UnsupportedOperationException::class.java) { controller.clear(target) }
        }
        assertFalse(directory.exists())
        assertTrue(platform.calls.isEmpty())
        controller.apply(WallpaperTarget.HOME) { "legacy home".byteInputStream() }
        controller.clear(WallpaperTarget.HOME)
        assertEquals(listOf(WallpaperTarget.HOME.flags, WallpaperTarget.HOME.flags), platform.calls)
    }
}
