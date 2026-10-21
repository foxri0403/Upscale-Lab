package com.livelayer.app

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Test
import java.io.File
import java.util.Base64

class ImageOwnerMetadataTest {
    @Test fun pngOwnerMetadataSurvivesReopenAndUpdate() = roundTrip("png", PNG)

    @Test fun jpegOwnerMetadataSurvivesReopenAndUpdate() = roundTrip("jpg", JPEG)

    @Test fun pngContentWithJpegNameUsesPngMetadata() = roundTrip("jpg", PNG, "png")

    @Test fun jpegContentWithPngNameUsesJpegMetadata() = roundTrip("png", JPEG, "jpg")

    @Test fun oversizedJpegMetadataLeavesTheSourceUnchanged() {
        val file = File.createTempFile("upscale-owner-test-", ".jpg")
        try {
            val original = Base64.getDecoder().decode(JPEG)
            file.writeBytes(original)
            assertThrows(IllegalArgumentException::class.java) {
                ImageOwnerMetadata.write(file, "UPSCALE_LAB_V1:" + "a".repeat(65534))
            }
            assertArrayEquals(original, file.readBytes())
            assertEquals(null, ImageOwnerMetadata.read(file))
        } finally {
            file.delete()
        }
    }

    private fun roundTrip(extension: String, fixture: String, expectedFormat: String = extension) {
        val file = File.createTempFile("upscale-owner-test-", ".$extension")
        try {
            file.writeBytes(Base64.getDecoder().decode(fixture))
            val originalBytes = file.readBytes()
            val initial = "UPSCALE_LAB_V1:{\"id\":\"123\",\"ownerKey\":\"owner-a\",\"title\":\"고양이\"}"
            val updated = "UPSCALE_LAB_V1:{\"id\":\"123\",\"ownerKey\":\"owner-a\",\"title\":\"자동차\"}"
            assertEquals(expectedFormat, ImageOwnerMetadata.detectFormat(file))
            assertEquals(null, ImageOwnerMetadata.read(file))
            ImageOwnerMetadata.write(file, initial)
            assertEquals(initial, ImageOwnerMetadata.read(file))
            assertArrayEquals(originalBytes, imageBytes(file, expectedFormat))
            val initialLength = file.length()
            ImageOwnerMetadata.write(file, updated)
            assertEquals(updated, ImageOwnerMetadata.read(file))
            assertArrayEquals(originalBytes, imageBytes(file, expectedFormat))
            assertEquals(initialLength, file.length())
            assertFalse(file.length() == 0L)
        } finally {
            file.delete()
        }
    }

    private fun imageBytes(file: File, format: String): ByteArray {
        val saved = file.readBytes()
        // The PNG fixture starts with IHDR, then IDAT; JPEG starts with SOI.
        // Removing only the new marker must recover EVERY original byte,
        // including compressed pixels, transparency, and existing metadata.
        val offset = if (format == "png") 33 else 2
        val markerLength = if (format == "png") {
            assertArrayEquals("iTXt".toByteArray(Charsets.US_ASCII), saved.copyOfRange(offset + 4, offset + 8))
            val length = java.nio.ByteBuffer.wrap(saved, offset, 4).int
            val crc = java.util.zip.CRC32().apply { update(saved, offset + 4, length + 4) }
            val storedCrc = java.nio.ByteBuffer.wrap(saved, offset + 8 + length, 4).int.toLong() and 0xffffffffL
            assertEquals(storedCrc, crc.value)
            length + 12
        } else {
            assertEquals(255, saved[offset].toInt() and 255)
            assertEquals(239, saved[offset + 1].toInt() and 255)
            (((saved[offset + 2].toInt() and 255) shl 8) or (saved[offset + 3].toInt() and 255)) + 2
        }
        return saved.copyOfRange(0, offset) + saved.copyOfRange(offset + markerLength, saved.size)
    }

    companion object {
        private const val PNG = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGM0jm4OZWBgYGBigAIAFZwBalHYnCIAAAAASUVORK5CYII="
        private const val JPEG = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAACAAIDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDk6KKK9g8s/9k="
    }
}
