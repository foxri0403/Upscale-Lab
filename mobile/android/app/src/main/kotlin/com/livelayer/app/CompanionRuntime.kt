package com.livelayer.app

import android.app.WallpaperManager
import android.content.Context
import android.os.Build
import android.provider.Settings
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.atomic.AtomicBoolean
import android.hardware.display.DisplayManager
import android.os.Handler
import android.os.Looper

/** All API and image work is serialized. Invalidation is immediate, including during downloads. */
internal object CompanionRuntime {
    private class InactiveWallpaperScreen : IllegalStateException("배경화면을 적용할 화면을 먼저 열어 주세요.")
    private lateinit var context: Context
    private lateinit var store: CompanionStore
    private lateinit var api: CompanionApi
    private val worker = Executors.newSingleThreadScheduledExecutor()
    private val watcher = Executors.newSingleThreadExecutor()
    private val watchSignal = Object()
    private val pollQueued = AtomicBoolean()
    private val inventoryDirty = AtomicBoolean(true)
    private val generation = AtomicLong()
    @Volatile private var initialized = false
    @Volatile private var running = false
    @Volatile private var verified = false
    @Volatile private var connectionState = "disconnected"
    @Volatile private var message = "웹과 같은 계정으로 로그인해 주세요."
    @Volatile private var pending: JSONObject? = null
    private var displayListener: DisplayManager.DisplayListener? = null
    private var wallpaperReceiver: android.content.BroadcastReceiver? = null
    private var nextHeartbeat = 0L
    @Volatile private var profile: JSONObject? = null
    @Volatile private var plan = "확인 중"
    @Volatile private var lastLiveAttempt: String? = null
    private var notifiedLiveId: String? = null
    private fun preferences() = CompanionPreferences(context)
    private fun currentOwner() = store.read().optJSONObject("session")?.optJSONObject("user")?.optString("id") ?: ""
    private fun targetFlag(target: String?) = if (target == "Lock") WallpaperManager.FLAG_LOCK else WallpaperManager.FLAG_SYSTEM
    private fun liveEnabled() = currentOwner().isNotEmpty() && preferences().live(currentOwner())
    fun alarmsEnabled() = currentOwner().isNotEmpty() && preferences().alarms(currentOwner())
    private fun announce(text: String) { CompanionAlerts.show(context, alarmsEnabled(), text) }

    fun inventoryAllowed() = initialized && CompanionPrivacy(context).accepted && verified && store.read().has("session")
    private fun requireConsent() = check(CompanionPrivacy(context).accepted) { "기기·화면 정보 수집·이용 동의가 필요합니다." }

