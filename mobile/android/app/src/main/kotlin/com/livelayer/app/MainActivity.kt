package com.livelayer.app

import android.Manifest
import android.app.DownloadManager
import android.app.WallpaperManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Environment
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.io.FilterInputStream
import java.io.InputStream
import java.net.HttpURLConnection
import java.net.URL
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors

class MainActivity : FlutterActivity() {
    private data class PendingDownload(
        val url: String,
        val fileName: String,
        val result: MethodChannel.Result,
    )

    private val ioExecutor: ExecutorService = Executors.newSingleThreadExecutor()
    private var pendingDownload: PendingDownload? = null

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "live_layer/config")
            .setMethodCallHandler { call, result ->
                when (call.method) {
                    "getApiBaseUrl" -> result.success(
                        getSharedPreferences(CONFIG_PREFERENCES, MODE_PRIVATE)
                            .getString(API_BASE_URL_KEY, null),
                    )
                    "setApiBaseUrl" -> {
                        val url = call.argument<String>("url")?.trim()
                        if (url.isNullOrBlank()) {
                            result.error("invalid_api_url", "서버 주소가 비어 있습니다.", null)
                        } else {
                            getSharedPreferences(CONFIG_PREFERENCES, MODE_PRIVATE)
                                .edit()
                                .putString(API_BASE_URL_KEY, url)
                                .apply()
                            result.success(null)
                        }
                    }
                    else -> result.notImplemented()
                }
            }
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "live_layer/wallpaper")
            .setMethodCallHandler { call, result ->
                when (call.method) {
                    "downloadImage" -> enqueueImageDownload(
                        call.argument<String>("url"),
                        call.argument<String>("fileName"),
                        result,
                    )
                    "applyStaticWallpaper" -> applyStaticWallpaper(
                        call.argument<String>("url"),
                        result,
                    )
                    "prepareLiveWallpaper" -> prepareLiveWallpaper(
                        call.arguments as? Map<*, *>,
                        result,
                    )
                    // 이전 Flutter 빌드와의 호환성을 유지한다.
                    "openWallpaperPicker" -> {
                        try {
                            openWallpaperPicker(call.argument<String>("projectId"))
                            result.success(null)
                        } catch (error: Exception) {
                            result.error(
                                "wallpaper_picker_failed",
                                "라이브 배경화면 설정 화면을 열 수 없습니다.",
                                error.message,
                            )
                        }
                    }
                    else -> result.notImplemented()
                }
            }
    }

    override fun onDestroy() {
        pendingDownload?.result?.error("cancelled", "다운로드 요청이 취소되었습니다.", null)
        pendingDownload = null
        ioExecutor.shutdownNow()
        super.onDestroy()
    }

    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray,
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode != DOWNLOAD_PERMISSION_REQUEST) return

        val pending = pendingDownload ?: return
        pendingDownload = null
        if (grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) {
            startImageDownload(pending.url, pending.fileName, pending.result)
        } else {
            pending.result.error(
                "storage_permission_denied",
                "사진 폴더에 저장하려면 저장소 권한이 필요합니다.",
                null,
            )
        }
    }

    private fun enqueueImageDownload(
        url: String?,
        requestedFileName: String?,
        result: MethodChannel.Result,
    ) {
        if (url.isNullOrBlank()) {
            result.error("invalid_url", "다운로드 URL이 없습니다.", null)
            return
        }

        val fileName = ensureImageExtension(
            sanitizeFileName(requestedFileName.orEmpty()),
            url,
        )
        if (
            Build.VERSION.SDK_INT in Build.VERSION_CODES.M..Build.VERSION_CODES.P &&
            checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            if (pendingDownload != null) {
                result.error("download_busy", "다른 다운로드 권한 요청이 진행 중입니다.", null)
                return
            }
            pendingDownload = PendingDownload(url, fileName, result)
            requestPermissions(
                arrayOf(Manifest.permission.WRITE_EXTERNAL_STORAGE),
                DOWNLOAD_PERMISSION_REQUEST,
            )
            return
        }

        startImageDownload(url, fileName, result)
    }

    private fun startImageDownload(
        url: String,
        fileName: String,
        result: MethodChannel.Result,
    ) {
        try {
            val request = DownloadManager.Request(Uri.parse(url))
                .setTitle(fileName)
                .setDescription("Upscale Lab 이미지 다운로드")
                .setMimeType(imageMimeType(fileName))
                .setNotificationVisibility(
                    DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED,
                )
                .setDestinationInExternalPublicDir(
                    Environment.DIRECTORY_PICTURES,
                    "Upscale Lab/$fileName",
                )
            val manager = getSystemService(Context.DOWNLOAD_SERVICE) as DownloadManager
            result.success(manager.enqueue(request))
        } catch (error: Exception) {
            result.error("download_failed", "이미지 다운로드를 시작하지 못했습니다.", error.message)
        }
    }

    private fun applyStaticWallpaper(url: String?, result: MethodChannel.Result) {
        if (url.isNullOrBlank()) {
            result.error("invalid_url", "배경화면 URL이 없습니다.", null)
            return
        }

        ioExecutor.execute {
            try {
                openHttpStream(url).use { input ->
                    val manager = WallpaperManager.getInstance(applicationContext)
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
                        manager.setStream(input, null, true, WallpaperManager.FLAG_SYSTEM)
                    } else {
                        manager.setStream(input)
                    }
                }
                runOnUiThread { result.success(null) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("wallpaper_failed", "배경화면을 적용하지 못했습니다.", error.message)
                }
            }
        }
    }

    private fun prepareLiveWallpaper(arguments: Map<*, *>?, result: MethodChannel.Result) {
        val projectId = arguments?.get("projectId") as? String
        val title = arguments?.get("title") as? String ?: "Upscale Lab"
        val originalUrl = arguments?.get("originalUrl") as? String
        val sensitivity = (arguments?.get("sensitivity") as? Number)?.toDouble() ?: 1.0
        val layers = arguments?.get("layers") as? List<*>
        if (projectId.isNullOrBlank() || originalUrl.isNullOrBlank() || layers.isNullOrEmpty()) {
            result.error("invalid_project", "완료된 라이브 레이어 프로젝트가 필요합니다.", null)
            return
        }

        ioExecutor.execute {
            try {
                val root = File(filesDir, "wallpapers")
                root.mkdirs()
                val safeProjectId = projectId.replace(Regex("[^A-Za-z0-9_-]"), "_")
                val staging = File(root, ".$safeProjectId-${System.nanoTime()}")
                check(staging.mkdirs()) { "임시 저장 폴더를 만들 수 없습니다." }

                try {
                    val originalFile = File(staging, "original.img")
                    downloadToFile(originalUrl, originalFile)
                    val manifestLayers = JSONArray()
                    layers.forEachIndexed { index, rawLayer ->
                        val layer = rawLayer as? Map<*, *>
                            ?: error("레이어 정보가 올바르지 않습니다.")
                        val layerUrl = layer["imageUrl"] as? String
                            ?: error("레이어 다운로드 URL이 없습니다.")
                        val fileName = "layer-${index.toString().padStart(3, '0')}.img"
                        downloadToFile(layerUrl, File(staging, fileName))
                        manifestLayers.put(
                            JSONObject()
                                .put("fileName", fileName)
                                .put("layerOrder", number(layer, "layerOrder", index))
                                .put("depth", number(layer, "depth", 0))
                                .put("positionX", number(layer, "positionX", 0))
                                .put("positionY", number(layer, "positionY", 0))
                                .put("rotation", number(layer, "rotation", 0))
                                .put("scale", number(layer, "scale", 1))
                                .put("movementX", number(layer, "movementX", 0))
                                .put("movementY", number(layer, "movementY", 0)),
                        )
                    }

                    val manifest = JSONObject()
                        .put("projectId", projectId)
                        .put("title", title)
                        .put("originalFileName", originalFile.name)
                        .put("sensitivity", sensitivity)
                        .put("layers", manifestLayers)
                    File(staging, "manifest.json").writeText(manifest.toString())

                    val destination = File(root, safeProjectId)
                    if (destination.exists() && !destination.deleteRecursively()) {
                        error("기존 배경화면 파일을 교체할 수 없습니다.")
                    }
                    check(staging.renameTo(destination)) { "배경화면 파일을 저장할 수 없습니다." }
                    getSharedPreferences("live_layer", MODE_PRIVATE)
                        .edit()
                        .putString("project_id", projectId)
                        .putString("manifest_path", File(destination, "manifest.json").absolutePath)
                        .apply()
                } catch (error: Exception) {
                    staging.deleteRecursively()
                    throw error
                }

                runOnUiThread {
                    try {
                        openWallpaperPicker(projectId)
                        result.success(null)
                    } catch (error: Exception) {
                        result.error(
                            "wallpaper_picker_failed",
                            "라이브 배경화면 설정 화면을 열 수 없습니다.",
                            error.message,
                        )
                    }
                }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error(
                        "live_wallpaper_failed",
                        "라이브 배경화면 파일을 준비하지 못했습니다.",
                        error.message,
                    )
                }
            }
        }
    }

    private fun openWallpaperPicker(projectId: String?) {
        getSharedPreferences("live_layer", MODE_PRIVATE)
            .edit()
            .putString("project_id", projectId)
            .apply()
        val intent = Intent(WallpaperManager.ACTION_CHANGE_LIVE_WALLPAPER).apply {
            putExtra(
                WallpaperManager.EXTRA_LIVE_WALLPAPER_COMPONENT,
                ComponentName(this@MainActivity, LiveLayerWallpaperService::class.java),
            )
        }
        startActivity(intent)
    }

    private fun downloadToFile(url: String, destination: File) {
        val temporary = File(destination.parentFile, "${destination.name}.part")
        openHttpStream(url).use { input ->
            FileOutputStream(temporary).use { output -> input.copyTo(output) }
        }
        check(temporary.renameTo(destination)) { "다운로드 파일을 저장할 수 없습니다." }
    }

    private fun openHttpStream(url: String): InputStream {
        val connection = URL(url).openConnection() as HttpURLConnection
        connection.connectTimeout = 15_000
        connection.readTimeout = 60_000
        connection.instanceFollowRedirects = true
        connection.connect()
        if (connection.responseCode !in 200..299) {
            val responseCode = connection.responseCode
            connection.disconnect()
            error("다운로드 서버 응답: $responseCode")
        }
        return object : FilterInputStream(connection.inputStream) {
            override fun close() {
                super.close()
                connection.disconnect()
            }
        }
    }

    private fun number(map: Map<*, *>, key: String, fallback: Number): Number =
        map[key] as? Number ?: fallback

    private fun sanitizeFileName(value: String): String {
        val sanitized = value.trim().replace(Regex("[\\\\/:*?\"<>|]"), "_")
        return sanitized.ifBlank { "upscale-lab" }.take(120)
    }

    private fun ensureImageExtension(value: String, url: String): String {
        val extension = value.substringAfterLast('.', "").lowercase()
        if (extension in IMAGE_EXTENSIONS) return value

        val urlExtension = runCatching {
            Uri.parse(url).lastPathSegment
                ?.substringAfterLast('.', "")
                ?.lowercase()
                ?.takeIf { it in IMAGE_EXTENSIONS }
        }.getOrNull()
        return "$value.${urlExtension ?: "jpg"}"
    }

    private fun imageMimeType(fileName: String): String =
        when (fileName.substringAfterLast('.', "").lowercase()) {
            "jpg", "jpeg", "jpe" -> "image/jpeg"
            "png" -> "image/png"
            "webp" -> "image/webp"
            "gif" -> "image/gif"
            "bmp", "dib" -> "image/bmp"
            "tif", "tiff" -> "image/tiff"
            "heic" -> "image/heic"
            "heics" -> "image/heic-sequence"
            "heif", "hif" -> "image/heif"
            "heifs" -> "image/heif-sequence"
            "avif" -> "image/avif"
            "dng" -> "image/x-adobe-dng"
            "jp2" -> "image/jp2"
            "jpx" -> "image/jpx"
            "wbmp" -> "image/vnd.wap.wbmp"
            else -> "application/octet-stream"
        }

    private companion object {
        const val DOWNLOAD_PERMISSION_REQUEST = 4101
        const val CONFIG_PREFERENCES = "live_layer_config"
        const val API_BASE_URL_KEY = "api_base_url"
        val IMAGE_EXTENSIONS = setOf(
            "jpg", "jpeg", "jpe", "png", "webp", "gif", "bmp", "dib",
            "tif", "tiff", "heic", "heics", "heif", "heifs", "hif",
            "avif", "dng", "jp2", "jpx", "wbmp",
        )
    }
}
