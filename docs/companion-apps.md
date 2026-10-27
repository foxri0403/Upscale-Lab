# Windows / Android 연결 앱

웹의 **앱 다운로드**에서 Windows EXE와 Android APK를 받습니다. iOS는 **준비 중**으로만 표시합니다. 연결 앱에서 웹과 같은 계정으로 로그인하면 실제 화면 크기로 기기를 자동 등록하고 기기 자격을 발급받습니다. 웹의 **기기 관리** 또는 작업실에서 이미지와 화면을 선택하면 연결 앱이 내려받아 적용하고, OS에서 적용을 확인한 뒤 완료를 보고합니다.

## 사용 방법

1. 연결할 기기에 앱을 내려받아 실행합니다. Windows는 x64 Windows 10/11, Android는 7.0(API 24) 이상을 대상으로 합니다.
2. 서버 주소를 확인하고 웹과 같은 아이디/이메일 및 비밀번호로 로그인합니다. 비밀번호는 저장하지 않습니다.
3. 웹 기기 관리에 기기가 나타나면 화면별 배경화면을 선택합니다. Windows는 약 5초, Android는 약 15초 간격으로 요청을 확인하며 화면 정보는 약 30초마다 갱신합니다.
4. Android Live 2D의 첫 적용은 앱의 **Live 2D 적용 확인**을 눌러 시스템 승인 화면에서 적용합니다. 승인 전에는 웹에 **기기 확인 필요**로 표시됩니다. 시스템이 홈/잠금 화면을 함께 바꾸는 기종에서는 실제 변경 상태를 보고합니다.
5. 동기화를 끄거나 로그아웃하면 새 요청을 적용하지 않습니다. 적용된 정적 배경화면은 남고 Live 2D 움직임은 중지합니다. 로그아웃은 이 앱의 계정 세션을 폐기합니다.

Windows 창을 닫으면 트레이에서 계속 실행합니다. 트레이의 종료로 완전히 끌 수 있습니다. Windows 시작 시 실행은 사용자가 선택합니다. Android는 동기화 중 알림을 표시하며 알림의 중지 또는 앱의 스위치로 끌 수 있습니다. 자동 로그인을 켠 기기는 재부팅 후 연결을 복원합니다. Android 제조사의 백그라운드 제한이나 네트워크 상태에 따라 갱신이 지연될 수 있습니다.

## 수집 범위와 인증

수집은 OS 디스플레이 API로 수행합니다. 연결된 물리 화면 개수, 픽셀 해상도, 소수점 주사율, 배율, 회전, 위치, 주 화면, 연결 상태와 제조사·모델·OS 버전·CPU 구조·연결 앱 버전을 보고합니다. Android의 홈/잠금은 한 물리 화면의 적용 대상이며 화면 개수에 중복 계산하지 않습니다. 화면을 꺼도 연결된 물리 화면으로 유지합니다. Android 외부 디스플레이는 공개 WallpaperManager API로 개별 적용할 수 없어 **정보 조회 전용**으로 보고하고 웹/API에서 적용을 차단합니다.

이 앱이 적용한 배경화면은 결과물 ID와 OS의 적용 식별자를 대조합니다. 다른 프로그램/사용자가 바꾼 배경화면은 External/Unavailable로 보고합니다. 외부 배경화면의 원본 사진, 로컬 경로, 다른 앱 목록, MAC 주소와 일련번호는 수집·전송하지 않습니다. Windows 모니터의 로컬 장치 경로는 안정적인 해시로 바꾸어 전송합니다.

모든 agent 요청은 유효한 계정 JWT와 해당 계정에 속한 기기 토큰을 함께 사용합니다. Windows는 현재 사용자 DPAPI, Android는 Android Keystore AES/GCM으로 기기 자격과 자동 로그인 세션을 보호합니다. 자동 로그인을 끄면 계정 세션은 메모리에만 유지합니다. 계정·서버별로 캐시와 적용 이력을 분리합니다. 웹에서 폐기한 기기 자격을 자동 재발급하지 않습니다. 사용자가 **기기 다시 연결**을 선택해야 재발급합니다.

서명된 이미지 다운로드 URL에는 계정 JWT와 기기 키를 보내지 않습니다. 다운로드 캐시에는 만료 URL 대신 로컬 레이어 파일과 변환 정보를 저장합니다. 다운로드 후에도 최신 화면·요청 순서·결과물 ID를 다시 확인하고 대체된 명령은 적용하지 않습니다. 네트워크가 끊기면 재시도하며 실제 적용 후 보고 실패를 적용 실패로 바꾸지 않습니다.

## 구현 및 제한

