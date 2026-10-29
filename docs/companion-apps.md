# Windows / Android 연결 앱

웹의 **앱 다운로드**에서 Windows EXE와 Android APK를 받습니다. iOS는 **준비 중**으로만 표시합니다. 연결 앱에서 웹과 같은 계정으로 로그인하면 실제 화면 크기로 기기를 자동 등록하고 기기 자격을 발급받습니다. 웹의 **기기 관리** 또는 작업실에서 이미지와 화면을 선택하면 연결 앱이 내려받아 적용하고, OS에서 적용을 확인한 뒤 완료를 보고합니다.

## 사용 방법

1. 연결할 기기에 앱을 내려받아 실행합니다. Windows는 x64 Windows 10/11, Android는 7.0(API 24) 이상을 대상으로 합니다.
2. 서버 주소를 확인하고 웹과 같은 아이디/이메일 및 비밀번호로 로그인합니다. 비밀번호는 저장하지 않습니다.
3. 웹 기기 관리에 기기가 나타나면 화면별 배경화면을 선택합니다. 웹과 연결 앱은 인증된 변경 알림을 대기하다가 요청·처리 결과를 즉시 조회합니다. 20초 대기 응답은 변경이 없을 때의 연결 유지 시간입니다. Windows/Android 화면 연결·해상도 변경도 OS 이벤트로 즉시 보고하고, 30초 주기 보고는 보조로 유지합니다. 이미지 다운로드·OS 처리와 Android 최초 승인에 필요한 시간은 별도로 걸립니다. 이 동작은 **1.0.2 연결 앱으로 업데이트**해야 사용할 수 있습니다.
4. Android Live 2D의 첫 적용은 앱의 **Live 2D 적용 확인**을 눌러 시스템 승인 화면에서 적용합니다. 승인 전에는 웹에 **기기 확인 필요**로 표시됩니다. 시스템이 홈/잠금 화면을 함께 바꾸는 기종에서는 실제 변경 상태를 보고합니다.
5. 동기화를 끄거나 로그아웃하면 새 요청을 적용하지 않습니다. 적용된 정적 배경화면은 남고 Live 2D 움직임은 중지합니다. 로그아웃은 이 앱의 계정 세션을 폐기합니다.

Windows 창을 닫으면 트레이에서 계속 실행합니다. 트레이의 종료로 완전히 끌 수 있습니다. Windows 시작 시 실행은 사용자가 선택합니다. Android는 동기화 중 알림을 표시하며 알림의 중지 또는 앱의 스위치로 끌 수 있습니다. 자동 로그인을 켠 기기는 재부팅 후 연결을 복원합니다. Android 제조사의 백그라운드 제한이나 네트워크 상태에 따라 갱신이 지연될 수 있습니다.

## 수집 범위와 인증

수집은 OS 디스플레이 API로 수행합니다. 기기 이름·모니터 이름, 연결된 물리 화면 개수, 픽셀 해상도, 소수점 주사율, 배율, 회전, 위치, 주 화면, 연결 상태와 OS 버전·연결 앱 버전을 보고합니다. 제조사·모델·CPU 구조는 수집하거나 표시하지 않습니다. Android의 홈/잠금은 한 물리 화면의 적용 대상이며 화면 개수에 중복 계산하지 않습니다. 화면을 꺼도 연결된 물리 화면으로 유지합니다. Android 외부 디스플레이는 공개 WallpaperManager API로 개별 적용할 수 없어 **정보 조회 전용**으로 보고하고 웹/API에서 적용을 차단합니다.

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

APK 버전/빌드 번호는 `AGENTS.md`, `mobile/pubspec.yaml`, `mobile/README.md`를 함께 갱신합니다. 공유 서명 설정은 Git 밖 파일을 지정하고 `apksigner verify --print-certs`로 반드시 검증합니다. 최신 APK는 **1.2.2+36**, Windows 앱은 **1.2.2**입니다. 1.2.2는 제조사·모델·CPU 구조 수집을 제거하며 기기 이름·OS·화면 해상도·주사율은 유지합니다. 신규 Android 기기는 OS에 설정된 이름으로 등록하며 이름을 읽을 수 없을 때만 일반 이름을 사용합니다.

1.2.1은 Windows 11의 raised desktop에서 동적 창을 정적 WorkerW 내부 대신 아이콘 아래·시스템 배경 위의 불투명 layered child로 연결합니다. 테마 변경 후 순서를 재확인하며 마우스의 가로 움직임은 웹과 같은 방향으로 적용합니다. 트레이에서 이전 앱을 종료하고 새 EXE를 실행한 뒤 다시 적용합니다. Android는 gyroEnabled 설정에 따라 센서를 구독하며, 회전 벡터가 없는 기기는 원시 자이로를 사용합니다. 캐시된 레이어도 최신 입력 설정을 반영하고 실행 엔진에 갱신을 알립니다. Android APK 인증서는 이전 정식 릴리스와 동일합니다. 실기기별 OS·센서 확인은 설치 후 필요합니다.