    @Synchronized fun initialize(value: Context) {
        if (initialized) return
        context = value.applicationContext; store = CompanionStore(context); api = CompanionApi(store); initialized = true
        val configuredServer = CompanionApi.normalize(BuildConfig.COMPANION_API_BASE_URL)
        val saved = store.read()
        if (saved.has("server") && saved.optString("server") != configuredServer)
            store.save(JSONObject().put("server", configuredServer))
        worker.scheduleWithFixedDelay({ if (running) poll() }, 0, 15, TimeUnit.SECONDS)
        watcher.execute {
            var cursor: String? = null; var epoch = generation.get()
            while (!Thread.currentThread().isInterrupted) {
                if (!running) { synchronized(watchSignal) { if (!running) watchSignal.wait() }; continue }
                val current = generation.get()
                if (epoch != current) { cursor = null; epoch = current }
                try {
                    val notice = api.request("/api/agent/updates" + (cursor?.let { "?after=$it" } ?: ""), agent = true, retry = false) as JSONObject
                    val next = notice.getString("cursor")
                    if (epoch == generation.get() && running && next != cursor) { cursor = next; pollSoon() }
                } catch (_: Exception) { if (running) pollSoon(); Thread.sleep(3000) }
            }
        }
    }
    @Synchronized private fun observeInventory() {
        check(inventoryAllowed()) { "로그인과 정보 수집 동의가 필요합니다." }
        if (displayListener != null) return
        val listener = object : DisplayManager.DisplayListener {
            private fun changed() { inventoryDirty.set(true); pollSoon() }
            override fun onDisplayAdded(id: Int) = changed()
            override fun onDisplayRemoved(id: Int) = changed()
            override fun onDisplayChanged(id: Int) = changed()
        }
        (context.getSystemService(Context.DISPLAY_SERVICE) as DisplayManager).registerDisplayListener(listener, Handler(Looper.getMainLooper()))
        displayListener = listener
        val receiver = object : android.content.BroadcastReceiver() {
            override fun onReceive(context: Context, intent: android.content.Intent) { inventoryDirty.set(true); pollSoon() }
        }
        androidx.core.content.ContextCompat.registerReceiver(context, receiver,
            android.content.IntentFilter(android.content.Intent.ACTION_WALLPAPER_CHANGED), androidx.core.content.ContextCompat.RECEIVER_NOT_EXPORTED)
        wallpaperReceiver = receiver
    }
    @Synchronized private fun stopObservingInventory() {
        displayListener?.let { (context.getSystemService(Context.DISPLAY_SERVICE) as DisplayManager).unregisterDisplayListener(it) }
        wallpaperReceiver?.let { context.unregisterReceiver(it) }
        displayListener = null; wallpaperReceiver = null
    }
    fun invalidate() {
        generation.incrementAndGet(); running = false; connectionState = "disconnected"; pending = null
        stopObservingInventory()
        api.cancelUpdates()
        context.getSharedPreferences("live_layer", Context.MODE_PRIVATE).edit().putLong("companion_valid_until", 0).apply()
    }
    fun resume(): Boolean {
        val state = store.read()
        if (!CompanionPrivacy(context).accepted || !state.has("session")) return false
        if (!running) connectionState = "connecting"
        running = true; synchronized(watchSignal) { watchSignal.notifyAll() }; inventoryDirty.set(true); pollSoon(); return true
    }
    fun pollSoon() { if (initialized && running && pollQueued.compareAndSet(false, true)) worker.execute { pollQueued.set(false); if (running) poll() } }
    fun status(): Map<String, Any?> {
        val state = store.read(); val user = state.optJSONObject("session")?.optJSONObject("user")
        val allowed = inventoryAllowed()
        val displays = CompanionInventoryAccess.read(CompanionPrivacy(context).accepted, allowed) {
            runCatching { CompanionInventory(context).read(applied(state)) }.getOrDefault(JSONArray())
        } ?: JSONArray()
        val waiting = pending
        val requiredFlag = targetFlag(waiting?.optString("target"))
        val active = if (allowed) runCatching { CompanionInventory(context).activeFlags() and requiredFlag != 0 }.getOrDefault(false) else false
        val currentScreen = (0 until displays.length()).map { displays.getJSONObject(it) }.firstOrNull { it.optBoolean("isPrimary") }
        val needsSetup = allowed && waiting?.optString("nativeId") == currentScreen?.optString("nativeId") &&
            LiveActivationPolicy.needsPicker(liveEnabled(), waiting?.has("manifest") == true, active, lastLiveAttempt == waiting?.optString("id"))
        return mapOf("server" to state.optString("server"), "account" to user?.optString("email"),
            "username" to (profile?.optString("username") ?: user?.optString("username")),
            "profileImageUrl" to profile?.optString("profileImageUrl")?.takeIf { it.startsWith("https://") }, "plan" to plan,
            "saveIdentifier" to state.optBoolean("saveIdentifier"), "savedIdentifier" to state.optString("identifier"),
            "liveEnabled" to liveEnabled(), "alarmsEnabled" to alarmsEnabled(), "needsLiveSetup" to needsSetup,
            "notificationPermissionGranted" to (Build.VERSION.SDK_INT < 33 || androidx.core.content.ContextCompat.checkSelfPermission(context, android.Manifest.permission.POST_NOTIFICATIONS) == android.content.pm.PackageManager.PERMISSION_GRANTED),
            "privacyAccepted" to CompanionPrivacy(context).accepted,
            "loggedIn" to (allowed && user != null), "enabled" to running, "connectionState" to connectionState, "message" to message,
            "pendingLive" to (pending != null),
            "osVersion" to (if (allowed) "Android ${Build.VERSION.RELEASE}" else ""), "displays" to (0 until displays.length()).map {
                val item = displays.getJSONObject(it)
                mapOf("name" to item.optString("name"), "width" to item.optInt("width"), "height" to item.optInt("height"),
                    "active" to item.optBoolean("isPrimary"), "foldScreen" to CompanionFoldScreens.isFoldScreen(item.optString("nativeId")),
                    "innerScreen" to (item.optString("nativeId") == CompanionFoldScreens.INNER),
                    "refreshRateHz" to item.optDouble("refreshRateHz").takeIf { hz -> hz.isFinite() }, "canApply" to item.optBoolean("canApplyWallpaper"))
            })
    }
    fun action(name: String, arguments: Map<*, *>, done: (Any?, String?) -> Unit) {
        if (name in listOf("login", "logout")) invalidate()
        if (name in listOf("login", "logout")) verified = false
        worker.execute {
            try {
                if (name !in listOf("acceptPrivacy", "logout", "disable")) requireConsent()
                when (name) {
                    "saveIdentifier" -> {
                        val saved = store.read().put("saveIdentifier", arguments["enabled"] == true)
                        if (arguments["enabled"] == true) saved.put("identifier", (arguments["identifier"] as? String ?: "").trim()) else saved.remove("identifier")
                        store.save(saved)
                    }
                    "refreshProfile" -> { check(inventoryAllowed()) { "로그인이 필요합니다." }; refreshProfile() }
                    "setAlarms" -> {
                        check(inventoryAllowed()) { "로그인이 필요합니다." }
                        val enabled = arguments["enabled"] == true; preferences().setAlarms(currentOwner(), enabled)
                        if (!enabled) CompanionAlerts.cancel(context)
                    }
                    "setLive" -> {
                        check(inventoryAllowed()) { "로그인이 필요합니다." }
                        val enabled = arguments["enabled"] == true
                        if (enabled) { verifyAccount(); api.request("/api/agent/state", agent = true) }
                        preferences().setLive(currentOwner(), enabled); lastLiveAttempt = null
                        context.getSharedPreferences("live_layer", Context.MODE_PRIVATE).edit().putBoolean("companion_live_enabled", enabled).apply()
                        if (enabled) { lease(); pollSoon() } else {
                            context.getSharedPreferences("live_layer", Context.MODE_PRIVATE).edit().putLong("companion_valid_until", 0).apply()
                        }
                    }
                    "acceptPrivacy" -> {
                        require(arguments["accepted"] == true) { "동의 항목을 선택해 주세요." }
                        CompanionPrivacy(context).accept()
                    }
                    "login" -> {
                        val server = CompanionApi.normalize(BuildConfig.COMPANION_API_BASE_URL)
                        val previous = store.read(); verified = false; profile = null; plan = "확인 중"
                        val initial = JSONObject().put("server", server).put("remember", arguments["remember"] == true).put("saveIdentifier", arguments["saveIdentifier"] == true)
                        if (arguments["saveIdentifier"] == true) initial.put("identifier", (arguments["identifier"] as String).trim())
                        store.save(initial)
                        val session = api.request("/api/auth/login", "POST", JSONObject().put("email", (arguments["identifier"] as String).trim())
                            .put("password", arguments["password"] as String), anonymous = true) as JSONObject
                        require(session.optString("refreshToken").isNotBlank()) { "계정 세션을 지원하는 서버가 필요합니다." }
                        val state = store.read().put("session", session).put("enabled", true)
                        val old = previous.optJSONObject("binding")
                        if (old != null && old.optString("server") == server && old.optString("owner") == session.getJSONObject("user").getString("id")) state.put("binding", old)
                        store.save(state); verified = true; bind(true); refreshProfile(); message = "기기 연결 완료. 웹에서 배경화면을 선택해 주세요."
                    }
                    "enable" -> { require(store.read().has("session")) { "로그인이 필요합니다." }; verifyAccount(); bind(false); store.save(store.read().put("enabled", true)) }
                    "logout" -> {
                        val refresh = store.read().optJSONObject("session")?.optString("refreshToken")
                        store.save(store.read().apply { remove("session"); put("enabled", false) }); verified = false
                        profile = null; plan = "확인 중"; CompanionAlerts.cancel(context)
                        if (!refresh.isNullOrBlank()) runCatching { api.request("/api/auth/logout", "POST", JSONObject().put("refreshToken", refresh), anonymous = true) }
                        message = "로그아웃했습니다. 정적 배경화면은 기기에 남습니다."
                    }
                    "reconnect" -> { verifyAccount(); bind(true); store.save(store.read().put("enabled", true)) }
                    "activateLive" -> {
                        check(inventoryAllowed() && liveEnabled()) { "설정에서 Live 2D를 켜 주세요." }
                        val item = pending ?: error("대기 중인 Live 2D 요청이 없습니다.")
                        requireLatest(item)
                        val path = item.getString("manifest")
                        if (lastLiveAttempt == item.getString("id")) { done(mapOf("openLivePicker" to false), null); return@execute }
                        lastLiveAttempt = item.getString("id")
                        lease()
                        check(context.getSharedPreferences("live_layer", Context.MODE_PRIVATE).edit().putString("preview_manifest_path", path).commit())
                        done(mapOf("openLivePicker" to true), null); return@execute
                    }
                    "completeLiveSetup" -> {
                        check(inventoryAllowed()) { "로그인이 필요합니다." }
                        val required = targetFlag(pending?.optString("target"))
                        if (arguments["accepted"] != true && CompanionInventory(context).activeFlags() and required == 0) {
                            preferences().setLive(currentOwner(), false)
                            context.getSharedPreferences("live_layer", Context.MODE_PRIVATE).edit().putBoolean("companion_live_enabled", false).putLong("companion_valid_until", 0).apply()
                            message = "Live 2D 적용을 취소했습니다. 설정에서 다시 켤 수 있습니다."
                        } else { lease(); inventoryDirty.set(true); pollSoon() }
                    }
                    else -> error("지원하지 않는 동작입니다.")
                }
                done(status(), null)
            } catch (error: Exception) { message = error.message ?: "연결에 실패했습니다."; done(null, message) }
        }
    }
    private fun verifyAccount() {
        requireConsent()
        val user = api.request("/api/auth/me") as JSONObject
        require(user.getString("id") == owner(store.read())) { "로그인 계정이 일치하지 않습니다." }; verified = true
        refreshProfile()
    }
    private fun refreshProfile() {
        val owner = currentOwner()
        try {
            val value = api.request("/api/account") as JSONObject
            require(value.getString("id") == owner) { "계정 정보가 일치하지 않습니다." }
            profile = value
            plan = (api.request("/api/tokens/me") as JSONObject).optString("plan", "확인 불가")
        } catch (_: Exception) { plan = "확인 불가" }
    }
    private fun owner(state: JSONObject) = state.getJSONObject("session").getJSONObject("user").getString("id")
    private fun bind(force: Boolean) {
        check(inventoryAllowed()) { "로그인과 정보 수집 동의가 필요합니다." }
        val state = store.read()
        if (state.has("binding") && !force) return
        var id = state.optJSONObject("binding")?.optString("deviceId")
        if (id != null) try {
            val token = api.request("/api/devices/$id/agent-token", "POST", JSONObject()) as JSONObject
            store.save(state.put("binding", JSONObject().put("server", state.getString("server")).put("owner", owner(state))
                .put("deviceId", id).put("token", token.getString("token")))); return
        } catch (error: CompanionApiError) { if (error.status != 404) throw error }
        val inventory = CompanionInventory(context).read(JSONObject())
        val primary = (0 until inventory.length()).map { inventory.getJSONObject(it) }.first { it.getBoolean("isPrimary") }
        val width = primary.getInt("width"); val height = primary.getInt("height")
        val deviceName = runCatching { Settings.Global.getString(context.contentResolver, "device_name") }
            .getOrNull()?.trim()?.takeIf { it.isNotEmpty() }?.take(120) ?: "Android 기기"
        val device = api.request("/api/devices", "POST", JSONObject().put("platform", "Android").put("deviceName", deviceName)
            .put("screenWidth", width).put("screenHeight", height).put("aspectRatio", "$width:$height").put("orientation", if (width >= height) "Landscape" else "Portrait")) as JSONObject
        id = device.getString("id")
        val token = api.request("/api/devices/$id/agent-token", "POST", JSONObject()) as JSONObject
        store.save(state.put("binding", JSONObject().put("server", state.getString("server")).put("owner", owner(state))
            .put("deviceId", id).put("token", token.getString("token"))))
    }
    private fun appliedFile(state: JSONObject): File {
        val server = java.security.MessageDigest.getInstance("SHA-256").digest(state.getString("server").toByteArray()).take(8).joinToString("") { "%02x".format(it) }
        return File(context.filesDir, "companion/applied-${owner(state)}-$server.json")
    }
    private fun applied(state: JSONObject): JSONObject = runCatching { JSONObject(appliedFile(state).readText()) }.getOrDefault(JSONObject())
    private fun saveApplied(state: JSONObject, value: JSONObject) {
        val file = appliedFile(state); file.parentFile!!.mkdirs(); val temp = File(file.path + ".part")
        temp.writeText(value.toString()); check(temp.renameTo(file))
    }
    private fun lease() {
        check(inventoryAllowed()) { "로그인과 정보 수집 동의가 필요합니다." }
        context.getSharedPreferences("live_layer", Context.MODE_PRIVATE).edit().putString("companion_owner", owner(store.read()))
            .putBoolean("companion_live_enabled", liveEnabled())
            .putLong("companion_valid_until", if (liveEnabled()) System.currentTimeMillis() + 60000 else 0).apply()
    }
    private fun requireLatest(command: JSONObject): JSONObject {
        val clear = command.optString("action", "Apply") == "Clear"
        api.request("/api/agent/commands/${command.getString("id")}", agent = true)
        val state = api.request("/api/agent/state", agent = true) as JSONObject
        val displays = state.getJSONArray("displays")
        val display = (0 until displays.length()).map { displays.getJSONObject(it) }.firstOrNull {
            it.getString("id") == command.getString("displayId") && it.getBoolean("isConnected") && it.optBoolean("canApplyWallpaper", true)
        } ?: error("적용 대상 화면이 연결되어 있지 않습니다.")
        if (display.getString("nativeId") != CompanionInventory(context).activeId()) throw InactiveWallpaperScreen()
        val assignments = display.getJSONArray("wallpapers")
        require((0 until assignments.length()).any {
            val item = assignments.getJSONObject(it)
            item.getString("target") == command.getString("target") && item.optLong("sequence") == command.getLong("sequence") &&
                (if (clear) item.isNull("desiredAssetId") else item.optString("desiredAssetId") == command.getString("assetId"))
        }) { "최신 요청으로 변경되었습니다." }
        return display
    }

