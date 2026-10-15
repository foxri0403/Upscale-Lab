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
import android.app.RecoverableSecurityException
import androidx.core.content.FileProvider
import android.content.pm.PackageManager
import android.media.MediaScannerConnection
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import android.graphics.Point
import android.util.DisplayMetrics
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
    private val wallpaperController by lazy {
        StaticWallpaperController(
            File(filesDir, APPLIED_WALLPAPER_DIRECTORY),
            AndroidWallpaperPlatform(applicationContext),
        )
    }
    private var pendingDownload: PendingDownload? = null
    private var pendingLocalSave: PendingLocalSave? = null
    private val galleryStore by lazy { GalleryLocalImageStore(applicationContext) }
    private data class PendingGalleryRead(
        val ownerId: String,
        val legacyScope: String,
        val result: MethodChannel.Result,
    )
    private var pendingGalleryRead: PendingGalleryRead? = null
    private data class PendingGalleryImport(
        val ownerId: String,
        val arguments: Map<*, *>?,
        val result: MethodChannel.Result,
    )
    private var pendingGalleryImport: PendingGalleryImport? = null
    private var pendingGalleryAction: (() -> Unit)? = null
    private var pendingGalleryResult: MethodChannel.Result? = null

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
                    "getDisplaySize" -> {
                        val size = deviceDisplaySize()
                        result.success(mapOf("width" to size.x, "height" to size.y))
                    }
                    "downloadImage" -> enqueueImageDownload(
                        call.argument<String>("url"),
                        call.argument<String>("fileName"),
                        result,
                    )
                    "applyStaticWallpaper" -> applyStaticWallpaper(
                        call.argument<String>("url"),
                        call.argument<Any>("target"),
                        WallpaperRatio.fromMap(call.argument<Map<*, *>>("ratio")),
                        result,
                    )
                    "applyLocalStaticWallpaper" -> applyLocalStaticWallpaper(
                        call.argument<String>("sourcePath"),
                        call.argument<Any>("target"),
                        WallpaperRatio.fromMap(call.argument<Map<*, *>>("ratio")),
                        result,
                    )
                    "clearWallpaper" -> clearWallpaper(call.argument<Any>("target"), result)
                    "clearHomeWallpaper" -> clearWallpaper("home", result)
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
                    val ownerId = call.argument<String>("ownerId")
                    // Photo-album export is explicit and independent of the private library.
                    val scope = if (call.method == "saveImageToGallery") "" else {
                        require(!rawScope.isNullOrBlank()) { "로그인 계정 정보가 필요합니다." }
                        MessageDigest.getInstance("SHA-256").digest(rawScope.toByteArray(Charsets.UTF_8))
                            .joinToString("") { "%02x".format(it) }
                    }
                    if (call.method != "saveImageToGallery") {
                        require(!ownerId.isNullOrBlank()) { "로그인 사용자 ID가 필요합니다." }
                    }
                    when (call.method) {
                        "saveImageToGallery" -> enqueueLocalImageSave(
                            call.argument<String>("sourcePath"),
                            call.argument<String>("fileName"),
                            result,
                        )
                        "listLocalImages" -> listGalleryImages(ownerId!!, scope, result)
                        "getLibraryOrder" -> {
                            val value = JSONObject(getSharedPreferences(LOCAL_IMAGES_PREFERENCES, MODE_PRIVATE)
                                .getString("order_$scope", "{}") ?: "{}")
                            val ids = value.optJSONArray("ids") ?: JSONArray()
                            result.success(mapOf("sort" to value.optString("sort", "manual"),
                                "ids" to (0 until ids.length()).map { ids.getString(it) }))
                        }
                        "saveLibraryOrder" -> saveLibraryOrder(ownerId!!, scope, call.arguments as? Map<*, *>, result)
                        "previewLocalWallpaper" -> ioExecutor.execute {
                            runCatching { previewLocalWallpaper(ownerId!!, call.argument<String>("id"), result) }
                                .onFailure { error -> runOnUiThread {
                                    result.error("preview_failed", error.message, null)
                                } }
                        }
                        "applyLocalWallpaper" -> {
                            ioExecutor.execute {
                                try {
                                    val item = localWallpaperItem(ownerId!!, call.argument<String>("id"))
                                    runOnUiThread {
                                        applyLocalStaticWallpaper(
                                            File(item.getString("path")).canonicalPath,
                                            call.argument<Any>("target"),
                                            ratioFromJson(item.optJSONObject("ratio")), result)
                                    }
                                } catch (error: Exception) {
                                    runOnUiThread { result.error("wallpaper_failed", error.message, null) }
                                }
                            }
                        }
                        "downloadSharedImage" -> downloadSharedImage(call.arguments as? Map<*, *>, scope, result)
                        "importLocalImage" -> galleryImport(
                            ownerId!!, call.arguments as? Map<*, *>, result)
                        "markLocalImageShared" -> galleryMutation(result) {
                            val args = call.arguments as? Map<*, *>
                            val id = args?.get("id") as? String ?: error("이미지 ID가 없습니다.")
                            val project = args["cloudProjectId"] as? String ?: error("프로젝트 ID가 없습니다.")
                            val post = args["galleryPostId"] as? String ?: error("공유 ID가 없습니다.")
                            galleryStore.update(ownerId!!, id) {
                                it.put("cloudProjectId", project).put("galleryPostId", post)
                            }
                        }
                        "markLocalImageUnshared" -> galleryMutation(result) {
                            galleryStore.update(ownerId!!, call.argument<String>("id") ?: error("이미지 ID가 없습니다.")) {
                                it.put("cloudProjectId", JSONObject.NULL).put("galleryPostId", JSONObject.NULL)
                            }
                        }
                        "updateLocalImageTag" -> galleryMutation(result) {
                            val args = call.arguments as? Map<*, *>
                            val id = args?.get("id") as? String ?: error("이미지 ID가 없습니다.")
                            val tags = (args["tags"] as? List<*>)?.filterIsInstance<String>()
                                ?.filter { it.isNotBlank() } ?: error("태그가 없습니다.")
                            require(tags.isNotEmpty())
                            galleryStore.update(ownerId!!, id) {
                                it.put("tags", JSONArray(tags)).put("tag", tags.first())
                            }
                        }
                        "updateLocalImageDetails" -> galleryMutation(result) {
                            val args = call.arguments as? Map<*, *>
                            val id = args?.get("id") as? String ?: error("이미지 ID가 없습니다.")
                            val title = (args["title"] as? String)?.trim() ?: error("이름이 없습니다.")
                            val tags = (args["tags"] as? List<*>)?.filterIsInstance<String>()
                                ?.filter { it.isNotBlank() } ?: error("태그가 없습니다.")
                            require(title.isNotBlank() && title.length <= 160 && tags.isNotEmpty())
                            galleryStore.update(ownerId!!, id) {
                                it.put("title", title).put("tags", JSONArray(tags)).put("tag", tags.first())
                                if (args["ratio"] is Map<*, *>) {
                                    it.put("ratio", JSONObject(WallpaperRatio.fromMap(args["ratio"] as Map<*, *>).toMap()))
                                }
                            }
                        }
                        "deleteLocalImage" -> galleryMutation(result) {
                            galleryStore.delete(ownerId!!, call.argument<String>("id") ?: error("이미지 ID가 없습니다."))
                        }
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
        pendingGalleryRead?.result?.error("cancelled", "사진 조회가 취소되었습니다.", null)
        pendingGalleryRead = null
        pendingGalleryImport?.result?.error("cancelled", "이미지 저장이 취소되었습니다.", null)
        pendingGalleryImport = null
        pendingGalleryAction = null
        pendingGalleryResult?.error("cancelled", "이미지 수정이 취소되었습니다.", null)
        pendingGalleryResult = null
        ioExecutor.shutdownNow()
        super.onDestroy()
    }

    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray,
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == GALLERY_WRITE_PERMISSION_REQUEST) {
            val pending = pendingGalleryImport ?: return
            pendingGalleryImport = null
            if (hasGalleryWritePermission()) {
                galleryImport(pending.ownerId, pending.arguments, pending.result)
            } else {
                pending.result.error("storage_permission_denied", "사진을 저장하려면 저장소 권한이 필요합니다.", null)
            }
            return
        }
        if (requestCode == GALLERY_READ_PERMISSION_REQUEST) {
            val pending = pendingGalleryRead ?: return
            pendingGalleryRead = null
            if (hasGalleryReadPermission()) {
                listGalleryImages(pending.ownerId, pending.legacyScope, pending.result)
            } else {
                pending.result.error("gallery_permission_denied",
                    "자동 복원을 위해 사진 접근 권한에서 '모든 사진 허용'을 선택해 주세요.", null)
            }
            return
        }
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

    private fun hasGalleryWritePermission(): Boolean = Build.VERSION.SDK_INT >= 29 ||
        Build.VERSION.SDK_INT < 23 || checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) ==
            PackageManager.PERMISSION_GRANTED

    private fun hasGalleryReadPermission(): Boolean = hasGalleryWritePermission() && when {
        Build.VERSION.SDK_INT >= 33 -> checkSelfPermission(Manifest.permission.READ_MEDIA_IMAGES) ==
            PackageManager.PERMISSION_GRANTED
        Build.VERSION.SDK_INT >= 23 -> checkSelfPermission(Manifest.permission.READ_EXTERNAL_STORAGE) ==
            PackageManager.PERMISSION_GRANTED
        else -> true
    }

    private fun listGalleryImages(ownerId: String, legacyScope: String, result: MethodChannel.Result) {
        if (!hasGalleryReadPermission()) {
            if (pendingGalleryRead != null) {
                result.error("gallery_busy", "이미지 읽기 권한을 확인하는 중입니다.", null)
                return
            }
            pendingGalleryRead = PendingGalleryRead(ownerId, legacyScope, result)
            if (Build.VERSION.SDK_INT >= 34) {
                requestPermissions(arrayOf(Manifest.permission.READ_MEDIA_IMAGES,
                    Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED), GALLERY_READ_PERMISSION_REQUEST)
            } else if (Build.VERSION.SDK_INT <= 28) {
                requestPermissions(arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE,
                    Manifest.permission.WRITE_EXTERNAL_STORAGE), GALLERY_READ_PERMISSION_REQUEST)
            } else {
                requestPermissions(arrayOf(
                    if (Build.VERSION.SDK_INT >= 33) Manifest.permission.READ_MEDIA_IMAGES
                    else Manifest.permission.READ_EXTERNAL_STORAGE), GALLERY_READ_PERMISSION_REQUEST)
            }
            return
        }
        ioExecutor.execute {
            try {
                val images = galleryStore.list(ownerId, legacyScope)
                runOnUiThread { result.success(images) }
            } catch (error: Exception) {
                runOnUiThread { result.error("gallery_read_failed", error.message, null) }
            }
        }
    }

    private fun galleryImport(ownerId: String, arguments: Map<*, *>?, result: MethodChannel.Result) {
        if (!hasGalleryWritePermission()) {
            if (pendingGalleryImport != null) {
                result.error("gallery_busy", "이미지 저장 권한을 확인하는 중입니다.", null)
                return
            }
            pendingGalleryImport = PendingGalleryImport(ownerId, arguments, result)
            requestPermissions(arrayOf(Manifest.permission.WRITE_EXTERNAL_STORAGE), GALLERY_WRITE_PERMISSION_REQUEST)
            return
        }
        ioExecutor.execute {
            try {
                val source = File(arguments?.get("sourcePath") as? String ?: error("이미지 경로가 없습니다."))
                val title = (arguments["title"] as? String)?.trim() ?: error("이미지 이름이 없습니다.")
                val tags = (arguments["tags"] as? List<*>)?.filterIsInstance<String>()
                    ?.filter { it.isNotBlank() }?.distinct().orEmpty().ifEmpty { listOf("장르 설정되지 않음") }
                val width = (arguments["width"] as? Number)?.toInt() ?: error("이미지 너비가 없습니다.")
                val height = (arguments["height"] as? Number)?.toInt() ?: error("이미지 높이가 없습니다.")
                require(title.isNotBlank() && width > 0 && height > 0)
                val ratio = JSONObject(WallpaperRatio.fromMap(arguments["ratio"] as? Map<*, *>).toMap())
                val image = galleryStore.importImage(ownerId, source, source.name, title, tags, width, height,
                    arguments["mode"] as? String ?: "original", ratio)
                runOnUiThread { result.success(image) }
            } catch (error: Exception) {
                runOnUiThread { result.error("local_import_failed", error.message, null) }
            }
        }
    }

    /** Requests user consent for modifying photos created before a reinstall (Android 10+). */
    private fun galleryMutation(result: MethodChannel.Result, action: () -> Unit) {
        fun attempt(retryAllowed: Boolean) {
            ioExecutor.execute {
                try {
                    action()
                    runOnUiThread { result.success(null) }
                } catch (need: GalleryLocalImageStore.MediaAccessRequired) {
                    if (!retryAllowed || pendingGalleryAction != null) {
                        runOnUiThread { result.error("media_permission_required", "사진 수정 권한이 필요합니다.", null) }
                        return@execute
                    }
                    runOnUiThread {
                        try {
                            val sender = if (Build.VERSION.SDK_INT >= 30) {
                                if (need.deleting) MediaStore.createDeleteRequest(contentResolver, listOf(need.mediaUri)).intentSender
                                else MediaStore.createWriteRequest(contentResolver, listOf(need.mediaUri)).intentSender
                            } else if (Build.VERSION.SDK_INT == 29 && need.cause is RecoverableSecurityException) {
                                (need.cause as RecoverableSecurityException).userAction.actionIntent.intentSender
                            } else {
                                result.error("media_permission_required", "갤러리에서 사진 수정 권한을 허용해 주세요.", null)
                                return@runOnUiThread
                            }
                            pendingGalleryAction = { attempt(false) }
                            pendingGalleryResult = result
                            startIntentSenderForResult(sender, GALLERY_EDIT_PERMISSION_REQUEST,
                                null, 0, 0, 0)
                        } catch (error: Exception) {
                            pendingGalleryAction = null
                            pendingGalleryResult = null
                            result.error("media_permission_failed", error.message, null)
                        }
                    }
                } catch (error: Exception) {
                    runOnUiThread { result.error("gallery_update_failed", error.message, null) }
                }
            }
        }
        attempt(true)
    }

    @Deprecated("Used for explicit gallery write authorization")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == GALLERY_EDIT_PERMISSION_REQUEST) {
            val continuation = pendingGalleryAction
            val pendingResult = pendingGalleryResult
            pendingGalleryAction = null
            pendingGalleryResult = null
            if (resultCode == RESULT_OK) continuation?.invoke()
            else pendingResult?.error("media_permission_denied", "사진 수정 권한을 허용하지 않았습니다.", null)
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
        val ratio = WallpaperRatio.fromMap(arguments?.get("ratio") as? Map<*, *>)
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
                    .put("ratio", JSONObject(ratio.toMap()))
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
                    if (arguments?.get("ratio") is Map<*, *>) {
                        item.put("ratio", JSONObject(WallpaperRatio.fromMap(arguments["ratio"] as Map<*, *>).toMap()))
                    }
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

    private fun saveLibraryOrder(ownerId: String, scope: String, arguments: Map<*, *>?, result: MethodChannel.Result) {
        val sort = arguments?.get("sort") as? String ?: "manual"
        require(sort in setOf("manual", "newest", "oldest", "korean", "english")) { "올바르지 않은 정렬 방식입니다." }
        val ids = (arguments?.get("ids") as? List<*>)?.filterIsInstance<String>()?.distinct() ?: emptyList()
        ioExecutor.execute {
            try {
                synchronized(localImagesLock) {
                    val ownedIds = galleryStore.list(ownerId, scope)
                        .mapNotNull { it["id"] as? String }.toSet()
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

    private fun localWallpaperItem(ownerId: String, id: String?): JSONObject {
        require(!id.isNullOrBlank()) { "이미지 ID가 없습니다." }
        return galleryStore.item(ownerId, id)
    }

    private fun previewLocalWallpaper(ownerId: String, id: String?, result: MethodChannel.Result) {
        val item = localWallpaperItem(ownerId, id)
        val source = File(item.getString("path")).canonicalFile
        val ratio = ratioFromJson(item.optJSONObject("ratio"))
        val display = deviceDisplaySize()
        ioExecutor.execute {
            var preview: File? = null
            try {
                val directory = File(cacheDir, "wallpaper_previews")
                check(directory.exists() || directory.mkdirs())
                // A granted URI must outlive the launcher preview, not the method
                // call. Retain recent copies and clean up expired cache entries.
                val expiresBefore = System.currentTimeMillis() - 24 * 60 * 60 * 1000L
                directory.listFiles()?.filter { it.lastModified() < expiresBefore }?.forEach { it.delete() }
                val prepared = File.createTempFile("preview-", ".png", directory)
                preview = prepared
                val bitmap = WallpaperImageProcessor.render(source, ratio, display)
                try {
                    FileOutputStream(prepared).use { output ->
                        check(bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, output))
                    }
                } finally {
                    bitmap.recycle()
                }
                runOnUiThread {
                    try {
                        val uri = FileProvider.getUriForFile(this, "$packageName.wallpaper_images", prepared)
                        // Keep the existing Android launcher preview with a crop
                        // already prepared from this account's saved ratio.
                        val intent = WallpaperManager.getInstance(this).getCropAndSetWallpaperIntent(uri)
                            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                        intent.clipData = ClipData.newRawUri("wallpaper", uri)
                        startActivity(intent)
                        result.success(true)
                    } catch (_: ActivityNotFoundException) {
                        prepared.delete()
                        result.success(false)
                    } catch (_: IllegalArgumentException) {
                        prepared.delete()
                        result.success(false)
                    } catch (_: SecurityException) {
                        prepared.delete()
                        result.success(false)
                    }
                }
            } catch (_: Exception) {
                preview?.delete()
                runOnUiThread { result.success(false) }
            }
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

    private fun ratioFromJson(value: JSONObject?): WallpaperRatio = WallpaperRatio.fromMap(value?.let {
        it.keys().asSequence().associateWith { key -> it.opt(key).takeUnless { field -> field == JSONObject.NULL } }
    })

    private fun jsonObjectToMap(item: JSONObject): Map<String, Any?> = mapOf(
        "id" to item.getString("id"),
        "title" to item.getString("title"),
        "path" to item.getString("path"),
        "width" to item.getInt("width"),
        "height" to item.getInt("height"),
        "mode" to item.optString("mode", "original"),
        "ratio" to ratioFromJson(item.optJSONObject("ratio")).toMap(),
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

    @Suppress("DEPRECATION")
    private fun deviceDisplaySize(): Point {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            val bounds = windowManager.maximumWindowMetrics.bounds
            return Point(bounds.width(), bounds.height())
        }
        val metrics = DisplayMetrics()
        windowManager.defaultDisplay.getRealMetrics(metrics)
        return Point(metrics.widthPixels, metrics.heightPixels)
    }

    private fun wallpaperTarget(rawTarget: Any?, result: MethodChannel.Result): WallpaperTarget? {
        return try {
            WallpaperTarget.fromArgument(rawTarget).also { it.requireSupported(Build.VERSION.SDK_INT) }
        } catch (error: IllegalArgumentException) {
            result.error("invalid_target", error.message, null)
            null
        } catch (error: UnsupportedOperationException) {
            result.error("unsupported_target", error.message, null)
            null
        }
    }

    private fun applyStaticWallpaper(url: String?, rawTarget: Any?, ratio: WallpaperRatio, result: MethodChannel.Result) {
        if (url.isNullOrBlank()) {
            result.error("invalid_url", "배경화면 URL이 없습니다.", null)
            return
        }

        val target = wallpaperTarget(rawTarget, result) ?: return
        applyWallpaperSource(target, ratio, result) { openHttpStream(url) }
    }

    private fun applyLocalStaticWallpaper(sourcePath: String?, rawTarget: Any?, ratio: WallpaperRatio, result: MethodChannel.Result) {
        if (sourcePath.isNullOrBlank()) {
            result.error("invalid_source", "배경화면 이미지가 없습니다.", null)
            return
        }
        val target = wallpaperTarget(rawTarget, result) ?: return
        applyWallpaperSource(target, ratio, result) {
            val source = File(sourcePath)
            check(source.isFile) { "배경화면 이미지 파일을 찾을 수 없습니다." }
            source.inputStream()
        }
    }

    private fun applyWallpaperSource(
        target: WallpaperTarget,
        ratio: WallpaperRatio,
        result: MethodChannel.Result,
        openSource: () -> InputStream,
    ) {
        val display = deviceDisplaySize()
        ioExecutor.execute {
            try {
                wallpaperController.apply(target, prepareSource = { source ->
                    val prepared = File.createTempFile("wallpaper-crop-", ".png", cacheDir)
                    try {
                        WallpaperImageProcessor.writePng(source, ratio, display, prepared)
                        prepared
                    } catch (error: Exception) {
                        prepared.delete()
                        throw error
                    }
                }, openSource = openSource)
                val preferences = getSharedPreferences("live_layer", MODE_PRIVATE).edit()
                target.screens.forEach { screen ->
                    preferences.putString(
                        staticWallpaperPathKey(screen),
                        File(File(filesDir, APPLIED_WALLPAPER_DIRECTORY), screen.cacheFileName).absolutePath,
                    )
                }
                check(preferences.commit()) { "배경화면 설정 정보를 저장하지 못했습니다." }
                runOnUiThread { result.success(null) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("wallpaper_failed", "배경화면을 적용하지 못했습니다.", error.message)
                }
            }
        }
    }

    private fun staticWallpaperPathKey(screen: WallpaperTarget): String = when (screen) {
        WallpaperTarget.HOME -> "static_wallpaper_path"
        WallpaperTarget.LOCK -> "static_lock_wallpaper_path"
        WallpaperTarget.BOTH -> error("A source path belongs to one screen.")
    }

    private fun clearWallpaper(rawTarget: Any?, result: MethodChannel.Result) {
        val target = wallpaperTarget(rawTarget, result) ?: return
        ioExecutor.execute {
            try {
                wallpaperController.clear(target)
                val preferences = getSharedPreferences("live_layer", MODE_PRIVATE).edit()
                target.screens.forEach { preferences.remove(staticWallpaperPathKey(it)) }
                check(preferences.commit()) { "배경화면 설정 정보를 저장하지 못했습니다." }
                runOnUiThread { result.success(null) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("wallpaper_clear_failed", "배경화면을 제거하지 못했습니다.", error.message)
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
        const val GALLERY_READ_PERMISSION_REQUEST = 4102
        const val GALLERY_EDIT_PERMISSION_REQUEST = 4103
        const val GALLERY_WRITE_PERMISSION_REQUEST = 4104
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
