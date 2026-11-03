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

Current release: **1.3.1**, Android build **40**, `artifacts/Upscale_Lab-1.3.1-.apk`.

기존 로고를 앱·런처에 적용하고 모바일 연결 UI를 개선했다. 서버 입력란을 제거했으며 Kotlin foreground service도 빌드에 포함된 운영 HTTPS 서버 주소를 사용한다. 운영 APK는 기존 `com.livelayer.app` 패키지와 팀 공유 서명을 유지한다. 별도 패키지로 설치하는 로컬 테스트 절차는 [로컬 테스트 가이드](../companion/LOCAL-TESTING.md)를 따른다. 기기 이름·OS·화면 해상도·주사율은 유지하며 제조사·모델·CPU 구조는 수집하지 않는다.

이 APK는 팀 공유 릴리스 키로 서명하고 apksigner로 검증하며, 이전 운영 APK와 인증서를 대조한다. 다음 APK 생성 때도 [AGENTS.md](../AGENTS.md)의 버전 증가·문서 동기화·공유 keystore·apksigner 검증 규칙을 따른다. 릴리스 서명은 Git 밖 C:\UpscaleLab-Secrets\key.properties를 UPSCALE_SIGNING_PROPERTIES_FILE로 지정하고 debug key로 대체하지 않는다. 별도 로컬 테스트 패키지는 운영 앱 업그레이드용이 아니다.
