package com.livelayer.app

import org.json.JSONArray
import org.json.JSONObject
import java.io.File

internal class LegacyImageMigration(private val store: PrivateImageStore) {
    data class Candidate(val key: String, val metadata: JSONObject, val source: File, val name: String)
    data class Result(val imported: Int, val removed: Int, val pending: List<String>, val failed: Int)

    /** Remove only an app-created, owned album copy after its durable bytes are verified. */
    fun migrate(owner: String, candidates: List<Candidate>, remove: (String) -> Boolean): Result {
        var imported = 0; var removed = 0; var failed = 0
        val pending = mutableListOf<String>()
        val existing = store.list(owner).map { it["id"] }.toMutableSet()
        for (candidate in candidates) {
            val item = candidate.metadata
            val id = item.optString("id")
            val extension = candidate.name.substringAfterLast('.', "").lowercase()
            if (!GalleryImageMetadata.isOwned(item, GalleryImageMetadata.ownerKey(owner)) ||
                extension !in setOf("png", "jpg", "jpeg") || candidate.name != "UpscaleLab_$id.$extension") continue
            try {
                val tags = item.optJSONArray("tags") ?: JSONArray().put(item.optString("tag", "장르 설정되지 않음"))
                store.importImage(owner, candidate.source, candidate.name, item.optString("title", "이미지"),
                    (0 until tags.length()).map { tags.getString(it) }, item.optInt("width"), item.optInt("height"),
                    item.optString("mode", "original"), item.optJSONObject("ratio") ?: JSONObject(), item)
                if (existing.add(id)) imported++
                check(store.matchesSource(owner, id, candidate.source)) { "기존 복사본과 앱 내부 이미지가 다릅니다." }
                try { if (remove(candidate.key)) removed++ else pending.add(candidate.key) }
                catch (_: SecurityException) { pending.add(candidate.key) }
            } catch (_: Exception) { failed++ } // Keep the album source intact; an explicit migration can retry.
        }
        return Result(imported, removed, pending, failed)
    }
}
