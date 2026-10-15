package com.livelayer.app

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test
import java.io.File
import java.util.Base64

class ImageOwnerMetadataTest {
    @Test fun pngOwnerMetadataSurvivesReopenAndUpdate() = roundTrip("png", PNG)

    @Test fun jpegOwnerMetadataSurvivesReopenAndUpdate() = roundTrip("jpg", JPEG)

    private fun roundTrip(extension: String, fixture: String) {
        val file = File.createTempFile("upscale-owner-test-", ".$extension")
        try {
            file.writeBytes(Base64.getDecoder().decode(fixture))
            val initial = "UPSCALE_LAB_V1:{\"id\":\"123\",\"ownerKey\":\"owner-a\",\"title\":\"고양이\"}"
            val updated = "UPSCALE_LAB_V1:{\"id\":\"123\",\"ownerKey\":\"owner-a\",\"title\":\"자동차\"}"
            assertEquals(extension, ImageOwnerMetadata.detectFormat(file))
            assertEquals(null, ImageOwnerMetadata.read(file))
            ImageOwnerMetadata.write(file, initial)
            assertEquals(initial, ImageOwnerMetadata.read(file))
            ImageOwnerMetadata.write(file, updated)
            assertEquals(updated, ImageOwnerMetadata.read(file))
            assertFalse(file.length() == 0L)
        } finally {
            file.delete()
        }
    }

    companion object {
        private const val PNG = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGM0jm4OZWBgYGBigAIAFZwBalHYnCIAAAAASUVORK5CYII="
        private const val JPEG = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAACAAIDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDk6KKK9g8s/9k="
    }
}