    private fun applyStatic(manager: WallpaperManager, flag: Int, manifest: File): Int {
        val layer = JSONObject(manifest.readText()).getJSONArray("layers").getJSONObject(0)
        val wallpaperId = File(manifest.parentFile, layer.getString("fileName")).inputStream().use { manager.setStream(it, null, true, flag) }
        check(wallpaperId > 0 && manager.getWallpaperId(flag) == wallpaperId) { "Android 배경화면 적용을 확인하지 못했습니다." }
        return wallpaperId
    }

    private fun selectLive(flag: Int, path: String) {
        val prefs = context.getSharedPreferences("live_layer", Context.MODE_PRIVATE)
        val key = CompanionLiveEngines.selectionKey(flag)
        if (prefs.getString(key, null) == path) return
        CompanionLiveEngines.reset(flag)
        check(prefs.edit().putString(key, path).commit()) { "배경화면 선택을 저장하지 못했습니다." }
    }

    private fun restoreFoldScreen(state: JSONObject, all: JSONObject, saved: JSONObject, display: JSONObject,
        commands: List<JSONObject>, inventory: CompanionInventory, epoch: Long) {
        val screen = display.getString("nativeId")
        val manager = WallpaperManager.getInstance(context)
        val assignments = display.getJSONArray("wallpapers")
        var deferred = false
        var changed = false
        for (index in 0 until assignments.length()) {
            val assignment = assignments.getJSONObject(index)
            val target = assignment.getString("target")
            if (commands.any { it.optString("target") == target }) continue
            val receipt = saved.optJSONObject(target) ?: continue
            if (receipt.optLong("sequence", -1) != assignment.getLong("sequence")) continue
            if (epoch != generation.get() || !running || inventory.activeId() != screen) return
            val flag = targetFlag(target)
            if (receipt.optBoolean("cleared")) {
                if (!CompanionWallpaperRemoval.isCleared(receipt, manager.getWallpaperId(flag))) {
                    manager.clear(flag)
                    receipt.put("wallpaperId", manager.getWallpaperId(flag))
                    changed = true
                }
                continue
            }
            if (receipt.optString("assetId") != assignment.optString("desiredAssetId")) continue
            val manifest = File(receipt.getString("manifest"))
            if (!manifest.isFile) continue
            if (receipt.optString("mode") != "Live2D") {
                if (receipt.optInt("wallpaperId") != manager.getWallpaperId(flag)) {
                    receipt.put("wallpaperId", applyStatic(manager, flag, manifest))
                    changed = true
                }
            } else if (inventory.activeFlags() and flag != 0 && liveEnabled()) {
                selectLive(flag, manifest.path)
            } else {
                deferred = true
                if (pending == null) {
                    pending = JSONObject().put("id", receipt.getString("commandId")).put("assetId", receipt.getString("assetId"))
                        .put("target", target).put("manifest", manifest.path).put("displayId", display.getString("id"))
                        .put("sequence", receipt.getLong("sequence")).put("nativeId", screen).put("restoring", true)
                    message = "이 화면에 저장된 동적 배경화면을 다시 적용하려면 Android 배경화면 선택을 확인해 주세요."
                }
            }
        }
        if (!deferred) all.put("_activeScreen", screen)
        if (changed || !deferred) {
            saveApplied(state, all)
            inventoryDirty.set(true)
        }
    }
    private fun poll() {
        val epoch = generation.get()
        try {
            if (!running || !CompanionPrivacy(context).accepted) return
            if (connectionState != "connected") connectionState = "connecting"
            if (!verified) verifyAccount()
            if (epoch != generation.get() || !running) return
            observeInventory()
            val state = store.read(); val all = applied(state)
            val inventory = CompanionInventory(context)
            var remote = api.request("/api/agent/state", agent = true) as JSONObject
            if (inventoryDirty.getAndSet(false) || System.currentTimeMillis() >= nextHeartbeat) {
                val body = JSONObject().put("revision", remote.getLong("inventoryRevision") + 1).put("osVersion", "Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})")
                    .put("agentVersion", context.packageManager.getPackageInfo(context.packageName, 0).versionName ?: "1.0.1")
                    .put("supportsLive2D", context.packageManager.hasSystemFeature("android.software.live_wallpaper"))
                    .put("supportsLockScreen", true).put("supportsWallpaperRemoval", true).put("displays", inventory.read(all))
                try { remote = api.request("/api/agent/heartbeat", "POST", body, agent = true) as JSONObject; nextHeartbeat = System.currentTimeMillis() + 30000 }
                catch (error: CompanionApiError) { if (error.status != 409) throw error }
            }
            if (epoch != generation.get() || !running) return
            lease()
            val queue = api.request("/api/agent/commands", agent = true) as JSONArray
            if (epoch != generation.get() || !running) return
            connectionState = "connected"
            val screen = inventory.activeId()
            val displays = remote.getJSONArray("displays")
            val display = (0 until displays.length()).map { displays.getJSONObject(it) }
                .firstOrNull { it.optString("nativeId") == screen && it.optBoolean("isConnected") } ?: return
            val commands = (0 until queue.length()).map { queue.getJSONObject(it) }
                .filter { it.getString("displayId") == display.getString("id") }
            val waiting = pending
            if (waiting != null && (waiting.optString("nativeId") != screen ||
                    (!waiting.optBoolean("restoring") && commands.none { it.getString("id") == waiting.optString("id") }))) pending = null
            val saved = if (CompanionFoldScreens.isFoldScreen(screen))
                all.optJSONObject(screen) ?: JSONObject().also { all.put(screen, it) } else all
            if (CompanionFoldScreens.isFoldScreen(screen) && all.optString("_activeScreen") != screen)
                restoreFoldScreen(state, all, saved, display, commands, inventory, epoch)
            if (epoch != generation.get() || !running) return
            val restoring = pending
            if (restoring?.optBoolean("restoring") == true) {
                val flag = targetFlag(restoring.optString("target"))
                val path = restoring.getString("manifest")
                if (commands.any { it.optString("target") == restoring.optString("target") }) pending = null
                else if (liveEnabled() && inventory.activeFlags() and flag != 0) {
                    try { requireLatest(restoring); selectLive(flag, path); pending = null }
                    catch (error: CompanionApiError) { if (error.status !in listOf(404, 409)) throw error; pending = null }
                }
            }
            for (command in commands) {
                if (epoch != generation.get() || !running) return
                val id = command.getString("id"); var didApply = false
                try {
                    val target = command.getString("target")
                    require(target in listOf("Home", "Lock")) { "지원하지 않는 배경화면 대상입니다." }
                    if (command.optString("action", "Apply") == "Clear") {
                        requireLatest(command)
                        if (epoch != generation.get() || !running) return
                        val screen = if (target == "Home") WallpaperTarget.HOME else WallpaperTarget.LOCK
                        val oldAsset = saved.optJSONObject(target)?.optString("assetId")
                        val manager = WallpaperManager.getInstance(context)
                        CompanionWallpaperRemoval.clear(screen, id, saved, AndroidWallpaperPlatform(context),
                            { manager.getWallpaperId(screen.flags) }, { didApply = true }, {
                                saved.put(target, it.getJSONObject(target).put("sequence", command.getLong("sequence")))
                                saveApplied(state, all)
                            })
                        if (pending?.optString("target") == target) pending = null
                        val panels = listOfNotNull(all, all.optJSONObject(CompanionFoldScreens.COVER), all.optJSONObject(CompanionFoldScreens.INNER))
                        if (!oldAsset.isNullOrBlank() && panels.none { panel -> listOf("Home", "Lock").any { panel.optJSONObject(it)?.optString("assetId") == oldAsset } })
                            runCatching { CompanionCache(context).delete(owner(state), state.getString("server"), oldAsset) }
                        api.report(id, "Applied"); message = "배경화면 삭제 완료."; nextHeartbeat = 0
                        continue
                    }
                    require(command.optString("action", "Apply") == "Apply") { "지원하지 않는 명령입니다." }
                    if (command.optString("status") == "Pending") api.report(id, "Downloading")
                    val manifest = api.request("/api/agent/commands/$id/manifest", agent = true) as JSONObject
                    require(manifest.getString("assetId") == command.getString("assetId")) { "결과물 정보가 일치하지 않습니다." }
                    val local = CompanionCache(context).download(owner(state), state.getString("server"), manifest)
                    requireLatest(command)
                    if (epoch != generation.get() || !running) return
                    val flag = targetFlag(target)
                    val manager = WallpaperManager.getInstance(context); val mode = manifest.getString("mode")
                    val old = saved.optJSONObject(target)
                    val screens = inventory.read(all)
                    val reports = (0 until screens.length()).map { screens.getJSONObject(it) }.firstOrNull { it.optBoolean("isPrimary") }?.optJSONArray("wallpapers") ?: JSONArray()
                    val already = old?.optString("commandId") == id && (0 until reports.length()).any {
                        val report = reports.getJSONObject(it); report.optString("target") == target && report.optString("observation") == "Managed"
                    }
                    if (!already && mode == "Static") {
                        val wallpaperId = applyStatic(manager, flag, local)
                        didApply = true
                        saved.put(target, JSONObject().put("assetId", command.getString("assetId")).put("commandId", id)
                            .put("sequence", command.getLong("sequence")).put("mode", mode).put("manifest", local.absolutePath).put("wallpaperId", wallpaperId))
                        saveApplied(state, all)
                    } else if (!already && mode == "Live2D") {
                        if (!liveEnabled() || inventory.activeFlags() and flag == 0) {
                            if (pending == null || pending?.optString("id") == id)
                                pending = JSONObject(command.toString()).put("manifest", local.absolutePath).put("nativeId", screen)
                            if (notifiedLiveId != id) { notifiedLiveId = id; announce("동적 배경화면을 적용하려면 연결 앱에서 시스템 적용을 확인해 주세요.") }
                            if (command.optString("status") != "NeedsUserAction") api.report(id, "NeedsUserAction")
                            message = "연결 앱 설정에서 Live 2D를 켜고 Android 배경화면 선택을 확인해 주세요."
                            continue
                        }
                        selectLive(flag, local.absolutePath)
                        if (!CompanionLiveEngines.hasRendered(flag, local.absolutePath)) {
                            if (command.optString("status") != "NeedsUserAction") api.report(id, "NeedsUserAction")
                            message = "선택한 홈 또는 잠금 화면을 열면 동적 배경화면이 적용됩니다."
                            continue
                        }
                        didApply = true
                        saved.put(target, JSONObject().put("assetId", command.getString("assetId")).put("commandId", id)
                            .put("sequence", command.getLong("sequence")).put("mode", mode).put("manifest", local.absolutePath))
                        saveApplied(state, all)
                        if (pending?.optString("id") == id) pending = null
                    }
                    didApply = true; api.report(id, "Applied"); message = "배경화면 적용 완료. 웹의 기기 관리에서 확인할 수 있습니다."; announce("배경화면을 적용했습니다."); nextHeartbeat = 0
                } catch (error: Exception) {
                    if (epoch != generation.get() || !running) return
                    if (error is CompanionApiError && error.status == 401) throw error
                    if (error is InactiveWallpaperScreen || error is CompanionApiError && error.status in listOf(404, 409)) continue
                    if (error is java.io.IOException || error is CompanionApiError) connectionState = "disconnected"
                    if (!didApply && error !is java.io.IOException) runCatching { api.report(id, "Failed", error.message) }
                    message = if (didApply) "적용 결과 보고를 다시 시도합니다." else error.message ?: "배경화면 적용에 실패했습니다."
                }
            }
            if (commands.isEmpty() && pending == null) message = "연결됨 · 웹의 새 배경화면 요청을 기다리고 있습니다."
        } catch (error: Exception) {
            if (epoch != generation.get() || !running) return
            connectionState = "disconnected"
            context.getSharedPreferences("live_layer", Context.MODE_PRIVATE).edit().putLong("companion_valid_until", 0).apply()
            if (error is CompanionApiError && error.status == 401) {
                running = false; verified = false; api.cancelUpdates(); store.save(store.read().put("enabled", false)); message = "계정 또는 기기 연결이 만료되었습니다. 다시 로그인하거나 웹의 기기 관리에서 확인해 주세요."
            } else message = "연결 재시도 중 · ${error.message ?: "네트워크를 확인해 주세요."}"
        }
    }
}
