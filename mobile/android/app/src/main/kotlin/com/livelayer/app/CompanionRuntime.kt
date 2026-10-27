package com.livelayer.app

import android.app.WallpaperManager
import android.content.Context
import android.os.Build
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicLong

/** All API and image work is serialized. Invalidation is immediate, including during downloads. */
internal object CompanionRuntime {
    private lateinit var context: Context
    private lateinit var store: CompanionStore
    private lateinit var api: CompanionApi
    private val worker = Executors.newSingleThreadScheduledExecutor()
    private val generation = AtomicLong()
    @Volatile private var initialized = false
    @Volatile private var running = false
    @Volatile private var verified = false
    @Volatile private var message = "웹과 같은 계정으로 로그인해 주세요."
    @Volatile private var pending: JSONObject? = null
    private var nextHeartbeat = 0L

    @Synchronized fun initialize(value: Context) {
        if (initialized) return
        context = value.applicationContext; store = CompanionStore(context); api = CompanionApi(store); initialized = true
        worker.scheduleWithFixedDelay({ if (running) poll() }, 0, 15, TimeUnit.SECONDS)
    }
    fun invalidate() {
        generation.incrementAndGet(); running = false; pending = null
        context.getSharedPreferences("live_layer", Context.MODE_PRIVATE).edit().putLong("companion_valid_until", 0).apply()
    }
    fun resume(): Boolean {
        val state = store.read()
        if (!state.optBoolean("enabled") || !state.has("session")) return false
        running = true; pollSoon(); return true
    }
    fun pollSoon() { if (initialized && running) worker.execute { poll() } }
    fun status(): Map<String, Any?> {
        val state = store.read(); val user = state.optJSONObject("session")?.optJSONObject("user")
        val displays = runCatching { CompanionInventory(context).read(applied(state)) }.getOrDefault(JSONArray())
        return mapOf("server" to state.optString("server"), "account" to user?.optString("email"),
            "loggedIn" to (verified && user != null), "enabled" to running, "message" to message,
            "pendingLive" to (pending != null), "manufacturer" to Build.MANUFACTURER, "model" to Build.MODEL,
            "osVersion" to "Android ${Build.VERSION.RELEASE}", "displays" to (0 until displays.length()).map {
                val item = displays.getJSONObject(it)
                mapOf("name" to item.optString("name"), "width" to item.optInt("width"), "height" to item.optInt("height"),
                    "refreshRateHz" to item.optDouble("refreshRateHz").takeIf { hz -> hz.isFinite() }, "canApply" to item.optBoolean("canApplyWallpaper"))
            })
    }
    fun action(name: String, arguments: Map<*, *>, done: (Any?, String?) -> Unit) {
        if (name in listOf("login", "logout", "disable", "reconnect")) invalidate()
        worker.execute {
            try {
                when (name) {
                    "login" -> {
                        val server = CompanionApi.normalize(arguments["server"] as String)
                        val previous = store.read(); verified = false
                        store.save(JSONObject().put("server", server).put("remember", arguments["remember"] == true))
                        val session = api.request("/api/auth/login", "POST", JSONObject().put("email", (arguments["identifier"] as String).trim())
                            .put("password", arguments["password"] as String), anonymous = true) as JSONObject
                        require(session.optString("refreshToken").isNotBlank()) { "계정 세션을 지원하는 서버가 필요합니다." }
                        val state = store.read().put("session", session).put("enabled", true)
                        val old = previous.optJSONObject("binding")
                        if (old != null && old.optString("server") == server && old.optString("owner") == session.getJSONObject("user").getString("id")) state.put("binding", old)
                        store.save(state); verified = true; bind(false); message = "기기 연결 완료. 웹에서 배경화면을 선택해 주세요."
                    }
                    "enable" -> { require(store.read().has("session")) { "로그인이 필요합니다." }; verifyAccount(); bind(false); store.save(store.read().put("enabled", true)) }
                    "disable" -> { store.save(store.read().put("enabled", false)); message = "동기화가 꺼져 있습니다." }
                    "logout" -> {
                        val refresh = store.read().optJSONObject("session")?.optString("refreshToken")
                        store.save(store.read().apply { remove("session"); put("enabled", false) }); verified = false
                        if (!refresh.isNullOrBlank()) runCatching { api.request("/api/auth/logout", "POST", JSONObject().put("refreshToken", refresh), anonymous = true) }
                        message = "로그아웃했습니다. 정적 배경화면은 기기에 남습니다."
                    }
                    "reconnect" -> { verifyAccount(); bind(true); store.save(store.read().put("enabled", true)) }
                    "activateLive" -> {
                        val item = pending ?: error("대기 중인 Live 2D 요청이 없습니다.")
                        requireLatest(item)
                        val path = item.getString("manifest")
                        lease()
                        context.getSharedPreferences("live_layer", Context.MODE_PRIVATE).edit().putString("preview_manifest_path", path).commit()
                        done(path, null); return@execute
                    }
                    else -> error("지원하지 않는 동작입니다.")
                }
                done(status(), null)
            } catch (error: Exception) { message = error.message ?: "연결에 실패했습니다."; done(null, message) }
        }
    }
    private fun verifyAccount() {
        val user = api.request("/api/auth/me") as JSONObject
        require(user.getString("id") == owner(store.read())) { "로그인 계정이 일치하지 않습니다." }; verified = true
    }
    private fun owner(state: JSONObject) = state.getJSONObject("session").getJSONObject("user").getString("id")
    private fun bind(force: Boolean) {
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
        val device = api.request("/api/devices", "POST", JSONObject().put("platform", "Android").put("deviceName", "${Build.MANUFACTURER} ${Build.MODEL}".take(120))
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
        context.getSharedPreferences("live_layer", Context.MODE_PRIVATE).edit().putString("companion_owner", owner(store.read()))
            .putLong("companion_valid_until", System.currentTimeMillis() + 60000).apply()
    }
    private fun requireLatest(command: JSONObject): JSONObject {
        val state = api.request("/api/agent/state", agent = true) as JSONObject
        val displays = state.getJSONArray("displays")
        val display = (0 until displays.length()).map { displays.getJSONObject(it) }.firstOrNull {
            it.getString("id") == command.getString("displayId") && it.getBoolean("isConnected") && it.optBoolean("canApplyWallpaper", true)
        } ?: error("적용 대상 화면이 연결되어 있지 않습니다.")
        require(display.getString("nativeId") == "android-built-in") { "외부 화면은 정보 조회만 지원합니다." }
        val assignments = display.getJSONArray("wallpapers")
        require((0 until assignments.length()).any {
            val item = assignments.getJSONObject(it)
            item.getString("target") == command.getString("target") && item.optLong("sequence") == command.getLong("sequence") &&
                item.optString("desiredAssetId") == command.getString("assetId")
        }) { "최신 요청으로 변경되었습니다." }
        return display
    }
    private fun poll() {
        val epoch = generation.get()
        try {
            if (!running) return
            if (!verified) verifyAccount()
            val state = store.read(); val saved = applied(state)
            val inventory = CompanionInventory(context)
            val remote = api.request("/api/agent/state", agent = true) as JSONObject
            if (System.currentTimeMillis() >= nextHeartbeat) {
                val body = JSONObject().put("revision", remote.getLong("inventoryRevision") + 1).put("osVersion", "Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})")
                    .put("agentVersion", "1.0.0").put("manufacturer", Build.MANUFACTURER.take(120)).put("model", Build.MODEL.take(120))
                    .put("architecture", Build.SUPPORTED_ABIS.firstOrNull()?.take(32) ?: "").put("supportsLive2D", context.packageManager.hasSystemFeature("android.software.live_wallpaper"))
                    .put("supportsLockScreen", true).put("displays", inventory.read(saved))
                try { api.request("/api/agent/heartbeat", "POST", body, agent = true); nextHeartbeat = System.currentTimeMillis() + 30000 }
                catch (error: CompanionApiError) { if (error.status != 409) throw error }
            }
            if (epoch != generation.get() || !running) return
            lease()
            val commands = api.request("/api/agent/commands", agent = true) as JSONArray
            if (pending != null && !(0 until commands.length()).any { commands.getJSONObject(it).getString("id") == pending!!.getString("id") }) pending = null
            for (i in 0 until commands.length()) {
                if (epoch != generation.get() || !running) return
                val command = commands.getJSONObject(i); val id = command.getString("id"); var didApply = false
                try {
                    val target = command.getString("target")
                    require(target in listOf("Home", "Lock")) { "지원하지 않는 배경화면 대상입니다." }
                    api.report(id, "Downloading")
                    val manifest = api.request("/api/agent/commands/$id/manifest", agent = true) as JSONObject
                    require(manifest.getString("assetId") == command.getString("assetId")) { "결과물 정보가 일치하지 않습니다." }
                    val local = CompanionCache(context).download(owner(state), state.getString("server"), manifest)
                    requireLatest(command)
                    if (epoch != generation.get() || !running) return
                    val flag = if (target == "Home") WallpaperManager.FLAG_SYSTEM else WallpaperManager.FLAG_LOCK
                    val manager = WallpaperManager.getInstance(context); val mode = manifest.getString("mode")
                    val old = saved.optJSONObject(target)
                    val screens = inventory.read(saved)
                    val reports = (0 until screens.length()).map { screens.getJSONObject(it) }.firstOrNull { it.optBoolean("isPrimary") }?.optJSONArray("wallpapers") ?: JSONArray()
                    val already = old?.optString("commandId") == id && (0 until reports.length()).any {
                        val report = reports.getJSONObject(it); report.optString("target") == target && report.optString("observation") == "Managed"
                    }
                    if (!already && mode == "Static") {
                        val file = File(local.parentFile, JSONObject(local.readText()).getJSONArray("layers").getJSONObject(0).getString("fileName"))
                        val wallpaperId = file.inputStream().use { manager.setStream(it, null, true, flag) }
                        check(wallpaperId > 0 && manager.getWallpaperId(flag) == wallpaperId) { "Android 배경화면 적용을 확인하지 못했습니다." }
                        didApply = true
                        saved.put(target, JSONObject().put("assetId", command.getString("assetId")).put("commandId", id).put("mode", mode).put("manifest", local.absolutePath).put("wallpaperId", wallpaperId))
                        saveApplied(state, saved)
                    } else if (!already && mode == "Live2D") {
                        pending = JSONObject(command.toString()).put("manifest", local.absolutePath)
                        if (inventory.activeFlags() and flag == 0) {
                            api.report(id, "NeedsUserAction"); message = "앱에서 Live 2D 적용 확인을 눌러 Android 화면에서 승인해 주세요."; continue
                        }
                        val prefs = context.getSharedPreferences("live_layer", Context.MODE_PRIVATE)
                        if (prefs.getString("direct_manifest_path", null) != local.absolutePath)
                            check(prefs.edit().putString("manifest_path", local.absolutePath).putString("direct_manifest_path", local.absolutePath).commit())
                        if (prefs.getString("companion_rendered_manifest", null) != local.absolutePath) {
                            api.report(id, "NeedsUserAction"); message = "Live 2D 화면이 표시되기를 기다리고 있습니다."; continue
                        }
                        didApply = true
                        saved.put(target, JSONObject().put("assetId", command.getString("assetId")).put("commandId", id).put("mode", mode).put("manifest", local.absolutePath))
                        saveApplied(state, saved); pending = null
                    }
                    didApply = true; api.report(id, "Applied"); message = "배경화면 적용 완료. 웹의 기기 관리에서 확인할 수 있습니다."; nextHeartbeat = 0
                } catch (error: Exception) {
                    if (epoch != generation.get() || !running) return
                    if (error is CompanionApiError && error.status == 401) throw error
                    if (error is CompanionApiError && error.status in listOf(404, 409)) continue
                    if (!didApply) runCatching { api.report(id, "Failed", error.message) }
                    message = if (didApply) "적용 결과 보고를 다시 시도합니다." else error.message ?: "배경화면 적용에 실패했습니다."
                }
            }
            if (commands.length() == 0) message = "연결됨 · 웹의 새 배경화면 요청을 기다리고 있습니다."
        } catch (error: Exception) {
            if (epoch != generation.get() || !running) return
            context.getSharedPreferences("live_layer", Context.MODE_PRIVATE).edit().putLong("companion_valid_until", 0).apply()
            if (error is CompanionApiError && error.status == 401) {
                running = false; store.save(store.read().put("enabled", false)); message = "계정 또는 기기 연결이 만료되었습니다. 로그인 또는 기기 다시 연결이 필요합니다."
            } else message = "연결 재시도 중 · ${error.message ?: "네트워크를 확인해 주세요."}"
        }
    }
}