1.2.0은 웹의 **배경화면 삭제** 명령을 지원합니다. Windows는 선택 모니터의 OS 배경화면을 해제하고 경로가 비워진 것을 확인한 뒤 해당 Live 창을 종료합니다. Android는 선택 Home/Lock의 WallpaperManager 초기화 API를 호출하고 완료 영수증을 저장합니다. 다른 화면과 내 이미지 라이브러리는 유지합니다. 서버는 기기의 Applied 보고 후에만 삭제 완료를 표시합니다. 기존 앱은 업데이트하고 연결 상태를 새로 보고해야 삭제 버튼이 활성화됩니다. Android APK 인증서는 1.1.0과 동일함을 확인했습니다. 실제 기기에서 OS별 삭제 동작은 설치 후 확인해야 합니다.

```powershell
$env:UPSCALE_SIGNING_PROPERTIES_FILE = 'C:\UpscaleLab-Secrets\key.properties'
.\companion\Build-Release.ps1 -ApiBaseUrl 'https://YOUR_SERVER' -Flutter 'C:\dev\flutter\bin\flutter.bat'
```

현재 HTTP 테스트 서버로 빌드할 때만 `companionAllowHttp=true`를 Gradle에 전달합니다. 기본 릴리스 설정은 HTTP를 허용하지 않습니다. 서버 주소는 빌드 인수로 공급하며 코드에 환경별 값을 추가하지 않습니다.

스크립트는 서명을 검증한 APK와 Windows EXE를 무시되는 `web/public/downloads/`에 복사하고 버전·SHA-256·바이트 크기·동일 출처 다운로드 URL을 `manifest.json`에 기록합니다. 그 후 `web`에서 `pnpm run build:cloud`를 실행하고 정적 파일 전체를 수동 배포합니다. 바이너리/서명키는 Git에 커밋하지 않습니다. `/downloads/`는 SPA fallback 없이 파일 부재 시 404를 반환해야 하며 manifest는 캐시하지 않습니다.

## 백엔드와 DB

`20261027034626_AddCompanionInventory`가 추가했던 devices의 Manufacturer/Model/Architecture 열은 스키마 호환을 위해 남아 있지만 신규 값을 저장하지 않습니다. 기존 값은 다음 유효 heartbeat에서 비우며 구형 앱의 해당 필드는 무시합니다. device_displays의 RefreshRateHz/CanApplyWallpaper는 유지합니다. 주사율 미보고는 null이며 기존 화면의 적용 가능 기본값은 true입니다. 잘못된 주사율·기기 정보는 상태 변경 전에 검증합니다. 정보 조회 전용 화면은 적용 요청을 거부합니다.

기기 관리의 최근 적용 내역, 웹 기록 저장·조회 코드와 계정용 내역 조회 API는 제거했습니다. 기기 명령 저장은 전달·재시도·중복 요청 방지·현재 적용 결과물 복구에 사용합니다. 웹은 기기 state의 실제 배경화면·대기 상태만 조회합니다.

`backend/tools/UpscaleLab.DatabaseTool`은 운영자가 명시적으로 실행하는 유지보수 도구입니다. `status`로 대상 DB/소유자와 미적용 migration을 확인하고, `apply-companion /absolute/backup.dump`는 새 migration 하나만 남아 있을 때 `pg_dump` 전체 백업을 완료한 뒤 적용합니다. DB 자격은 서비스와 같은 보안 환경 공급자로만 받으며 명령행 인수나 로그에 출력하지 않습니다. API 시작 시 자동 migration이나 자동 배포를 추가하지 않습니다.

관련 규격: [agent API](agent-api.md). 플랫폼 문서: [Windows IDesktopWallpaper](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper), [Android Display.Mode](https://developer.android.com/reference/android/view/Display.Mode), [Android specialUse foreground service](https://developer.android.com/develop/background-work/services/fgs/service-types#special-use).

현재 검증: 백엔드 190건, Windows 15건, Android 앱 자체 66건(`:app:testReleaseUnitTest`), 웹 및 운영 HTTPS 점검 54건을 통과했습니다. 삭제 요청·대기·완료 보고와 다른 화면 유지도 가상 기기로 확인했습니다. Android 전체 하위 프로젝트의 외부 image_picker 테스트 10건은 기존 리소스 누락으로 실패하며 앱 자체 범위는 통과했습니다. 실제 기기의 OS 배경화면 삭제는 새 앱 설치 후 통합 확인이 필요합니다. 배포 경로, 파일 해시와 검증 범위는 [기기 관리 배포 기록](device-management-deployment-20261028.md)에 정리했습니다.
