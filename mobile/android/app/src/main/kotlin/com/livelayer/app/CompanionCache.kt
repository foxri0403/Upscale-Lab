package com.livelayer.app

import android.content.Context
import android.graphics.BitmapFactory
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URI
import java.util.UUID

internal class CompanionCache(private val context: Context) {
    fun download(owner: String, server: String, manifest: JSONObject): File {
        val asset = UUID.fromString(manifest.getString("assetId")).toString()
        UUID.fromString(owner)
        val width = manifest.getInt("width"); val height = manifest.getInt("height")
        val mode = manifest.getString("mode"); val files = manifest.getJSONArray("files")
        require(manifest.getInt("schemaVersion") == 1 && width in 1..32768 && height in 1..32768 &&
            mode in listOf("Static", "Live2D") && files.length() in 1..32) { "배경화면 데이터가 올바르지 않습니다." }
        val origin = java.security.MessageDigest.getInstance("SHA-256").digest(server.toByteArray()).take(8).joinToString("") { "%02x".format(it) }
        val directory = File(context.filesDir, "companion/$owner/$origin/$asset").apply { check(isDirectory || mkdirs()) }
        val local = File(directory, "manifest.json")
        if (local.isFile) {
            val saved = runCatching { JSONObject(local.readText()) }.getOrNull()
            val layers = saved?.optJSONArray("layers")
            if (saved?.optString("assetId") == asset && saved.optString("mode") == mode && layers != null && layers.length() == files.length() && (0 until layers.length()).all {
                val file = File(directory, layers.getJSONObject(it).getString("fileName"))
                file.canonicalFile.parentFile == directory.canonicalFile && file.isFile && file.length() > 0
            }) return local
        }
        val layers = JSONArray(); val orders = mutableSetOf<Int>(); var total = 0L
        for (index in 0 until files.length()) {
            val file = files.getJSONObject(index); val order = file.getInt("order")
            require(order in 0..1000 && orders.add(order) && file.getString("contentType").startsWith("image/")) { "잘못된 이미지 파일입니다." }
            val uri = URI(file.getString("source")); require(uri.scheme == "https" && uri.userInfo == null) { "다운로드 주소가 올바르지 않습니다." }
            val connection = uri.toURL().openConnection() as HttpURLConnection
            val image = File(directory, "layer-$order.image"); val temporary = File(directory, "layer-$order.part")
            try {
                connection.connectTimeout = 15000; connection.readTimeout = 30000; connection.instanceFollowRedirects = false
                require(connection.responseCode == 200) { "이미지를 내려받지 못했습니다." }
                var bytes = 0L
                connection.inputStream.use { input -> temporary.outputStream().use { output ->
                    val buffer = ByteArray(65536)
                    while (true) { val count = input.read(buffer); if (count < 0) break
                        bytes += count; total += count
                        require(bytes <= 64 * 1024 * 1024 && total <= 200 * 1024 * 1024) { "이미지 용량이 너무 큽니다." }
                        output.write(buffer, 0, count)
                    }
                } }
                val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                BitmapFactory.decodeFile(temporary.absolutePath, bounds)
                require(bounds.outWidth in 1..32768 && bounds.outHeight in 1..32768) { "이 기기에서 지원하지 않는 이미지입니다." }
                if (mode == "Live2D") require(bounds.outWidth == width && bounds.outHeight == height) { "레이어 크기가 일치하지 않습니다." }
                check(temporary.renameTo(image)) { "이미지를 보관하지 못했습니다." }
            } finally { connection.disconnect(); temporary.delete() }
            val item = JSONObject().put("fileName", image.name).put("layerOrder", order).put("role", file.getString("role"))
            for ((source, target, default) in listOf(Triple("x", "positionX", 0.0), Triple("y", "positionY", 0.0),
                Triple("rotation", "rotation", 0.0), Triple("scale", "scale", 1.0), Triple("movementX", "movementX", 0.0), Triple("movementY", "movementY", 0.0))) {
                val number = file.optDouble(source, default); require(number.isFinite() && (source != "scale" || number in .01..10.0)) { "레이어 설정이 올바르지 않습니다." }; item.put(target, number)
            }
            layers.put(item)
        }
        if (mode == "Static") require(layers.length() == 1) { "정적 배경화면 파일이 올바르지 않습니다." }
        else require((0 until layers.length()).any { layers.getJSONObject(it).getString("role") == "Background" }) { "배경 레이어가 없습니다." }
        val saved = JSONObject().put("protocol", "agent-v1").put("renderer", if (mode == "Live2D") "live2d" else "static")
            .put("width", width).put("height", height).put("ownerId", owner).put("assetId", asset).put("mode", mode)
            .put("selectionRevision", System.currentTimeMillis()).put("layers", layers)
        File(directory, "manifest.part").writeText(saved.toString()); check(File(directory, "manifest.part").renameTo(local))
        return local
    }
}
