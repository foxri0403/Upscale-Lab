package com.livelayer.app

import java.io.BufferedInputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.EOFException
import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.util.zip.CRC32

/**
 * Stores the Upscale Lab ownership marker *inside* PNG/JPEG image bytes.
 *
 * PNG: a standard uncompressed iTXt chunk (preserves transparent pixels).
 * JPEG: an application-specific APP15 segment (does not re-encode pixels).
 *
 * Some Android ExifInterface versions do not round-trip TAG_USER_COMMENT in
 * PNG. Do not rely on EXIF for the primary ownership marker on either format.
 * Photo editors can still strip metadata; the marker is not an access control.
 */
internal object ImageOwnerMetadata {
    private const val KEY = "UpscaleLab"
    private const val PREFIX = "UPSCALE_LAB_V1:"
    private const val MAX_METADATA_BYTES = 128 * 1024
    private val PNG_SIGNATURE = byteArrayOf(
        0x89.toByte(), 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a
    )
    private val KEY_BYTES = KEY.toByteArray(Charsets.ISO_8859_1)
    private val PREFIX_BYTES = PREFIX.toByteArray(Charsets.UTF_8)

    /** MIME/source names are often unreliable for shared downloads. */
    fun detectFormat(file: File): String? = file.inputStream().use { input ->
        val header = ByteArray(PNG_SIGNATURE.size)
        var count = 0
        while (count < header.size) {
            val n = input.read(header, count, header.size - count)
            if (n < 0) break
            count += n
        }
        when {
            count == PNG_SIGNATURE.size && header.contentEquals(PNG_SIGNATURE) -> "png"
            count >= 2 && (header[0].toInt() and 255) == 255 &&
                (header[1].toInt() and 255) == 216 -> "jpg"
            else -> null
        }
    }

    fun read(file: File): String? = file.inputStream().use { read(it) }

    fun read(stream: InputStream): String? {
        val input = BufferedInputStream(stream)
        input.mark(PNG_SIGNATURE.size)
        val header = ByteArray(PNG_SIGNATURE.size)
        var count = 0
        while (count < header.size) {
            val next = input.read(header, count, header.size - count)
            if (next < 0) break
            count += next
        }
        input.reset()
        return when {
            count == PNG_SIGNATURE.size && header.contentEquals(PNG_SIGNATURE) -> readPng(input)
            count >= 2 && (header[0].toInt() and 255) == 255 &&
                (header[1].toInt() and 255) == 216 -> readJpeg(input)
            else -> null
        }
    }

    fun write(file: File, value: String) {
        require(value.startsWith(PREFIX)) { "잘못된 이미지 메타데이터입니다." }
        val encoded = value.toByteArray(Charsets.UTF_8)
        require(encoded.size <= MAX_METADATA_BYTES) { "이미지 정보가 너무 깁니다." }
        val temporary = File.createTempFile("upscale-metadata-", ".tmp", file.parentFile)
        try {
            file.inputStream().buffered().use { input ->
                temporary.outputStream().buffered().use { output ->
                    input.mark(PNG_SIGNATURE.size)
                    val header = ByteArray(PNG_SIGNATURE.size)
                    var count = 0
                    while (count < header.size) {
                        val next = input.read(header, count, header.size - count)
                        if (next < 0) break
                        count += next
                    }
                    input.reset()
                    when {
                        count == PNG_SIGNATURE.size && header.contentEquals(PNG_SIGNATURE) ->
                            writePng(input, output, encoded)
                        count >= 2 && (header[0].toInt() and 255) == 255 &&
                            (header[1].toInt() and 255) == 216 ->
                            writeJpeg(input, output, encoded)
                        else -> error("JPEG 또는 PNG 형식의 이미지만 등록할 수 있습니다.")
                    }
                }
            }
            // Fail without replacing the source unless the newly encoded marker works.
            check(read(temporary) == value) { "이미지에 사용자 정보를 저장할 수 없습니다." }
            temporary.inputStream().use { source ->
                file.outputStream().use { destination -> source.copyTo(destination) }
            }
        } finally {
            temporary.delete()
        }
    }

    private fun readJpeg(stream: InputStream): String? {
        val input = DataInputStream(stream)
        if (input.readUnsignedByte() != 255 || input.readUnsignedByte() != 216) return null
        while (true) {
            val first = input.read()
            if (first < 0 || first != 255) return null
            var marker = input.read()
            while (marker == 255) marker = input.read()
            if (marker < 0 || marker == 0xDA || marker == 0xD9) return null
            if (marker in 0xD0..0xD7 || marker == 0x01) continue
            val length = input.readUnsignedShort() - 2
            if (length < 0) return null
            if (marker == 0xEF && length <= MAX_METADATA_BYTES) {
                val data = ByteArray(length)
                input.readFully(data)
                if (data.startsWith(PREFIX_BYTES)) return String(data, Charsets.UTF_8)
            } else {
                input.skipExactly(length.toLong())
            }
        }
    }

