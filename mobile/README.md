# Upscale Lab Android 연결 앱

현재 앱은 웹과 같은 계정으로 로그인해 기기를 등록하고 화면 정보를 보고하며 웹에서 지정한 배경화면을 적용한다. Flutter 진입점은 lib/main.dart → runCompanion()이고 네트워크·기기 자격·OS 적용은 Kotlin CompanionRuntime과 foreground service가 담당한다.

이미지 업로드·편집·공유·보관함은 웹에서 사용한다. 실행되지 않던 옛 독립 모바일 화면, 해당 화면 전용 Dart 서비스, 로컬 갤러리 이전/편집 채널은 정리했다. 기존 사용자의 기기 저장 데이터는 삭제하지 않는다. 옛 로컬 보관함을 열거나 가져오는 UI는 현재 연결 앱에서 제공하지 않는다. LiveLayerWallpaperService·센서·계정별 companion 캐시는 유지한다.

설치·수집 범위·계정과 기기 이중 인증·OS 제한은 [연결 앱 가이드](../docs/companion-apps.md), 서버 규격은 [기기 API](../docs/agent-api.md)를 따른다.

## 개발과 검증

Flutter SDK와 Android SDK, Java 17 이상이 필요하다. SDK 경로는 로컬 android/local.properties에 지정하며 Git에 저장하지 않는다.

```powershell
cd mobile
flutter pub get
flutter analyze --no-pub
flutter test --no-pub
cd android
.\gradlew.bat :app:compileDebugKotlin :app:testDebugUnitTest --no-daemon
```

연결 앱 화면 테스트는 상태 조회, 같은 계정 로그인, 동기화 중지, Live 2D 승인 요청과 로그아웃을 검증한다. Kotlin 테스트는 배경화면 삭제, 시차 렌더링, 센서·프레임 수명과 메모리 예산을 검증한다. 실기기에서는 로그인 → 웹 적용 → OS 변경 확인, 첫 Live 2D 승인, 동기화 중지, 재부팅, 계정 전환을 확인한다.

## 현재 배포 파일과 서명

Current release: **1.5.0**, Android build **43**, `artifacts/companion-local/Upscale_Lab-1.5.0-local.apk` (로컬 테스트용).

시안의 로고·연보라 기기 카드·동기화 스위치·화면 정보·Live 2D 승인 카드로 UI를 구성했다. Android 설치 화면은 OS에서 제공하므로 첫 실행 시 필수 수집·이용 동의를 받으며, 로그인 화면에는 로그인 기능만 표시한다. 동의와 계정 인증을 모두 확인한 뒤 화면 감지와 변경 이벤트 구독을 시작한다. 동의 버전·UTC 시각·대상 서버를 로컬에 저장하며 동의는 다른 서버로 이전하지 않는다. 로그아웃 후에는 화면 목록을 비우고 감지를 중지한다.

로컬 테스트 APK는 서버 없이 임의의 아이디·비밀번호나 빈칸으로 로그인하는 UI 미리보기를 제공한다. 런처와 모든 화면 상단에 `테스트 앱`을 표시한다. 동의와 테스트 로그인 후에만 실제 화면 정보를 로컬에서 읽으며, 로그인 정보 저장·API 요청·기기 등록·실제 배경화면 변경은 하지 않는다. 동기화 스위치는 미리보기 상태만 변경하고 자동 로그인은 비활성화한다. 일반 빌드의 서버 인증은 유지한다.

패키지는 `com.livelayer.app.localtest`이고 팀 공유 키로 서명한다. [로컬 테스트 가이드](../companion/LOCAL-TESTING.md)에 실행 절차가 있다. 기기 이름·OS·화면 해상도·주사율은 유지하며 제조사·모델·CPU 구조는 수집하지 않는다.

이 APK는 팀 공유 릴리스 키로 서명하고 apksigner로 검증하며, 이전 운영 APK와 인증서를 대조한다. 다음 APK 생성 때도 [AGENTS.md](../AGENTS.md)의 버전 증가·문서 동기화·공유 keystore·apksigner 검증 규칙을 따른다. 릴리스 서명은 Git 밖 C:\UpscaleLab-Secrets\key.properties를 UPSCALE_SIGNING_PROPERTIES_FILE로 지정하고 debug key로 대체하지 않는다. 별도 로컬 테스트 패키지는 운영 앱 업그레이드용이 아니다.
