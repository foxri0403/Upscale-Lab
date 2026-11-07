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

Current release: **1.7.0**, Android build **47**, `artifacts/Upscale_Lab-1.7.0-.apk` (운영 배포용). 배경 PNG 없는 Live 2D 캐릭터, 레이어 공통 자르기 비율·회전축과 저장된 마우스·자이로 입력 설정을 지원한다. 연결 상태는 ‘연결된 화면’ 제목 오른쪽에 녹색(연결됨)·주황색(연결중)·빨간색(연결안됨)으로 표시하며 하단 연결 안내는 표시하지 않는다.

로그인에 아이디 저장을 추가하고, 로그인 후 연결된 화면을 헤더 바로 아래에 표시한다. 프로필 사진을 누르면 이름·이메일·현재 플랜을 조회하는 작은 팝업을 연다. 설정은 알람, Live 2D 순서이며 로그아웃은 팝업 하단에 표시한다. 기기 요약·동기화 스위치·재연결·별도 Live 2D 승인 카드는 제거했다. Android 설치 화면은 OS에서 제공하므로 첫 실행 시 필수 수집·이용 동의를 받으며, 로그인 화면에는 로그인 기능만 표시한다. 동의와 계정 인증을 모두 확인한 뒤 화면 감지와 변경 이벤트 구독을 시작한다. 동의 버전·UTC 시각·대상 서버를 로컬에 저장하며 동의는 다른 서버로 이전하지 않는다. 로그아웃 후에는 화면 목록을 비우고 감지를 중지한다.

테스트 전용 임의 로그인과 테스트 앱 표시를 제거했다. 서버 입력란은 없으며 빌드에 포함된 운영 HTTPS 주소로 실제 계정을 인증하고 기기를 연동한다. 패키지는 `com.livelayer.app`이고 팀 공유 키로 서명한다. 기기 이름·OS·화면 해상도·주사율은 유지하며 제조사·모델·CPU 구조는 수집하지 않는다. 테스트 실행파일과 UI 검토용 임시 빌드는 정리했다.

이 APK는 팀 공유 릴리스 키로 서명하고 apksigner로 검증하며, 이전 운영 APK와 인증서를 대조한다. 다음 APK 생성 때도 [AGENTS.md](../AGENTS.md)의 버전 증가·문서 동기화·공유 keystore·apksigner 검증 규칙을 따른다. 릴리스 서명은 Git 밖 C:\UpscaleLab-Secrets\key.properties를 UPSCALE_SIGNING_PROPERTIES_FILE로 지정하고 debug key로 대체하지 않는다. 별도 로컬 테스트 패키지는 운영 앱 업그레이드용이 아니다.

1.6.0에서 배경화면 동기화는 로그인한 동안 항상 활성화한다. Live 2D 설정은 계정·서버별로 저장하며 기본은 꺼짐이다. 켜면 앱 내 사용 의사를 기록하고, 대기 중인 결과물이 있을 때 최초 Android 시스템 적용 화면을 연다. 시스템 확인을 취소하면 설정을 끈 상태로 되돌린다. 실제 엔진의 적용 확인 전에는 서버에 완료를 보고하지 않는다. 끄거나 로그아웃하면 움직임을 중지하고 정적인 화면은 유지한다. 일반 알람을 꺼도 Android의 필수 foreground service 실행 알림은 유지한다. 새 UI는 계정 정보를 조회만 하며 수정 요청을 보내지 않는다.