    private fun writeJpeg(source: InputStream, target: OutputStream, value: ByteArray) {
        val input = BufferedInputStream(source)
        val output = DataOutputStream(target)
        check(input.read() == 255 && input.read() == 216) { "올바른 JPEG가 아닙니다." }
        require(value.size + 2 <= 65535) { "JPEG 이미지 정보가 너무 깁니다." }
        output.writeByte(255)
        output.writeByte(216)
        output.writeByte(255)
        output.writeByte(0xEF)
        output.writeShort(value.size + 2)
        output.write(value)

        // Replace our old marker on a subsequent title/tag/share update.
        input.mark(PREFIX_BYTES.size + 4)
        if (input.read() == 255 && input.read() == 0xEF) {
            val high = input.read()
            val low = input.read()
            val size = (high shl 8) or low
            if (high >= 0 && low >= 0 && size >= PREFIX_BYTES.size + 2) {
                val peek = ByteArray(PREFIX_BYTES.size)
                val read = input.read(peek)
                if (read == peek.size && peek.contentEquals(PREFIX_BYTES)) {
                    input.skipExactly((size - 2 - peek.size).toLong())
                    input.copyTo(output)
                    return
                }
            }
        }
        input.reset()
        input.copyTo(output)
    }

    private fun readPng(stream: InputStream): String? {
        val input = DataInputStream(stream)
        val signature = ByteArray(PNG_SIGNATURE.size)
        input.readFully(signature)
        if (!signature.contentEquals(PNG_SIGNATURE)) return null
        while (true) {
            val length = try { input.readInt() } catch (_: EOFException) { return null }
            if (length < 0) return null
            val type = ByteArray(4)
            input.readFully(type)
            val typeString = String(type, Charsets.US_ASCII)
            if (typeString == "iTXt" && length <= MAX_METADATA_BYTES + 100) {
                val data = ByteArray(length)
                input.readFully(data)
                input.readInt() // CRC (PNG decoders validate the stored CRC)
                val prefix = KEY_BYTES + byteArrayOf(0, 0, 0, 0, 0)
                if (data.startsWith(prefix)) {
                    val value = String(data, prefix.size, data.size - prefix.size, Charsets.UTF_8)
                    if (value.startsWith(PREFIX)) return value
                }
            } else {
                input.skipExactly(length.toLong() + 4L)
            }
            if (typeString == "IEND") return null
        }
    }

    private fun writePng(source: InputStream, target: OutputStream, value: ByteArray) {
        val input = DataInputStream(source)
        val output = DataOutputStream(target)
        val signature = ByteArray(PNG_SIGNATURE.size)
        input.readFully(signature)
        check(signature.contentEquals(PNG_SIGNATURE)) { "올바른 PNG가 아닙니다." }
        output.write(signature)
        var inserted = false
        var finished = false
        val keyPrefix = KEY_BYTES + byteArrayOf(0)
        while (!finished) {
            val length = input.readInt()
            check(length >= 0) { "잘못된 PNG 청크 크기입니다." }
            val type = ByteArray(4)
            input.readFully(type)
            val name = String(type, Charsets.US_ASCII)
            if ((name == "IDAT" || name == "IEND") && !inserted) {
                writeITxt(output, value)
                inserted = true
            }
            var oldMarker = false
            if (name == "iTXt" && length >= keyPrefix.size) {
                input.mark(keyPrefix.size)
                val peek = ByteArray(keyPrefix.size)
                input.readFully(peek)
                input.reset()
                oldMarker = peek.contentEquals(keyPrefix)
            }
            if (oldMarker) {
                input.skipExactly(length.toLong() + 4L)
            } else {
                output.writeInt(length)
                output.write(type)
                copyExactly(input, output, length.toLong() + 4L)
            }
            if (name == "IEND") finished = true
        }
        check(inserted) { "PNG 이미지에 메타데이터를 추가하지 못했습니다." }
    }

    private fun writeITxt(output: DataOutputStream, data: ByteArray) {
        // keyword NUL + compressionFlag 0 + compressionMethod 0 +
        // language NUL + translatedKeyword NUL + UTF-8 metadata.
        val body = KEY_BYTES + byteArrayOf(0, 0, 0, 0, 0) + data
        val type = "iTXt".toByteArray(Charsets.US_ASCII)
        val crc = CRC32()
        crc.update(type)
        crc.update(body)
        output.writeInt(body.size)
        output.write(type)
        output.write(body)
        output.writeInt(crc.value.toInt())
    }

    private fun copyExactly(input: InputStream, output: OutputStream, count: Long) {
        var remaining = count
        val buffer = ByteArray(64 * 1024)
        while (remaining > 0) {
            val read = input.read(buffer, 0, minOf(buffer.size.toLong(), remaining).toInt())
            if (read < 0) throw EOFException("이미지 파일이 손상되었습니다.")
            output.write(buffer, 0, read)
            remaining -= read
        }
    }

    private fun InputStream.skipExactly(count: Long) {
        var remaining = count
        while (remaining > 0) {
            val skipped = skip(remaining)
            if (skipped > 0) {
                remaining -= skipped
            } else if (read() >= 0) {
                remaining--
            } else {
                throw EOFException("이미지 파일이 손상되었습니다.")
            }
        }
    }

    private fun ByteArray.startsWith(prefix: ByteArray): Boolean =
        size >= prefix.size && prefix.indices.all { this[it] == prefix[it] }
}