- `companion/windows`: .NET 8 WinForms, 단일 EXE에 .NET 런타임 포함. 정적 배경은 IDesktopWallpaper, Live 2D는 Explorer WorkerW 아래 별도 렌더 창과 마우스 시차를 사용합니다. Explorer 구성에 따라 Live 2D 호스트를 찾지 못하면 지원하지 않는 것으로 보고합니다. Windows 잠금 화면은 지원하지 않습니다.
- `mobile/lib/companion.dart`: Flutter 연결 전용 화면. Kotlin foreground service가 네트워크·기기 자격·WallpaperManager를 처리하므로 Flutter 화면을 닫아도 동기화합니다. 기존 Kotlin Live 2D 엔진과 자이로 센서를 재사용하며 계정 인증의 유효 시간이 지나면 움직임을 중지합니다.
- 이전 모바일 편집 화면과 C#/Flutter/Kotlin 자산은 재사용을 위해 유지하며 기본 앱 진입점은 연결 화면으로 변경했습니다. 이미지 업스케일링은 기존 팀원 소유 클라우드 처리 규격을 그대로 사용합니다.
- Windows EXE는 현재 Authenticode 코드 서명 인증서가 없어 서명되지 않았습니다. Android APK는 팀 공유 릴리스 키로 서명합니다. 다른 인증서로 배포한 예전 APK와의 덮어쓰기 설치는 별도 인증서 확인이 필요합니다.
- Windows 실제 화면 정보 수집은 확인했습니다. 실제 기기의 계정 로그인 → 웹 적용 → OS 배경화면 변경과 Android 제조사별 홈/잠금 동작은 설치 후 통합 검증이 필요합니다. 현재 개발 환경에는 연결된 Android 기기/에뮬레이터가 없습니다.

## 빌드와 다운로드 배포

APK 버전/빌드 번호는 `AGENTS.md`, `mobile/pubspec.yaml`, `mobile/README.md`를 함께 갱신합니다. 공유 서명 설정은 Git 밖 파일을 지정하고 `apksigner verify --print-certs`로 반드시 검증합니다. 최신 APK는 **1.0.1+31**, Windows 앱은 **1.0.1**입니다.

```powershell
$env:UPSCALE_SIGNING_PROPERTIES_FILE = 'C:\UpscaleLab-Secrets\key.properties'
.\companion\Build-Release.ps1 -ApiBaseUrl 'https://YOUR_SERVER' -Flutter 'C:\dev\flutter\bin\flutter.bat'
```

현재 HTTP 테스트 서버로 빌드할 때만 `companionAllowHttp=true`를 Gradle에 전달합니다. 기본 릴리스 설정은 HTTP를 허용하지 않습니다. 서버 주소는 빌드 인수로 공급하며 코드에 환경별 값을 추가하지 않습니다.

스크립트는 서명을 검증한 APK와 Windows EXE를 무시되는 `web/public/downloads/`에 복사하고 버전·SHA-256·바이트 크기·동일 출처 다운로드 URL을 `manifest.json`에 기록합니다. 그 후 `web`에서 `pnpm run build:cloud`를 실행하고 정적 파일 전체를 수동 배포합니다. 바이너리/서명키는 Git에 커밋하지 않습니다. `/downloads/`는 SPA fallback 없이 파일 부재 시 404를 반환해야 하며 manifest는 캐시하지 않습니다.

## 백엔드와 DB

`20261027034626_AddCompanionInventory`는 devices에 Manufacturer/Model/Architecture, device_displays에 RefreshRateHz/CanApplyWallpaper를 추가합니다. 주사율 미보고는 null이며 기존 화면의 적용 가능 기본값은 true입니다. heartbeat 선택 필드를 생략하는 기존 클라이언트와 호환됩니다. 잘못된 주사율·기기 정보는 상태 변경 전에 검증합니다. 정보 조회 전용 화면은 적용 요청을 거부합니다.

`backend/tools/UpscaleLab.DatabaseTool`은 운영자가 명시적으로 실행하는 유지보수 도구입니다. `status`로 대상 DB/소유자와 미적용 migration을 확인하고, `apply-companion /absolute/backup.dump`는 새 migration 하나만 남아 있을 때 `pg_dump` 전체 백업을 완료한 뒤 적용합니다. DB 자격은 서비스와 같은 보안 환경 공급자로만 받으며 명령행 인수나 로그에 출력하지 않습니다. API 시작 시 자동 migration이나 자동 배포를 추가하지 않습니다.

관련 규격: [agent API](agent-api.md). 플랫폼 문서: [Windows IDesktopWallpaper](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper), [Android Display.Mode](https://developer.android.com/reference/android/view/Display.Mode), [Android specialUse foreground service](https://developer.android.com/develop/background-work/services/fgs/service-types#special-use).

검증: 백엔드 95건, Windows 계정·기기 자격 테스트 7건, Android 앱 자체 테스트 61건, 웹 및 클라우드 HTTP 점검 38건을 통과했습니다. Windows 실제 모니터에서 1920×1080/60 Hz 정보를 읽었으며 다운로드 파일은 서명 검증한 APK/실행 가능한 EXE 헤더·바이트 크기·SHA-256을 대조합니다. 계정 로그인 후 실제 배경화면 변경은 실기기 통합 검증이 남아 있습니다. Android 전체 하위 프로젝트 테스트에서는 외부 image_picker 플러그인 10건이 테스트 리소스 누락으로 실패했으며, `:app:testDebugUnitTest`로 앱 자체 범위를 분리해 통과를 확인했습니다.
