package com.livelayer.app

import android.Manifest
import android.app.DownloadManager
import android.app.WallpaperManager
import android.content.ComponentName
import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.content.ClipData
import android.content.ActivityNotFoundException
import androidx.core.content.FileProvider
import android.content.pm.PackageManager
import android.media.MediaScannerConnection
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
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
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.UUID
import java.security.MessageDigest
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors

class MainActivity : FlutterActivity() {
    private data class PendingDownload(
        val url: String,
        val fileName: String,
        val result: MethodChannel.Result,
    )

    private data class PendingLocalSave(
        val sourcePath: String,
        val fileName: String,
        val result: MethodChannel.Result,
    )

    private val ioExecutor: ExecutorService = Executors.newSingleThreadExecutor()
    private var pendingDownload: PendingDownload? = null
    private var pendingLocalSave: PendingLocalSave? = null

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
                    "applyLocalStaticWallpaper" -> applyLocalStaticWallpaper(
                        call.argument<String>("sourcePath"),
                        result,
                    )
                    "clearHomeWallpaper" -> clearHomeWallpaper(result)
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
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "live_layer/storage")
            .setMethodCallHandler { call, result ->
                try {
                    val rawScope = call.argument<String>("scope")
                    // Photo-album export is explicit and independent of the private library.
                    val scope = if (call.method == "saveImageToGallery") "" else {
                        require(!rawScope.isNullOrBlank()) { "로그인 계정 정보가 필요합니다." }
                        MessageDigest.getInstance("SHA-256").digest(rawScope.toByteArray(Charsets.UTF_8))
                            .joinToString("") { "%02x".format(it) }
                    }
                    when (call.method) {
                        "saveImageToGallery" -> enqueueLocalImageSave(
                            call.argument<String>("sourcePath"),
                            call.argument<String>("fileName"),
                            result,
                        )
                        "listLocalImages" -> result.success(readLocalImages(scope))
                        "getLibraryOrder" -> {
                            val value = JSONObject(getSharedPreferences(LOCAL_IMAGES_PREFERENCES, MODE_PRIVATE)
                                .getString("order_$scope", "{}") ?: "{}")
                            val ids = value.optJSONArray("ids") ?: JSONArray()
                            result.success(mapOf("sort" to value.optString("sort", "manual"),
                                "ids" to (0 until ids.length()).map { ids.getString(it) }))
                        }
                        "saveLibraryOrder" -> saveLibraryOrder(scope, call.arguments as? Map<*, *>, result)
                        "previewLocalWallpaper" -> previewLocalWallpaper(scope, call.argument<String>("id"), result)
                        "downloadSharedImage" -> downloadSharedImage(call.arguments as? Map<*, *>, scope, result)
                        "importLocalImage" -> importLocalImage(
                            call.arguments as? Map<*, *>,
                            scope,
                            result,
                        )
                        "markLocalImageShared" -> markLocalImageShared(
                            call.arguments as? Map<*, *>,
                            scope,
                            result,
                        )
                        "markLocalImageUnshared" -> markLocalImageUnshared(
                            call.argument<String>("id"),
                            scope,
                            result,
                        )
                        "updateLocalImageTag" -> updateLocalImageTag(
                            call.arguments as? Map<*, *>,
                            scope,
                            result,
                        )
                        "updateLocalImageDetails" -> updateLocalImageDetails(
                            call.arguments as? Map<*, *>, scope, result,
                        )
                        "deleteLocalImage" -> deleteLocalImage(
                            call.argument<String>("id"),
                            scope,
                            result,
                        )
                        else -> result.notImplemented()
                    }
                } catch (error: Exception) {
                    result.error("local_storage_failed", error.message, null)
                }
            }
    }

    override fun onDestroy() {
        pendingDownload?.result?.error("cancelled", "다운로드 요청이 취소되었습니다.", null)
        pendingDownload = null
        pendingLocalSave?.result?.error("cancelled", "로컬 저장 요청이 취소되었습니다.", null)
        pendingLocalSave = null
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

        if (grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) {
            pendingDownload?.also { pending ->
                pendingDownload = null
                startImageDownload(pending.url, pending.fileName, pending.result)
            } ?: pendingLocalSave?.also { pending ->
                pendingLocalSave = null
                startLocalImageSave(pending.sourcePath, pending.fileName, pending.result)
            }
        } else {
            val result = pendingDownload?.result ?: pendingLocalSave?.result ?: return
            pendingDownload = null
            pendingLocalSave = null
            result.error(
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
            if (pendingDownload != null || pendingLocalSave != null) {
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

    private fun enqueueLocalImageSave(
        sourcePath: String?,
        requestedFileName: String?,
        result: MethodChannel.Result,
    ) {
        if (sourcePath.isNullOrBlank()) {
            result.error("invalid_source", "저장할 이미지 파일이 없습니다.", null)
            return
        }
        val source = File(sourcePath)
        if (!source.isFile) {
            result.error("source_not_found", "선택한 이미지 파일을 찾을 수 없습니다.", null)
            return
        }

        val fileName = ensureImageExtension(
            sanitizeFileName(requestedFileName.orEmpty()),
            source.toURI().toString(),
        )
        if (
            Build.VERSION.SDK_INT in Build.VERSION_CODES.M..Build.VERSION_CODES.P &&
            checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            if (pendingDownload != null || pendingLocalSave != null) {
                result.error("save_busy", "다른 저장소 권한 요청이 진행 중입니다.", null)
                return
            }
            pendingLocalSave = PendingLocalSave(sourcePath, fileName, result)
            requestPermissions(
                arrayOf(Manifest.permission.WRITE_EXTERNAL_STORAGE),
                DOWNLOAD_PERMISSION_REQUEST,
            )
            return
        }

        startLocalImageSave(sourcePath, fileName, result)
    }

    private fun startLocalImageSave(
        sourcePath: String,
        fileName: String,
        result: MethodChannel.Result,
    ) {
        ioExecutor.execute {
            try {
                val source = File(sourcePath)
                val savedUri = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    saveWithMediaStore(source, fileName)
                } else {
                    saveToLegacyPictures(source, fileName)
                }
                runOnUiThread { result.success(savedUri.toString()) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error(
                        "local_save_failed",
                        "이미지를 기기 사진에 저장하지 못했습니다.",
                        error.message,
                    )
                }
            }
        }
    }

    private fun importLocalImage(arguments: Map<*, *>?, scope: String, result: MethodChannel.Result) {
        val sourcePath = arguments?.get("sourcePath") as? String
        val requestedFileName = arguments?.get("fileName") as? String
        val requestedTitle = (arguments?.get("title") as? String)?.trim()
        val tags = (arguments?.get("tags") as? List<*>)?.filterIsInstance<String>()?.distinct()
            ?.filter { it.isNotBlank() }?.takeIf { it.isNotEmpty() } ?: listOf("장르 설정되지 않음")
        val width = (arguments?.get("width") as? Number)?.toInt()
        val height = (arguments?.get("height") as? Number)?.toInt()
        val mode = arguments?.get("mode") as? String ?: "original"
        if (sourcePath.isNullOrBlank() || requestedTitle.isNullOrBlank() || width == null || height == null) {
            result.error("invalid_local_image", "로컬 이미지 정보가 올바르지 않습니다.", null)
            return
        }

        ioExecutor.execute {
            try {
                val source = File(sourcePath)
                check(source.isFile) { "선택한 이미지 파일을 찾을 수 없습니다." }
                val id = UUID.randomUUID().toString()
                val directory = File(filesDir, LOCAL_IMAGES_DIRECTORY)
                check(directory.exists() || directory.mkdirs()) { "로컬 이미지 폴더를 만들 수 없습니다." }
                val safeName = ensureImageExtension(
                    sanitizeFileName(requestedFileName.orEmpty()),
                    source.toURI().toString(),
                )
                val extension = safeName.substringAfterLast('.', "jpg")
                val destination = File(directory, "$id.$extension")
                source.inputStream().use { input ->
                    FileOutputStream(destination).use { output -> input.copyTo(output) }
                }

                val item = JSONObject()
                    .put("id", id)
                    .put("title", requestedTitle.take(160))
                    .put("tag", tags.first())
                    .put("tags", JSONArray(tags))
                    .put("ownerScope", scope)
                    .put("path", destination.absolutePath)
                    .put("width", width)
                    .put("height", height)
                    .put("mode", mode)
                    .put("createdAt", utcTimestamp())
                    .put("cloudProjectId", JSONObject.NULL)
                    .put("galleryPostId", JSONObject.NULL)
                synchronized(localImagesLock) {
                    val items = readLocalImagesJson(scope)
                    items.put(item)
                    writeLocalImagesJson(scope, items)
                }
                runOnUiThread { result.success(jsonObjectToMap(item)) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error(
                        "local_import_failed",
                        "이미지를 로컬에 등록하지 못했습니다.",
                        error.message,
                    )
                }
            }
        }
    }

    private fun downloadSharedImage(arguments: Map<*, *>?, scope: String, result: MethodChannel.Result) {
        val url = arguments?.get("url") as? String
        if (url.isNullOrBlank() || Uri.parse(url).scheme !in listOf("https", "http")) {
            result.error("invalid_url", "공유 이미지 주소가 올바르지 않습니다.", null)
            return
        }
        ioExecutor.execute {
            var destination: File? = null
            try {
                val directory = File(cacheDir, "shared_imports")
                check(directory.exists() || directory.mkdirs())
                val name = ensureImageExtension(sanitizeFileName(arguments?.get("fileName") as? String ?: "image"), url)
                destination = File(directory, "$scope-${UUID.randomUUID()}.${name.substringAfterLast('.')}")
                downloadToFile(url, destination)
                val path = destination.absolutePath
                runOnUiThread { result.success(path) }
            } catch (error: Exception) {
                destination?.delete()
                destination?.let { File(it.parentFile, "${it.name}.part").delete() }
                runOnUiThread { result.error("shared_download_failed", "공유 이미지를 받지 못했습니다.", error.message) }
            }
        }
    }

    private fun readLocalImages(scope: String): List<Map<String, Any?>> = synchronized(localImagesLock) {
        val items = readLocalImagesJson(scope)
        buildList {
            for (index in items.length() - 1 downTo 0) {
                add(jsonObjectToMap(items.getJSONObject(index)))
            }
        }
    }

    private fun markLocalImageShared(arguments: Map<*, *>?, scope: String, result: MethodChannel.Result) {
        val id = arguments?.get("id") as? String
        val cloudProjectId = arguments?.get("cloudProjectId") as? String
        val galleryPostId = arguments?.get("galleryPostId") as? String
        if (id.isNullOrBlank() || cloudProjectId.isNullOrBlank() || galleryPostId.isNullOrBlank()) {
            result.error("invalid_share", "공유 정보가 올바르지 않습니다.", null)
            return
        }
        synchronized(localImagesLock) {
            val items = readLocalImagesJson(scope)
            for (index in 0 until items.length()) {
                val item = items.getJSONObject(index)
                if (item.optString("id") == id) {
                    item.put("cloudProjectId", cloudProjectId)
                    item.put("galleryPostId", galleryPostId)
                    writeLocalImagesJson(scope, items)
                    result.success(null)
                    return
                }
            }
        }
        result.error("local_image_not_found", "로컬 이미지를 찾을 수 없습니다.", null)
    }

    private fun markLocalImageUnshared(id: String?, scope: String, result: MethodChannel.Result) {
        if (id.isNullOrBlank()) {
            result.error("invalid_local_image", "공유를 취소할 이미지가 없습니다.", null)
            return
        }
        synchronized(localImagesLock) {
            val items = readLocalImagesJson(scope)
            for (index in 0 until items.length()) {
                val item = items.getJSONObject(index)
                if (item.optString("id") == id) {
                    item.put("cloudProjectId", JSONObject.NULL)
                    item.put("galleryPostId", JSONObject.NULL)
                    writeLocalImagesJson(scope, items)
                    result.success(null)
                    return
                }
            }
        }
        result.error("local_image_not_found", "로컬 이미지를 찾을 수 없습니다.", null)
    }

    private fun updateLocalImageTag(arguments: Map<*, *>?, scope: String, result: MethodChannel.Result) {
        val id = arguments?.get("id") as? String
        val tags = (arguments?.get("tags") as? List<*>)?.filterIsInstance<String>()?.distinct()
        if (id.isNullOrBlank() || tags.isNullOrEmpty() || tags.any { it.isBlank() }) {
            result.error("invalid_local_image", "수정할 태그 정보가 올바르지 않습니다.", null)
            return
        }
        synchronized(localImagesLock) {
            val items = readLocalImagesJson(scope)
            for (index in 0 until items.length()) {
                val item = items.getJSONObject(index)
                if (item.optString("id") == id) {
                    item.put("tag", tags.first())
                    item.put("tags", JSONArray(tags))
                    writeLocalImagesJson(scope, items)
                    result.success(null)
                    return
                }
            }
        }
        result.error("local_image_not_found", "로컬 이미지를 찾을 수 없습니다.", null)
    }

    private fun updateLocalImageDetails(arguments: Map<*, *>?, scope: String, result: MethodChannel.Result) {
        val id = arguments?.get("id") as? String
        val title = (arguments?.get("title") as? String)?.trim()
        val tags = (arguments?.get("tags") as? List<*>)?.filterIsInstance<String>()?.distinct()
        if (id.isNullOrBlank() || title.isNullOrBlank() || title.length > 160 ||
            tags.isNullOrEmpty() || tags.any { it.isBlank() }) {
            result.error("invalid_local_image", "이름과 태그를 확인해 주세요.", null)
            return
        }
        synchronized(localImagesLock) {
            val items = readLocalImagesJson(scope)
            for (index in 0 until items.length()) {
                val item = items.getJSONObject(index)
                if (item.optString("id") == id) {
                    item.put("title", title)
                    item.put("tag", tags.first())
                    item.put("tags", JSONArray(tags))
                    item.put("updatedAt", utcTimestamp())
                    writeLocalImagesJson(scope, items)
                    result.success(null)
                    return
                }
            }
        }
        result.error("local_image_not_found", "로컬 이미지를 찾을 수 없습니다.", null)
    }

    private fun deleteLocalImage(id: String?, scope: String, result: MethodChannel.Result) {
        if (id.isNullOrBlank()) {
            result.error("invalid_local_image", "삭제할 이미지가 없습니다.", null)
            return
        }
        ioExecutor.execute {
            try {
                synchronized(localImagesLock) {
                    val items = readLocalImagesJson(scope)
                    val remaining = JSONArray()
                    var found = false
                    for (index in 0 until items.length()) {
                        val item = items.getJSONObject(index)
                        if (item.optString("id") == id) {
                            found = true
                            val file = File(item.optString("path"))
                            check(!file.exists() || file.delete()) { "로컬 이미지 파일을 삭제할 수 없습니다." }
                        } else {
                            remaining.put(item)
                        }
                    }
                    check(found) { "로컬 이미지를 찾을 수 없습니다." }
                    writeLocalImagesJson(scope, remaining)
                }
                runOnUiThread { result.success(null) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("local_delete_failed", "로컬 이미지를 삭제하지 못했습니다.", error.message)
                }
            }
        }
    }

    private fun readAllLocalImagesJson(): JSONArray {
        val value = getSharedPreferences(LOCAL_IMAGES_PREFERENCES, MODE_PRIVATE)
            .getString(LOCAL_IMAGES_KEY, "[]") ?: "[]"
        return JSONArray(value)
    }

    private fun saveLibraryOrder(scope: String, arguments: Map<*, *>?, result: MethodChannel.Result) {
        val sort = arguments?.get("sort") as? String ?: "manual"
        require(sort in setOf("manual", "newest", "oldest", "korean", "english")) { "올바르지 않은 정렬 방식입니다." }
        val ids = (arguments?.get("ids") as? List<*>)?.filterIsInstance<String>()?.distinct() ?: emptyList()
        ioExecutor.execute {
            try {
                synchronized(localImagesLock) {
                    val owned = readLocalImagesJson(scope)
                    val ownedIds = (0 until owned.length()).map { owned.getJSONObject(it).getString("id") }.toSet()
                    val value = JSONObject().put("sort", sort).put("ids", JSONArray(ids.filter { it in ownedIds }))
                    check(getSharedPreferences(LOCAL_IMAGES_PREFERENCES, MODE_PRIVATE).edit()
                        .putString("order_$scope", value.toString()).commit()) { "이미지 순서를 저장할 수 없습니다." }
                }
                runOnUiThread { result.success(null) }
            } catch (error: Exception) {
                runOnUiThread { result.error("order_save_failed", error.message, null) }
            }
        }
    }

    private fun previewLocalWallpaper(scope: String, id: String?, result: MethodChannel.Result) {
        val path = synchronized(localImagesLock) {
            val owned = readLocalImagesJson(scope)
            (0 until owned.length()).map { owned.getJSONObject(it) }.firstOrNull {
                it.getString("id") == id
            }?.getString("path") ?: error("등록된 이미지가 없습니다.")
        }
        val source = File(path).canonicalFile
        check(source.isFile) { "이미지 파일을 찾을 수 없습니다." }
        check(source.path.startsWith(File(filesDir, LOCAL_IMAGES_DIRECTORY).canonicalPath + File.separator))
        val uri = FileProvider.getUriForFile(this, "$packageName.wallpaper_images", source)
        try {
            // Prefer the user's default launcher's crop/preview UI when supported.
            val intent = WallpaperManager.getInstance(this).getCropAndSetWallpaperIntent(uri)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            intent.clipData = ClipData.newRawUri("wallpaper", uri)
            startActivity(intent)
            result.success(true)
        } catch (_: ActivityNotFoundException) {
            result.success(false)
        } catch (_: IllegalArgumentException) {
            result.success(false)
        }
    }

    private fun readLocalImagesJson(scope: String): JSONArray {
        return LocalImageOwnership.owned(readAllLocalImagesJson(), scope)
    }

    private fun writeLocalImagesJson(scope: String, items: JSONArray) {
        val merged = LocalImageOwnership.replaceOwned(readAllLocalImagesJson(), scope, items)
        check(getSharedPreferences(LOCAL_IMAGES_PREFERENCES, MODE_PRIVATE)
            .edit()
            .putString(LOCAL_IMAGES_KEY, merged.toString())
            .commit()) { "이미지 기록을 저장할 수 없습니다." }
    }

    private fun jsonObjectToMap(item: JSONObject): Map<String, Any?> = mapOf(
        "id" to item.getString("id"),
        "title" to item.getString("title"),
        "path" to item.getString("path"),
        "width" to item.getInt("width"),
        "height" to item.getInt("height"),
        "mode" to item.optString("mode", "original"),
        "tag" to item.optString("tag", "장르 설정되지 않음"),
        "tags" to (item.optJSONArray("tags")?.let { values ->
            (0 until values.length()).map { values.getString(it) }
        } ?: listOf(item.optString("tag", "장르 설정되지 않음"))),
        "createdAt" to item.getString("createdAt"),
        "cloudProjectId" to if (item.isNull("cloudProjectId")) {
            null
        } else {
            item.optString("cloudProjectId").takeIf { it.isNotBlank() }
        },
        "galleryPostId" to if (item.isNull("galleryPostId")) {
            null
        } else {
            item.optString("galleryPostId").takeIf { it.isNotBlank() }
        },
    )

    private fun utcTimestamp(): String = SimpleDateFormat(
        "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'",
        Locale.US,
    ).apply { timeZone = TimeZone.getTimeZone("UTC") }.format(Date())

    private fun saveWithMediaStore(source: File, fileName: String): Uri {
        val resolver = applicationContext.contentResolver
        val values = ContentValues().apply {
            put(MediaStore.Images.Media.DISPLAY_NAME, fileName)
            put(MediaStore.Images.Media.MIME_TYPE, imageMimeType(fileName))
            put(
                MediaStore.Images.Media.RELATIVE_PATH,
                "${Environment.DIRECTORY_PICTURES}/Upscale Lab",
            )
            put(MediaStore.Images.Media.IS_PENDING, 1)
        }
        val uri = resolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, values)
            ?: error("사진 저장 위치를 만들 수 없습니다.")

        try {
            resolver.openOutputStream(uri, "w").use { output ->
                checkNotNull(output) { "사진 저장 스트림을 열 수 없습니다." }
                source.inputStream().use { input -> input.copyTo(output) }
            }
            values.clear()
            values.put(MediaStore.Images.Media.IS_PENDING, 0)
            resolver.update(uri, values, null, null)
            return uri
        } catch (error: Exception) {
            resolver.delete(uri, null, null)
            throw error
        }
    }

    @Suppress("DEPRECATION")
    private fun saveToLegacyPictures(source: File, fileName: String): Uri {
        val directory = File(
            Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_PICTURES),
            "Upscale Lab",
        )
        check(directory.exists() || directory.mkdirs()) { "사진 폴더를 만들 수 없습니다." }
        val destination = uniqueFile(directory, fileName)
        source.inputStream().use { input ->
            FileOutputStream(destination).use { output -> input.copyTo(output) }
        }
        MediaScannerConnection.scanFile(
            applicationContext,
            arrayOf(destination.absolutePath),
            arrayOf(imageMimeType(destination.name)),
            null,
        )
        return Uri.fromFile(destination)
    }

    private fun uniqueFile(directory: File, fileName: String): File {
        val direct = File(directory, fileName)
        if (!direct.exists()) return direct

        val extension = fileName.substringAfterLast('.', "")
        val baseName = if (extension.isEmpty()) fileName else fileName.dropLast(extension.length + 1)
        var suffix = 1
        while (true) {
            val candidateName = if (extension.isEmpty()) {
                "$baseName ($suffix)"
            } else {
                "$baseName ($suffix).$extension"
            }
            val candidate = File(directory, candidateName)
            if (!candidate.exists()) return candidate
            suffix += 1
        }
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

    private fun applyLocalStaticWallpaper(sourcePath: String?, result: MethodChannel.Result) {
        if (sourcePath.isNullOrBlank()) {
            result.error("invalid_source", "배경화면 이미지가 없습니다.", null)
            return
        }
        ioExecutor.execute {
            try {
                val source = File(sourcePath)
                check(source.isFile) { "배경화면 이미지 파일을 찾을 수 없습니다." }
                val directory = File(filesDir, APPLIED_WALLPAPER_DIRECTORY)
                check(directory.exists() || directory.mkdirs()) { "배경화면 저장 폴더를 만들 수 없습니다." }
                val persistent = File(directory, "current.img")
                val temporary = File(directory, "current.img.part")
                source.inputStream().use { input ->
                    FileOutputStream(temporary).use { output -> input.copyTo(output) }
                }
                if (persistent.exists()) check(persistent.delete()) { "기존 배경화면 파일을 교체할 수 없습니다." }
                check(temporary.renameTo(persistent)) { "배경화면 파일을 보관할 수 없습니다." }

                persistent.inputStream().use { input ->
                    val manager = WallpaperManager.getInstance(applicationContext)
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
                        manager.setStream(input, null, true, WallpaperManager.FLAG_SYSTEM)
                    } else {
                        manager.setStream(input)
                    }
                }
                getSharedPreferences("live_layer", MODE_PRIVATE)
                    .edit()
                    .putString("static_wallpaper_path", persistent.absolutePath)
                    .commit()
                runOnUiThread { result.success(null) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("wallpaper_failed", "배경화면을 적용하지 못했습니다.", error.message)
                }
            }
        }
    }

    private fun clearHomeWallpaper(result: MethodChannel.Result) {
        ioExecutor.execute {
            try {
                val manager = WallpaperManager.getInstance(applicationContext)
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
                    manager.clear(WallpaperManager.FLAG_SYSTEM)
                } else {
                    manager.clear()
                }

                val persistent = File(File(filesDir, APPLIED_WALLPAPER_DIRECTORY), "current.img")
                check(!persistent.exists() || persistent.delete()) { "보관된 배경화면 파일을 삭제할 수 없습니다." }
                getSharedPreferences("live_layer", MODE_PRIVATE)
                    .edit()
                    .remove("static_wallpaper_path")
                    .commit()
                runOnUiThread { result.success(null) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("wallpaper_clear_failed", "홈 배경화면을 제거하지 못했습니다.", error.message)
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
        val localImagesLock = Any()
        const val DOWNLOAD_PERMISSION_REQUEST = 4101
        const val CONFIG_PREFERENCES = "live_layer_config"
        const val API_BASE_URL_KEY = "api_base_url"
        const val LOCAL_IMAGES_PREFERENCES = "local_images"
        const val LOCAL_IMAGES_KEY = "items"
        const val LOCAL_IMAGES_DIRECTORY = "local_images"
        const val APPLIED_WALLPAPER_DIRECTORY = "applied_wallpaper"
        val IMAGE_EXTENSIONS = setOf(
            "jpg", "jpeg", "jpe", "png", "webp", "gif", "bmp", "dib",
            "tif", "tiff", "heic", "heics", "heif", "heifs", "hif",
            "avif", "dng", "jp2", "jpx", "wbmp",
        )
    }
}
