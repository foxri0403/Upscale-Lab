package com.livelayer.app

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import java.io.File

class GalleryImageMetadataTest {
    @Test fun ownershipRequiresTheSameAccountAndKnownSchema() {
        val key = GalleryImageMetadata.ownerKey("account-a")
        assertEquals(key, GalleryImageMetadata.ownerKey("account-a"))
        assertNotEquals(key, GalleryImageMetadata.ownerKey("account-b"))
        val metadata = JSONObject().put("schema", 1).put("ownerKey", key).put("id", "image-1")
        assertTrue(GalleryImageMetadata.isOwned(metadata, key))
        assertFalse(GalleryImageMetadata.isOwned(metadata, GalleryImageMetadata.ownerKey("account-b")))
        metadata.put("schema", 2)
        assertFalse(GalleryImageMetadata.isOwned(metadata, key))
    }

    @Test fun externalIdsCannotEscapeTheCacheDirectory() {
        val root = File(System.getProperty("java.io.tmpdir"), "upscale-metadata-test")
        assertEquals(root.canonicalFile, GalleryImageMetadata.cacheFile(root, "image-1", "png").parentFile)
        for (id in listOf("../outside", "..\\outside", "/absolute", "", "a/b")) {
            assertThrows(IllegalArgumentException::class.java) {
                GalleryImageMetadata.cacheFile(root, id, "png")
            }
        }
        assertThrows(IllegalArgumentException::class.java) {
            GalleryImageMetadata.cacheFile(root, "image-1", "../other")
        }
    }

    @Test fun verificationDetectsLostTagsOrShareStateEvenWhenIdIsUnchanged() {
        val metadata = JSONObject("""{"id":"image-1","tags":["풍경","SF"],"ratio":{"mode":"manual","portrait":true},"galleryPostId":"post-1"}""")
        assertTrue(GalleryImageMetadata.matches(metadata, JSONObject(metadata.toString())))
        val missingTag = JSONObject(metadata.toString()).put("tags", org.json.JSONArray().put("풍경"))
        assertFalse(GalleryImageMetadata.matches(metadata, missingTag))
        val missingPost = JSONObject(metadata.toString()).put("galleryPostId", JSONObject.NULL)
        assertFalse(GalleryImageMetadata.matches(metadata, missingPost))
        assertFalse(GalleryImageMetadata.matches(metadata, null))
    }
}
