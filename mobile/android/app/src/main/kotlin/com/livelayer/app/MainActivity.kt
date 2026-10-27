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
import android.graphics.Point
import android.util.DisplayMetrics
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import io.flutter.plugin.common.EventChannel
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.io.FilterInputStream
import java.io.InputStream
import java.net.HttpURLConnection
import java.net.URL
import java.util.Locale
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
    @Volatile private var pendingLiveActivation: MethodChannel.Result? = null
    private val imageStore by lazy { PrivateImageStore(File(filesDir, "image_library")) }
    private val legacyGalleryImporter by lazy { LegacyGalleryImporter(applicationContext, imageStore) }
    private val live2DStore by lazy { Live2DStore(applicationContext) }
    private var tiltEvents: EventChannel.EventSink? = null
    private val tiltSensor by lazy {
        TiltSensor(applicationContext, { windowManager.defaultDisplay.rotation }) { x, y ->
            tiltEvents?.success(listOf(x.toDouble(), y.toDouble()))
        }
    }
    private data class PendingLegacyMigration(val ownerId: String, val scope: String,
        val result: MethodChannel.Result, val summary: LegacyImageMigration.Result? = null)
    private var pendingLegacyMigration: PendingLegacyMigration? = null
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        CompanionRuntime.initialize(applicationContext)
        val companionState = CompanionStore(applicationContext).read()
        if (companionState.optBoolean("remember") && companionState.optBoolean("enabled") && companionState.has("session"))
            androidx.core.content.ContextCompat.startForegroundService(this, Intent(this, CompanionSyncService::class.java))
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "upscale/companion").setMethodCallHandler { call, result ->
            if (call.method == "status") { result.success(CompanionRuntime.status()); return@setMethodCallHandler }
            if (call.method in listOf("login", "logout", "disable", "reconnect")) {
                CompanionRuntime.invalidate()
                stopService(Intent(this, CompanionSyncService::class.java))
            }
            CompanionRuntime.action(call.method, call.arguments as? Map<*, *> ?: emptyMap<Any, Any>()) { value, error ->
                runOnUiThread {
                    if (error != null) result.error("companion_failed", error, null)
                    else try {
                        if (call.method == "activateLive") {
                            startActivity(Intent(WallpaperManager.ACTION_CHANGE_LIVE_WALLPAPER)
                                .putExtra(WallpaperManager.EXTRA_LIVE_WALLPAPER_COMPONENT, ComponentName(this, LiveLayerWallpaperService::class.java)))
                        } else if (call.method in listOf("login", "enable", "reconnect")) {
                            if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
                                requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 2102)
                            androidx.core.content.ContextCompat.startForegroundService(this, Intent(this, CompanionSyncService::class.java))
                        }
                        result.success(value)
                    } catch (failure: Exception) { result.error("companion_failed", failure.message, null) }
                }
            }
        }
        EventChannel(flutterEngine.dartExecutor.binaryMessenger, "live_layer/tilt")
            .setStreamHandler(object : EventChannel.StreamHandler {
                override fun onListen(arguments: Any?, events: EventChannel.EventSink) {
                    tiltEvents = events
                    tiltSensor.start()
                }
                override fun onCancel(arguments: Any?) { tiltSensor.stop(); tiltEvents = null }
            })
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
                    "getActiveLive2DTarget" -> result.success(activeLiveWallpaperFlags())
                    "cloneLive2DDraft" -> ioExecutor.execute {
                        try {
                            val draft = live2DStore.cloneDraft(call.argument<String>("draftId") ?: error("분리 ID가 없습니다."))
                            runOnUiThread { result.success(draft) }
                        } catch (error: Exception) { runOnUiThread { result.error("live2d_failed", error.message, null) } }
                    }
                    "activateLive2DDraft" -> ioExecutor.execute {
                        try {
                            val manifest = live2DStore.activateDraft(call.argument<String>("draftId") ?: error("분리 ID가 없습니다."))
                            runOnUiThread { launchLiveActivation(manifest, result,
                                allowDirect = call.argument<Boolean>("showSettings") != true) }
                        } catch (error: Exception) {
                            runOnUiThread { result.error("live2d_failed", error.message, null) }
                        }
                    }
                    "splitLive2D" -> ioExecutor.execute {
                        try {
                            val source = File(call.argument<String>("sourcePath") ?: error("이미지가 없습니다."))
                            val draft = live2DStore.split(source)
                            runOnUiThread { result.success(draft) }
                        } catch (error: Exception) {
                            runOnUiThread { result.error("live2d_failed", error.cause?.message ?: error.message, null) }
                        }
                    }
                    "editLive2D" -> ioExecutor.execute {
                        try {
                            val draft = live2DStore.edit(
                                call.argument<String>("draftId") ?: error("분리 ID가 없습니다."),
                                File(call.argument<String>("backgroundPath") ?: error("배경 이미지가 없습니다.")),
                                File(call.argument<String>("foregroundPath") ?: error("전경 이미지가 없습니다.")),
                            )
                            runOnUiThread { result.success(draft) }
                        } catch (error: Exception) {
                            runOnUiThread { result.error("live2d_failed", error.message, null) }
                        }
                    }
                    "discardLive2D" -> ioExecutor.execute {
                        try {
                            live2DStore.discard(call.argument<String>("draftId") ?: error("분리 ID가 없습니다."))
                            runOnUiThread { result.success(null) }
                        } catch (error: Exception) {
                            runOnUiThread { result.error("live2d_failed", error.message, null) }
                        }
                    }
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
                        "attachLocalLive2D" -> storageMutation(result) {
                            val id = call.argument<String>("id") ?: error("이미지 ID가 없습니다.")
                            val item = imageStore.requireEditable(ownerId!!, id)
                            live2DStore.attach(ownerId, id, call.argument<String>("draftId") ?: error("분리 ID가 없습니다."))
                            try {
                                if (item.optString("mode") != "live2d") {
                                    imageStore.update(ownerId, id) { it.put("mode", "live2d") }
                                }
                            } catch (error: Exception) {
                                live2DStore.delete(ownerId, id)
                                throw error
                            }
                        }
                        "getLocalLive2D" -> ioExecutor.execute {
                            try {
                                val id = call.argument<String>("id") ?: error("이미지 ID가 없습니다.")
                                imageStore.item(ownerId!!, id)
                                val layers = live2DStore.layers(ownerId, id)
                                runOnUiThread { result.success(layers) }
                            } catch (error: Exception) {
                                runOnUiThread { result.error("live2d_failed", error.message, null) }
                            }
                        }
                        "getLocalLive2DDraft" -> ioExecutor.execute {
                            try {
                                val id = call.argument<String>("id") ?: error("이미지 ID가 없습니다.")
                                val item = imageStore.requireEditable(ownerId!!, id)
                                require(item.optString("mode") == "live2d") { "Live 2D 이미지가 아닙니다." }
                                val draft = live2DStore.editableLayers(ownerId, id)
                                runOnUiThread { result.success(draft) }
                            } catch (error: Exception) { runOnUiThread { result.error("image_edit_failed", error.message, null) } }
                        }
                        "saveLocalImageEdits" -> ioExecutor.execute {
                            try {
                                val id = call.argument<String>("id") ?: error("이미지 ID가 없습니다.")
                                val draftId = call.argument<String>("draftId")
                                val updated = imageStore.saveEdits(ownerId!!, id,
                                    File(call.argument<String>("sourcePath") ?: error("이미지가 없습니다.")),
                                    call.argument<String>("title") ?: error("이름이 없습니다."),
                                    call.argument<List<String>>("tags") ?: error("태그가 없습니다."),
                                    call.argument<Int>("width") ?: 0, call.argument<Int>("height") ?: 0,
                                    JSONObject(WallpaperRatio.fromMap(call.argument<Map<*, *>>("ratio")).toMap()),
                                    draftId != null,
                                    if (draftId == null) null else { destination -> live2DStore.copyDraftLayers(draftId, destination) })
                                runOnUiThread { result.success(updated) }
                            } catch (error: Exception) { runOnUiThread { result.error("image_edit_failed", error.message, null) } }
                        }
                        "activateLocalLive2D" -> prepareOwnedLiveWallpaper(ownerId!!,
                            call.argument<String>("id"), result, call.argument<Any>("target"),
                            forcePreview = call.argument<Boolean>("showSettings") == true)
                        "applyLocalLive2D" -> applyOwnedLiveWallpaper(ownerId!!,
                            call.argument<String>("id"), call.argument<Any>("target"), result)
                        "saveImageToGallery" -> enqueueLocalImageSave(
                            call.argument<String>("sourcePath"),
                            call.argument<String>("fileName"),
                            result,
                        )
                        "listLocalImages" -> listPrivateImages(ownerId!!, scope, result)
                        "migrateGalleryImages" -> migrateLegacyImages(ownerId!!, scope, result)
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
                                    if (item.optString("mode") == "live2d") {
                                        applyOwnedLiveWallpaper(ownerId, item.getString("id"), call.argument<Any>("target"), result)
                                        return@execute
                                    }
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
                        "importLocalImage" -> privateImport(
                            ownerId!!, call.arguments as? Map<*, *>, result)
                        "markLocalImageShared" -> storageMutation(result) {
                            val args = call.arguments as? Map<*, *>
                            val id = args?.get("id") as? String ?: error("이미지 ID가 없습니다.")
                            val project = args["cloudProjectId"] as? String ?: error("프로젝트 ID가 없습니다.")
                            val post = args["galleryPostId"] as? String ?: error("공유 ID가 없습니다.")
                            imageStore.update(ownerId!!, id) {
                                it.put("cloudProjectId", project).put("galleryPostId", post)
                            }
                        }
                        "markLocalImageUnshared" -> storageMutation(result) {
                            imageStore.update(ownerId!!, call.argument<String>("id") ?: error("이미지 ID가 없습니다.")) {
                                it.put("cloudProjectId", JSONObject.NULL).put("galleryPostId", JSONObject.NULL)
                            }
                        }
                        "updateLocalImageTag" -> storageMutation(result) {
                            val args = call.arguments as? Map<*, *>
                            val id = args?.get("id") as? String ?: error("이미지 ID가 없습니다.")
                            imageStore.requireEditable(ownerId!!, id)
                            val tags = (args["tags"] as? List<*>)?.filterIsInstance<String>()
                                ?.filter { it.isNotBlank() } ?: error("태그가 없습니다.")
                            require(tags.isNotEmpty())
                            imageStore.update(ownerId!!, id) {
                                it.put("tags", JSONArray(tags)).put("tag", tags.first())
                            }
                        }
                        "updateLocalImageDetails" -> storageMutation(result) {
                            val args = call.arguments as? Map<*, *>
                            val id = args?.get("id") as? String ?: error("이미지 ID가 없습니다.")
                            imageStore.requireEditable(ownerId!!, id)
                            val title = (args["title"] as? String)?.trim() ?: error("이름이 없습니다.")
                            val tags = (args["tags"] as? List<*>)?.filterIsInstance<String>()
                                ?.filter { it.isNotBlank() } ?: error("태그가 없습니다.")
                            require(title.isNotBlank() && title.length <= 160 && tags.isNotEmpty())
                            imageStore.update(ownerId!!, id) {
                                it.put("title", title).put("tags", JSONArray(tags)).put("tag", tags.first())
                                if (args["ratio"] is Map<*, *>) {
                                    it.put("ratio", JSONObject(WallpaperRatio.fromMap(args["ratio"] as Map<*, *>).toMap()))
                                }
                            }
                        }
                        "deleteLocalImage" -> storageMutation(result) {
                            val id = call.argument<String>("id") ?: error("이미지 ID가 없습니다.")
                            imageStore.delete(ownerId!!, id)
                            live2DStore.delete(ownerId, id)
                        }
                        else -> result.notImplemented()
                    }
                } catch (error: Exception) {
                    result.error("local_storage_failed", error.message, null)
                }
            }
    }

    override fun onDestroy() {
        tiltSensor.stop()
        tiltEvents = null
        pendingDownload?.result?.error("cancelled", "다운로드 요청이 취소되었습니다.", null)
        pendingDownload = null
        pendingLocalSave?.result?.error("cancelled", "로컬 저장 요청이 취소되었습니다.", null)
        pendingLocalSave = null
        pendingLiveActivation?.error("cancelled", "Live 2D 활성화가 취소되었습니다.", null)
        pendingLiveActivation = null
        pendingLegacyMigration?.result?.error("cancelled", "이미지 이전 요청이 취소되었습니다.", null)
        pendingLegacyMigration = null
        ioExecutor.shutdownNow()
        super.onDestroy()
    }

    override fun onPause() { tiltSensor.stop(); super.onPause() }

    override fun onResume() { super.onResume(); if (tiltEvents != null) tiltSensor.start() }

    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray,
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == GALLERY_READ_PERMISSION_REQUEST) {
            val pending = pendingLegacyMigration ?: return
            pendingLegacyMigration = null
            if (hasLegacyMigrationPermission()) migrateLegacyImages(pending.ownerId, pending.scope, pending.result)
            else pending.result.error("migration_permission_denied", "기존 이미지 이전을 위해 사진 접근 권한이 필요합니다. 새 이미지 저장에는 권한이 필요하지 않습니다.", null)
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

    private fun listPrivateImages(ownerId: String, legacyScope: String, result: MethodChannel.Result) {
        ioExecutor.execute {
            try {
                runCatching { legacyGalleryImporter.migrate(ownerId, legacyScope) }
                    .onFailure { android.util.Log.w("PrivateImages", "Legacy migration can be retried", it) }
                val images = imageStore.list(ownerId)
                runOnUiThread { result.success(images) }
            } catch (error: Exception) {
                runOnUiThread { result.error("local_read_failed", error.message, null) }
            }
        }
    }
    private fun privateImport(ownerId: String, arguments: Map<*, *>?, result: MethodChannel.Result) {
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
                val image = imageStore.importImage(ownerId, source, arguments["fileName"] as? String ?: source.name, title, tags, width, height,
                    arguments["mode"] as? String ?: "original", ratio)
                runOnUiThread { result.success(image) }
            } catch (error: Exception) {
                runOnUiThread { result.error("local_import_failed", error.message, null) }
            }
        }
    }

    private fun storageMutation(result: MethodChannel.Result, action: () -> Unit) {
        ioExecutor.execute {
            try { action(); runOnUiThread { result.success(null) } }
            catch (error: Exception) { runOnUiThread { result.error("local_update_failed", error.message, null) } }
        }
    }

    private fun migrateLegacyImages(ownerId: String, scope: String, result: MethodChannel.Result) {
        if (pendingLegacyMigration != null) {
            result.error("migration_busy", "기존 이미지 이전이 진행 중입니다.", null)
            return
        }
        if (!hasLegacyMigrationPermission()) {
            pendingLegacyMigration = PendingLegacyMigration(ownerId, scope, result)
            val permissions = if (Build.VERSION.SDK_INT >= 33) arrayOf(Manifest.permission.READ_MEDIA_IMAGES)
                else if (Build.VERSION.SDK_INT <= 28) arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE, Manifest.permission.WRITE_EXTERNAL_STORAGE)
                else arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE)
            requestPermissions(permissions, GALLERY_READ_PERMISSION_REQUEST)
            return
        }
        ioExecutor.execute {
            try {
                val summary = legacyGalleryImporter.migrate(ownerId, scope, force = true)
                val legacyConsent = legacyGalleryImporter.deletionConsent(summary.pending.firstOrNull())
                runOnUiThread {
                    if (summary.pending.isNotEmpty() && (Build.VERSION.SDK_INT >= 30 || legacyConsent != null)) {
                        try {
                            pendingLegacyMigration = PendingLegacyMigration(ownerId, scope, result, summary)
                            val sender = if (Build.VERSION.SDK_INT >= 30) MediaStore.createDeleteRequest(contentResolver,
                                summary.pending.map { Uri.parse(it) }).intentSender else legacyConsent!!
                            startIntentSenderForResult(sender,
                                GALLERY_EDIT_PERMISSION_REQUEST, null, 0, 0, 0)
                        } catch (error: Exception) {
                            pendingLegacyMigration = null
                            result.success(migrationSummary(summary))
                        }
                    } else result.success(migrationSummary(summary))
                }
            } catch (error: Exception) { runOnUiThread { result.error("migration_failed", error.message, null) } }
        }
    }

    private fun finishLegacyMigration(approved: Boolean) {
        val pending = pendingLegacyMigration ?: return
        pendingLegacyMigration = null
        val summary = pending.summary ?: return
        if (!approved) { pending.result.success(migrationSummary(summary)); return }
        ioExecutor.execute {
            try {
                val refreshed = legacyGalleryImporter.migrate(pending.ownerId, pending.scope, force = true)
                val total = refreshed.copy(imported = summary.imported + refreshed.imported,
                    removed = summary.removed + (if (Build.VERSION.SDK_INT >= 30) summary.pending.size else 0) + refreshed.removed)
                runOnUiThread { pending.result.success(migrationSummary(total)) }
            } catch (error: Exception) { runOnUiThread { pending.result.error("migration_failed", error.message, null) } }
        }
    }

    private fun hasLegacyMigrationPermission(): Boolean = legacyGalleryImporter.hasReadPermission() &&
        (Build.VERSION.SDK_INT >= 29 || checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED)

    private fun migrationSummary(summary: LegacyImageMigration.Result): Map<String, Any> = mapOf(
        "imported" to summary.imported, "removed" to summary.removed,
        "remaining" to summary.pending.size, "failed" to summary.failed)
    @Deprecated("Used for explicit gallery write authorization")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == LIVE_2D_PICKER_REQUEST) {
            val preferences = getSharedPreferences("live_layer", MODE_PRIVATE)
            val path = preferences.getString("preview_manifest_path", null)
            val edit = preferences.edit().remove("preview_manifest_path")
            val confirmed = path != null && (resultCode == RESULT_OK || preferences.getString("manifest_path", null) == path)
            if (confirmed) edit.putString("manifest_path", path)
            if (edit.commit()) {
                // A prior confirmed selection can still belong to a separate lock/home
                // engine on Android 16. Only discard an unselected preview copy.
                val expired = if (confirmed) null else path
                if (expired != preferences.getString("manifest_path", null)) {
                    ioExecutor.execute { live2DStore.discardWallpaper(expired) }
                }
            }
            pendingLiveActivation?.success(confirmed)
            pendingLiveActivation = null
            return
        }
        if (requestCode == GALLERY_EDIT_PERMISSION_REQUEST) finishLegacyMigration(resultCode == RESULT_OK)
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

    private fun saveLibraryOrder(ownerId: String, scope: String, arguments: Map<*, *>?, result: MethodChannel.Result) {
        val sort = arguments?.get("sort") as? String ?: "manual"
        require(sort in setOf("manual", "newest", "oldest", "korean", "english")) { "올바르지 않은 정렬 방식입니다." }
        val ids = (arguments?.get("ids") as? List<*>)?.filterIsInstance<String>()?.distinct() ?: emptyList()
        ioExecutor.execute {
            try {
                synchronized(localImagesLock) {
                    val ownedIds = imageStore.list(ownerId)
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
        return imageStore.item(ownerId, id)
    }

    private fun prepareOwnedLiveWallpaper(ownerId: String, id: String?, result: MethodChannel.Result,
        requestedTarget: Any? = null, forcePreview: Boolean = false) {
        ioExecutor.execute {
            try {
                val item = localWallpaperItem(ownerId, id)
                require(item.optString("mode") == "live2d") { "먼저 Live 2D 이미지를 분리해 주세요." }
                val manifest = live2DStore.activate(ownerId, item.getString("id"))
                val requestedFlags = requestedTarget?.let { WallpaperTarget.fromArgument(it).flags }
                runOnUiThread { launchLiveActivation(manifest, result, requestedFlags, !forcePreview) }
            } catch (error: Exception) { runOnUiThread { result.error("live2d_failed", error.message, null) } }
        }
    }

    private fun launchLiveActivation(manifest: File, result: MethodChannel.Result,
        requestedFlags: Int? = null, allowDirect: Boolean = true) {
        if (pendingLiveActivation != null) {
            ioExecutor.execute { live2DStore.discardWallpaper(manifest.absolutePath) }
            result.error("live2d_busy", "다른 Live 2D 활성화가 진행 중입니다.", null)
            return
        }
        try {
            val active = activeLiveWallpaperFlags()
            if (LiveWallpaperSelection.canActivateDirect(active, requestedFlags, !allowDirect)) {
                ioExecutor.execute {
                    try {
                        commitLiveSelection(manifest)
                        runOnUiThread { result.success(true) }
                    } catch (error: Exception) {
                        live2DStore.discardWallpaper(manifest.absolutePath)
                        runOnUiThread { result.error("live2d_failed", error.message, null) }
                    }
                }
                return
            }
            check(getSharedPreferences("live_layer", MODE_PRIVATE).edit()
                .putString("preview_manifest_path", manifest.absolutePath).commit())
            pendingLiveActivation = result
            openWallpaperPicker(null)
        } catch (error: Exception) {
            pendingLiveActivation = null
            getSharedPreferences("live_layer", MODE_PRIVATE).edit().remove("preview_manifest_path").apply()
            ioExecutor.execute { live2DStore.discardWallpaper(manifest.absolutePath) }
            result.error("wallpaper_picker_failed", error.message, null)
        }
    }

    private fun activeLiveWallpaperFlags(): Int {
        val manager = WallpaperManager.getInstance(this)
        val component = ComponentName(this, LiveLayerWallpaperService::class.java)
        val home = manager.wallpaperInfo?.component == component
        val lock = if (Build.VERSION.SDK_INT >= 34) manager.getWallpaperInfo(WallpaperManager.FLAG_LOCK)?.component == component
            || (home && manager.getWallpaperId(WallpaperManager.FLAG_LOCK) < 0)
            else home && manager.getWallpaperId(WallpaperManager.FLAG_LOCK) < 0
        return (if (home) WallpaperManager.FLAG_SYSTEM else 0) or (if (lock) WallpaperManager.FLAG_LOCK else 0)
    }

    private fun commitLiveSelection(manifest: File) {
        val manager = WallpaperManager.getInstance(this)
        check(manager.isWallpaperSupported && manager.isSetWallpaperAllowed) { "이 기기에서 배경화면 변경이 허용되지 않습니다." }
        val preferences = getSharedPreferences("live_layer", MODE_PRIVATE)
        val previous = preferences.getString("manifest_path", null)
        val direct = preferences.getString("direct_manifest_path", null)
        if (!preferences.edit().putString("manifest_path", manifest.absolutePath)
                .putString("direct_manifest_path", manifest.absolutePath).commit()) {
            preferences.edit().putString("manifest_path", previous).putString("direct_manifest_path", direct).commit()
            error("배경화면 설정을 저장하지 못했습니다.")
        }
    }

    private fun applyOwnedLiveWallpaper(ownerId: String, id: String?, rawTarget: Any?, result: MethodChannel.Result) {
        val target = wallpaperTarget(rawTarget, result) ?: return
        ioExecutor.execute {
            var manifest: File? = null
            try {
                val item = localWallpaperItem(ownerId, id)
                require(item.optString("mode") == "live2d") { "Live 2D 이미지가 아닙니다." }
                val manager = WallpaperManager.getInstance(this)
                check(manager.isWallpaperSupported && manager.isSetWallpaperAllowed) { "이 기기에서 배경화면 변경이 허용되지 않습니다." }
                if (!LiveWallpaperSelection.canUpdate(target.flags, activeLiveWallpaperFlags())) {
                    runOnUiThread { result.error("live2d_activation_required",
                        "Live 2D 적용에서 Android 배경화면 설정을 먼저 완료해 주세요.", null) }
                    return@execute
                }
                check(pendingLiveActivation == null) { "Live 2D 활성화 확인이 진행 중입니다." }
                val snapshot = live2DStore.activate(ownerId, item.getString("id"))
                manifest = snapshot
                commitLiveSelection(snapshot)
                runOnUiThread { result.success(null) }
            } catch (error: Exception) {
                manifest?.let { live2DStore.discardWallpaper(it.absolutePath) }
                runOnUiThread { result.error("live2d_failed", error.message, null) }
            }
        }
    }

    private fun previewLocalWallpaper(ownerId: String, id: String?, result: MethodChannel.Result) {
        val item = localWallpaperItem(ownerId, id)
        if (item.optString("mode") == "live2d") {
            prepareOwnedLiveWallpaper(ownerId, id, result, forcePreview = true)
            return
        }
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

    private fun ratioFromJson(value: JSONObject?): WallpaperRatio = WallpaperRatio.fromMap(value?.let {
        it.keys().asSequence().associateWith { key -> it.opt(key).takeUnless { field -> field == JSONObject.NULL } }
    })

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
                    live2DStore.stampSelection(File(staging, "manifest.json"))

                    val destination = File(root, safeProjectId)
                    if (destination.exists() && !destination.deleteRecursively()) {
                        error("기존 배경화면 파일을 교체할 수 없습니다.")
                    }
                    check(staging.renameTo(destination)) { "배경화면 파일을 저장할 수 없습니다." }
                    getSharedPreferences("live_layer", MODE_PRIVATE)
                        .edit()
                        .putString("project_id", projectId)
                        .putString("preview_manifest_path", File(destination, "manifest.json").absolutePath)
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
        startActivityForResult(intent, LIVE_2D_PICKER_REQUEST)
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
        const val LIVE_2D_PICKER_REQUEST = 4105
        val localImagesLock = Any()
        const val DOWNLOAD_PERMISSION_REQUEST = 4101
        const val GALLERY_READ_PERMISSION_REQUEST = 4102
        const val GALLERY_EDIT_PERMISSION_REQUEST = 4103

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
