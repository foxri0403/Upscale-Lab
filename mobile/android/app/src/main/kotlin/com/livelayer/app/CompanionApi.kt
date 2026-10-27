package com.livelayer.app

import org.json.JSONObject
import org.json.JSONTokener
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL

internal class CompanionApiError(val status: Int, message: String) : Exception(message)

internal class CompanionApi(private val store: CompanionStore) {
    companion object {
        fun normalize(raw: String): String {
            val uri = URI(raw.trim().trimEnd('/'))
            require(uri.scheme in listOf("https", "http") && !uri.host.isNullOrBlank() && uri.userInfo == null &&
                uri.rawQuery == null && uri.rawFragment == null && uri.path in listOf("", "/")) { "올바른 서버 주소를 입력해 주세요." }
            return uri.toString().trimEnd('/')
        }
    }
    fun request(path: String, method: String = "GET", body: JSONObject? = null, agent: Boolean = false,
        anonymous: Boolean = false, retry: Boolean = true): Any {
        val state = store.read()
        val server = state.getString("server")
        val session = state.optJSONObject("session")
        if (agent) {
            val binding = state.optJSONObject("binding")
            require(session != null && binding != null && binding.optString("owner") == session.getJSONObject("user").getString("id") &&
                binding.optString("server") == server) { "같은 계정의 로그인과 기기 연결이 필요합니다." }
        }
        val connection = URL(server + path).openConnection() as HttpURLConnection
        try {
            connection.connectTimeout = 15000; connection.readTimeout = 20000; connection.requestMethod = method
            connection.instanceFollowRedirects = false
            if (!anonymous && session != null) connection.setRequestProperty("Authorization", "Bearer ${session.getString("accessToken")}")
            if (agent) connection.setRequestProperty("X-Device-Token", state.getJSONObject("binding").getString("token"))
            if (body != null) {
                connection.setRequestProperty("Content-Type", "application/json"); connection.doOutput = true
                connection.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
            }
            val status = connection.responseCode
            if (status == 401 && !anonymous && retry && session != null) {
                val renewed = request("/api/auth/refresh", "POST", JSONObject().put("refreshToken", session.getString("refreshToken")), anonymous = true, retry = false) as JSONObject
                require(renewed.getJSONObject("user").getString("id") == session.getJSONObject("user").getString("id")) { "로그인 계정이 일치하지 않습니다." }
                val latest = store.read().put("session", renewed); store.save(latest)
                return request(path, method, body, agent, anonymous, false)
            }
            if (status !in 200..299) {
                val message = runCatching { JSONObject(connection.errorStream?.bufferedReader()?.use { it.readText() } ?: "{}").optString("message") }.getOrDefault("")
                throw CompanionApiError(status, message.ifEmpty { if (status == 401) "계정 또는 기기 연결을 다시 확인해 주세요." else "서버 요청에 실패했습니다. ($status)" })
            }
            if (status == 204) return JSONObject()
            return JSONTokener(connection.inputStream.bufferedReader().use { it.readText() }).nextValue()
        } finally { connection.disconnect() }
    }
    fun report(id: String, status: String, error: String? = null) {
        request("/api/agent/commands/$id/report", "POST", JSONObject().put("status", status).put("error", error?.take(900)), agent = true)
    }
}
