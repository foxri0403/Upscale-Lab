package com.livelayer.app

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class PrivateImageStoreTest {
    @get:Rule val temporary = TemporaryFolder()
    private fun store() = PrivateImageStore(File(temporary.root, "app-files/image_library"))
    private fun source(name: String = "original.heic") = File(temporary.root, name).apply { writeBytes(byteArrayOf(1, 9, 4, 2, 8)) }
    private fun import(store: PrivateImageStore, source: File, owner: String = "account-a", metadata: JSONObject? = null) =
        store.importImage(owner, source, source.name, "사진", listOf("풍경", "SF"), 1200, 1800,
            "original", JSONObject().put("mode", "automatic"), metadata)
    private fun metadata(owner: String = "account-a", id: String = "old-1") = JSONObject()
        .put("schema", 1).put("ownerKey", GalleryImageMetadata.ownerKey(owner)).put("id", id)
        .put("title", "기존 사진").put("width", 1200).put("height", 1800).put("mode", "live2d")
        .put("tags", JSONArray().put("SF")).put("tag", "SF").put("ratio", JSONObject().put("mode", "manual"))
        .put("createdAt", "2026-10-24T00:00:00.000Z").put("cloudProjectId", "project-1").put("galleryPostId", "post-1")

    @Test fun importsUntouchedBytesIntoPermanentPrivateFilesAndRestoresAfterCacheRemoval() {
        val store = store()
        val original = source()
        val bytes = original.readBytes()
        val saved = import(store, original)
        val copy = File(saved["path"] as String)
        assertTrue(copy.canonicalPath.startsWith(File(temporary.root, "app-files").canonicalPath + File.separator))
        assertEquals("heic", copy.extension)
        assertArrayEquals(bytes, copy.readBytes())
        original.delete() // The picker/cache source can disappear immediately.
        val restored = store().list("account-a").single()
        assertEquals(saved["id"], restored["id"])
        assertArrayEquals(bytes, File(restored["path"] as String).readBytes())
        assertTrue(File(temporary.root, "app-files/image_library/.nomedia").isFile)
    }

    @Test fun accountSwitchesCannotReadUpdateOrDeleteAnotherAccountsImage() {
        val store = store()
        val saved = import(store, source())
        val id = saved["id"] as String
        assertTrue(store.list("account-b").isEmpty())
        assertThrows(IllegalArgumentException::class.java) { store.item("account-b", id) }
        assertThrows(IllegalArgumentException::class.java) { store.update("account-b", id) { it.put("title", "wrong") } }
        store.delete("account-b", id)
        assertEquals(1, store.list("account-a").size)
    }

    @Test fun metadataUpdatesPreservePixelsAndIdentityAndUseUtc() {
        val store = store()
        val saved = import(store, source())
        val id = saved["id"] as String
        val bytes = File(saved["path"] as String).readBytes()
        store.update("account-a", id) {
            it.put("mode", "live2d").put("title", "수정").put("tags", JSONArray().put("게임"))
                .put("id", "forged").put("ownerKey", "forged").put("fileName", "../outside")
                .put("cloudProjectId", "project-1").put("galleryPostId", "post-1")
        }
        val current = store().item("account-a", id)
        assertEquals(id, current.getString("id"))
        assertEquals("live2d", current.getString("mode"))
        assertEquals("수정", current.getString("title"))
        assertTrue(current.getString("updatedAt").endsWith("Z"))
        assertArrayEquals(bytes, File(current.getString("path")).readBytes())
        assertEquals("project-1", store.list("account-a").single()["cloudProjectId"])
    }

    @Test fun editsReplacePixelsMetadataAndLayersTogetherWhilePreservingIdentity() {
        val store = store()
        val original = source()
        val first = import(store, original)
        val id = first["id"] as String
        val edited = source("edited.png").apply { writeBytes(byteArrayOf(7, 6, 5, 4)) }
        val saved = store.saveEdits("account-a", id, edited, "편집", listOf("SF"), 600, 800,
            JSONObject().put("mode", "manual"), true) { layers ->
            check(layers.mkdir())
            File(layers, "manifest.json").writeText("layers")
        }
        assertEquals(id, saved["id"])
        assertEquals(first["createdAt"], saved["createdAt"])
        assertEquals("live2d", saved["mode"])
        assertEquals(600, saved["width"])
        assertEquals(800, saved["height"])
        assertEquals("편집", saved["title"])
        assertNotEquals(first["path"], saved["path"])
        assertFalse(File(first["path"] as String).exists())
        assertArrayEquals(edited.readBytes(), File(saved["path"] as String).readBytes())
        assertTrue(File(File(saved["path"] as String).parentFile, "live2d/manifest.json").isFile)
        assertTrue(store.item("account-a", id).getString("updatedAt").endsWith("Z"))
        assertEquals(1, store.list("account-a").size)
        assertTrue(original.isFile)
    }

    @Test fun failedLayerPreparationPreservesThePreviousImageAndMetadata() {
        val store = store()
        val original = source()
        val saved = import(store, original)
        val id = saved["id"] as String
        val before = store.item("account-a", id).toString()
        assertThrows(IllegalStateException::class.java) {
            store.saveEdits("account-a", id, original, "실패", listOf("SF"), 50, 50, JSONObject(), true) {
                check(it.mkdir())
                File(it, "foreground.png").writeText("partial")
                error("disk full")
            }
        }
        assertEquals(before, store.item("account-a", id).toString())
        assertArrayEquals(original.readBytes(), File(saved["path"] as String).readBytes())
        assertEquals(1, store.list("account-a").size)
    }

    @Test fun sharedImagesAndOtherAccountsCannotBeEdited() {
        val store = store()
        val original = source()
        val saved = import(store, original)
        val id = saved["id"] as String
        store.update("account-a", id) { it.put("galleryPostId", "post-1").put("cloudProjectId", "project-1") }
        val before = store.item("account-a", id).toString()
        for (owner in listOf("account-a", "account-b")) {
            assertThrows(IllegalArgumentException::class.java) {
                store.saveEdits(owner, id, original, "차단", listOf("SF"), 50, 50, JSONObject(), false)
            }
        }
        assertEquals(before, store.item("account-a", id).toString())
        assertTrue(File(saved["path"] as String).isFile)
    }

    @Test fun interruptedPixelReplacementIsRecoveredBeforeListing() {
        val store = store()
        val saved = import(store, source())
        val id = saved["id"] as String
        val folder = File(saved["path"] as String).parentFile!!
        assertTrue(folder.renameTo(File(folder.parentFile, ".edit-backup-$id")))
        assertEquals(id, store().list("account-a").single()["id"])
        assertTrue(File(saved["path"] as String).isFile)
    }

    @Test fun interruptedMetadataReplacementRestoresBackup() {
        val store = store()
        val saved = import(store, source())
        val file = File(File(saved["path"] as String).parentFile, "item.json")
        assertTrue(file.renameTo(File(file.parentFile, "item.json.bak")))
        assertEquals(saved["id"], store().item("account-a", saved["id"] as String).getString("id"))
        assertTrue(file.isFile)
    }

    @Test fun deletionAffectsOnlyPrivateCopyAndPreservesChosenOriginal() {
        val store = store()
        val original = source()
        val saved = import(store, original)
        store.delete("account-a", saved["id"] as String)
        assertTrue(store.list("account-a").isEmpty())
        assertTrue(original.isFile)
    }

    @Test fun publicCopyIsRemovedOnlyAfterPrivatePixelsMetadataAndLiveModeAreDurable() {
        val store = store()
        val publicCopy = source("UpscaleLab_old-1.png")
        val result = LegacyImageMigration(store).migrate("account-a", listOf(
            LegacyImageMigration.Candidate("media-1", metadata(), publicCopy, publicCopy.name))) { key ->
            assertEquals("media-1", key)
            val restored = store().item("account-a", "old-1")
            assertEquals("live2d", restored.getString("mode"))
            assertEquals("post-1", restored.getString("galleryPostId"))
            assertArrayEquals(publicCopy.readBytes(), File(restored.getString("path")).readBytes())
            publicCopy.delete()
        }
        assertEquals(1, result.imported)
        assertEquals(1, result.removed)
        assertTrue(result.pending.isEmpty())
        assertFalse(publicCopy.exists())
        assertEquals("기존 사진", store().list("account-a").single()["title"])
    }

    @Test fun declinedPublicDeletionLeavesPrivateLibraryUsableAndRetainsGallerySource() {
        val store = store()
        val publicCopy = source("UpscaleLab_old-1.png")
        val result = LegacyImageMigration(store).migrate("account-a", listOf(
            LegacyImageMigration.Candidate("media-1", metadata(), publicCopy, publicCopy.name))) { throw SecurityException("consent required") }
        assertEquals(listOf("media-1"), result.pending)
        assertTrue(publicCopy.isFile)
        assertEquals(1, store.list("account-a").size)
    }

    @Test fun failedCopyForeignAccountAndNonAppPhotoAreNeverRemoved() {
        val store = store()
        val original = source()
        val candidates = listOf(
            LegacyImageMigration.Candidate("missing", metadata(), File(temporary.root, "missing.png"), "UpscaleLab_old-1.png"),
            LegacyImageMigration.Candidate("foreign", metadata("account-b"), original, "UpscaleLab_old-1.png"),
            LegacyImageMigration.Candidate("camera", metadata(), original, "camera.png"))
        val result = LegacyImageMigration(store).migrate("account-a", candidates) { fail("Must not delete $it"); true }
        assertEquals(1, result.failed)
        assertEquals(0, result.removed)
        assertTrue(store.list("account-a").isEmpty())
    }

    @Test fun repeatedMigrationPreservesNewPrivateEditsAndProtectsConflictingPixels() {
        val store = store()
        val original = source("UpscaleLab_old-1.png")
        import(store, original, metadata = metadata())
        store.update("account-a", "old-1") { it.put("title", "새 수정") }
        val different = File(temporary.root, "different.png").apply { writeBytes(byteArrayOf(7, 6, 5)) }
        val result = LegacyImageMigration(store).migrate("account-a", listOf(
            LegacyImageMigration.Candidate("media-1", metadata(), different, original.name))) { fail("Conflicting pixels must stay"); true }
        assertEquals(1, result.failed)
        assertEquals("새 수정", store.list("account-a").single()["title"])
        assertArrayEquals(original.readBytes(), File(store.item("account-a", "old-1").getString("path")).readBytes())
    }

    @Test fun forgedMigrationIdsCannotEscapePrivateDirectory() {
        for (id in listOf("../outside", "a/b", "..\\other")) {
            assertThrows(IllegalArgumentException::class.java) { import(store(), source(), metadata = metadata(id = id)) }
        }
    }
}
