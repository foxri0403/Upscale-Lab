package com.livelayer.app

import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.security.MessageDigest

/** Public album metadata is untrusted, including IDs used for working copies. */
internal object GalleryImageMetadata {
    private val safeId = Regex("[A-Za-z0-9_-]{1,128}")

    fun ownerKey(ownerId: String): String {
        require(ownerId.isNotBlank()) { "로그인 사용자 ID가 필요합니다." }
        return MessageDigest.getInstance("SHA-256")
            .digest("UpscaleLab:owner:$ownerId".toByteArray(Charsets.UTF_8))
            .joinToString("") { "%02x".format(it) }
    }

    fun isOwned(metadata: JSONObject, key: String): Boolean =
        metadata.optInt("schema") == 1 && metadata.optString("ownerKey") == key &&
            safeId.matches(metadata.optString("id"))

    fun cacheFile(root: File, id: String, extension: String): File {
        require(safeId.matches(id)) { "올바르지 않은 이미지 ID입니다." }
        require(extension in setOf("jpg", "jpeg", "png")) { "지원하지 않는 이미지 형식입니다." }
        val file = File(root, "$id.$extension").canonicalFile
        require(file.parentFile == root.canonicalFile) { "올바르지 않은 이미지 경로입니다." }
        return file
    }

    /** Verify the complete update, including tags, ratio and cloud linkage. */
    fun matches(expected: JSONObject, stored: JSONObject?): Boolean =
        stored != null && sameJson(expected, stored)

    private fun sameJson(left: Any?, right: Any?): Boolean = when {
        left is JSONObject && right is JSONObject -> {
            val keys = left.keys().asSequence().toSet()
            keys == right.keys().asSequence().toSet() &&
                keys.all { sameJson(left.opt(it), right.opt(it)) }
        }
        left is JSONArray && right is JSONArray ->
            left.length() == right.length() &&
                (0 until left.length()).all { sameJson(left.opt(it), right.opt(it)) }
        left is Number && right is Number -> left.toDouble() == right.toDouble()
        else -> left == right
    }
}
