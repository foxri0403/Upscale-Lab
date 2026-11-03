package com.livelayer.app

/** In-memory UI preview. Never creates API credentials or applies wallpapers. */
internal class CompanionTestSession(private val testBuild: Boolean) {
    var account: String? = null; private set
    var enabled = false; private set
    var message = "테스트 앱 · 아무 로그인 정보나 입력해 시작하세요."; private set
    val loggedIn get() = testBuild && account != null
    fun action(name: String, identifier: String = "") {
        check(testBuild) { "일반 앱에서는 테스트 로그인을 사용할 수 없습니다." }
        when (name) {
            "login" -> { account = identifier.trim().ifEmpty { "테스트 사용자" }; enabled = true; message = "테스트 앱 · 서버 연결 없이 화면 정보를 확인합니다." }
            "logout" -> { account = null; enabled = false; message = "테스트 앱 · 로그아웃했습니다." }
            "enable", "disable", "reconnect", "activateLive" -> {
                check(loggedIn) { "먼저 테스트 로그인을 해 주세요." }
                if (name == "enable") enabled = true
                if (name == "disable") enabled = false
                message = "테스트 앱 · 동기화 미리보기 ${if (enabled) "켜짐" else "꺼짐"}. 배경화면은 변경하지 않습니다."
            }
            else -> error("지원하지 않는 테스트 동작입니다.")
        }
    }
}
