package com.livelayer.app

import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.security.MessageDigest
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.UUID

/** Durable account-owned files, independent of MediaStore and the disposable cache. */
internal class PrivateImageStore(private val root: File) {
    private val idPattern = Regex("[A-Za-z0-9_-]{1,128}")

    private fun ownerRoot(owner: String): File = File(root, GalleryImageMetadata.ownerKey(owner))
    private fun directory(owner: String, id: String): File {
        require(idPattern.matches(id)) { "이미지 ID가 올바르지 않습니다." }
        val parent = ownerRoot(owner).canonicalFile
        return File(parent, id).canonicalFile.also { require(it.parentFile == parent) }
    }

    @Synchronized fun list(owner: String): List<Map<String, Any?>> =
        ownerRoot(owner).listFiles().orEmpty().filter { it.isDirectory && idPattern.matches(it.name) }
            .mapNotNull { runCatching { toMap(item(owner, it.name)) }.getOrNull() }
            .sortedByDescending { it["createdAt"] as String }

    @Synchronized fun item(owner: String, id: String): JSONObject {
        val folder = directory(owner, id)
        val json = File(folder, "item.json")
        // Recover an interrupted metadata replacement without discarding the image.
        val backup = File(folder, "item.json.bak")
        if (!json.exists() && backup.isFile) check(backup.renameTo(json))
        require(json.isFile) { "로그인한 사용자의 이미지가 없습니다." }
        val metadata = JSONObject(json.readText())
        require(metadata.optInt("schema") == 2 && metadata.optString("id") == id &&
            metadata.optString("ownerKey") == GalleryImageMetadata.ownerKey(owner)) {
            "로그인한 사용자의 이미지가 없습니다."
        }
        val name = metadata.getString("fileName")
        require(name.matches(Regex("source\\.[a-z0-9]{1,10}"))) { "이미지 경로가 올바르지 않습니다." }
        val file = File(folder, name).canonicalFile
        require(file.parentFile == folder && file.isFile && file.length() > 0) { "저장된 이미지 파일이 없습니다." }
        return metadata.put("path", file.absolutePath)
    }

    @Synchronized fun importImage(owner: String, source: File, requestedName: String, title: String,
        tags: List<String>, width: Int, height: Int, mode: String, ratio: JSONObject,
        existingMetadata: JSONObject? = null): Map<String, Any?> {
        require(source.isFile && source.length() > 0) { "이미지를 찾을 수 없습니다." }
        require(width > 0 && height > 0 && title.isNotBlank()) { "이미지 정보가 올바르지 않습니다." }
        val metadata = existingMetadata?.let { JSONObject(it.toString()) } ?: JSONObject()
            .put("id", UUID.randomUUID().toString()).put("title", title.take(160))
            .put("tags", JSONArray(tags)).put("tag", tags.firstOrNull() ?: "장르 설정되지 않음")
            .put("width", width).put("height", height).put("mode", mode).put("ratio", ratio)
            .put("createdAt", timestamp()).put("cloudProjectId", JSONObject.NULL).put("galleryPostId", JSONObject.NULL)
        val id = metadata.getString("id")
        val destination = directory(owner, id)
        if (destination.isDirectory) return toMap(item(owner, id)) // Idempotent upgrade; keep newer private edits.
        val extension = requestedName.substringAfterLast('.', "img").lowercase(Locale.US)
            .takeIf { it.matches(Regex("[a-z0-9]{1,10}")) } ?: "img"
        metadata.remove("path"); metadata.remove("ownerScope"); metadata.remove("albumMigrated")
        metadata.remove("privateMigrated")
        metadata.put("schema", 2).put("ownerKey", GalleryImageMetadata.ownerKey(owner))
            .put("fileName", "source.$extension")
        check(destination.parentFile!!.isDirectory || destination.parentFile!!.mkdirs())
        File(root, ".nomedia").apply { if (!exists()) createNewFile() }
        val staging = File(destination.parentFile, ".import-${UUID.randomUUID()}")
        check(staging.mkdir())
        try {
            val file = File(staging, "source.$extension")
            source.inputStream().use { input -> FileOutputStream(file).use { output -> input.copyTo(output); output.fd.sync() } }
            check(sha256(source) == sha256(file)) { "이미지 복사 검증에 실패했습니다." }
            writeJson(File(staging, "item.json"), metadata)
            check(staging.renameTo(destination)) { "이미지를 앱 저장소에 저장하지 못했습니다." }
            return toMap(item(owner, id))
        } finally { if (staging.exists()) staging.deleteRecursively() }
    }

    @Synchronized fun update(owner: String, id: String, changes: (JSONObject) -> Unit) {
        val current = item(owner, id)
        val updated = JSONObject(current.toString())
        changes(updated)
        updated.put("id", id).put("schema", 2).put("ownerKey", GalleryImageMetadata.ownerKey(owner))
            .put("fileName", current.getString("fileName")).put("updatedAt", timestamp())
        updated.remove("path")
        writeJson(File(directory(owner, id), "item.json"), updated)
    }

    @Synchronized fun delete(owner: String, id: String) {
        val folder = directory(owner, id)
        if (!folder.exists()) return
        item(owner, id) // Verify ownership before removing any bytes.
        check(folder.deleteRecursively()) { "앱에 저장된 이미지를 삭제하지 못했습니다." }
    }

    fun matchesSource(owner: String, id: String, source: File): Boolean =
        sha256(File(item(owner, id).getString("path"))) == sha256(source)

    private fun toMap(item: JSONObject): Map<String, Any?> {
        val tags = item.optJSONArray("tags") ?: JSONArray().put(item.optString("tag", "장르 설정되지 않음"))
        val ratio = item.optJSONObject("ratio") ?: JSONObject()
        return mapOf("id" to item.getString("id"), "title" to item.getString("title"), "path" to item.getString("path"),
            "width" to item.getInt("width"), "height" to item.getInt("height"), "mode" to item.optString("mode", "original"),
            "tag" to item.optString("tag", "장르 설정되지 않음"),
            "tags" to (0 until tags.length()).map { tags.getString(it) },
            "ratio" to ratio.keys().asSequence().associateWith { ratio.opt(it).takeUnless { value -> value == JSONObject.NULL } },
            "createdAt" to item.getString("createdAt"),
            "cloudProjectId" to item.optString("cloudProjectId").takeUnless { it.isBlank() || it == "null" },
            "galleryPostId" to item.optString("galleryPostId").takeUnless { it.isBlank() || it == "null" })
    }

    private fun writeJson(file: File, value: JSONObject) {
        val temporary = File(file.parentFile, "${file.name}.tmp")
        val backup = File(file.parentFile, "${file.name}.bak")
        FileOutputStream(temporary).use { it.write(value.toString().toByteArray(Charsets.UTF_8)); it.fd.sync() }
        try {
            if (file.exists()) { check(!backup.exists() || backup.delete()); check(file.renameTo(backup)) }
            check(temporary.renameTo(file)) { "이미지 정보를 저장하지 못했습니다." }
            backup.delete()
        } catch (error: Exception) {
            if (!file.exists() && backup.exists()) backup.renameTo(file)
            throw error
        } finally { temporary.delete() }
    }

    private fun sha256(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input -> val buffer = ByteArray(8192)
            while (true) { val count = input.read(buffer); if (count < 0) break; digest.update(buffer, 0, count) }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    private fun timestamp(): String = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.US)
        .apply { timeZone = TimeZone.getTimeZone("UTC") }.format(Date())
}
