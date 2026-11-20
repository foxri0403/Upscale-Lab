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

## 릴리스 버전과 서명

준비 중인 릴리스는 **1.8.4+51**이며 예정 파일명은 `Upscale_Lab-1.8.4-.apk`이다. 신규 APK 생성 전의 공개 배포 버전은 **1.8.2+50**이다.

배경 분리와 Live 2D는 Android 시스템 승인 후 대상 화면에 렌더링된 것을 확인해 적용 완료를 보고한다. 홈과 잠금 화면이 엔진을 공유할 때 잠금 상태에 맞춰 각 화면에 지정한 이미지를 표시한다. Android 적용 화면에서 홈만 선택한 경우 연결 앱에서 잠금화면 적용을 다시 열 수 있다. 폴더블에서 외부 화면과 내부 화면을 각각 한 번 열면 두 화면의 해상도가 등록된다. 화면마다 홈·잠금 배경을 선택할 수 있으며, 비활성 화면에 보낸 요청은 24시간 이내 해당 화면을 열었을 때 적용된다. 동기화 중에는 화면을 바꿀 때 해당 화면의 저장된 배경을 복원한다.

로그인에 아이디 저장을 추가하고, 로그인 후 연결된 화면을 헤더 바로 아래에 표시한다. 프로필 사진을 누르면 이름·이메일·현재 플랜을 조회하는 작은 팝업을 연다. 설정은 알람, Live 2D 순서이며 로그아웃은 팝업 하단에 표시한다. 기기 요약·동기화 스위치·재연결·별도 Live 2D 승인 카드는 제거했다. Android 설치 화면은 OS에서 제공하므로 첫 실행 시 필수 수집·이용 동의를 받으며, 로그인 화면에는 로그인 기능만 표시한다. 동의와 계정 인증을 모두 확인한 뒤 화면 감지와 변경 이벤트 구독을 시작한다. 동의 버전·UTC 시각·대상 서버를 로컬에 저장하며 동의는 다른 서버로 이전하지 않는다. 로그아웃 후에는 화면 목록을 비우고 감지를 중지한다.

테스트 전용 임의 로그인과 테스트 앱 표시를 제거했다. 서버 입력란은 없으며 빌드에 포함된 운영 HTTPS 주소로 실제 계정을 인증하고 기기를 연동한다. 패키지는 `com.livelayer.app`이고 팀 공유 키로 서명한다. 기기 이름·OS·화면 해상도·주사율은 유지하며 제조사·모델·CPU 구조는 수집하지 않는다. 테스트 실행파일과 UI 검토용 임시 빌드는 정리했다.

릴리스 서명은 Git 밖 `C:\UpscaleLab-Secrets\key.properties`를 `UPSCALE_SIGNING_PROPERTIES_FILE`로 지정한다. [서명 설정 예시](android/key.properties.example)의 네 항목을 로컬에서 채우고, `keytool -list -v`로 지정한 JKS 별칭의 인증서 SHA-256을 기존 배포 APK와 대조한다. 빌드 후 각 APK를 `apksigner verify --print-certs`로 검사해 서명 인증서 SHA-256이 같은지 확인한다. `aapt2 dump badging`에서 같은 패키지 ID `com.livelayer.app`과 더 높은 `versionCode`를 확인한 다음 기존 앱을 삭제하지 않고 업데이트를 검증한다. [버전 관리 및 서명 규칙](../AGENTS.md)

배경화면 동기화는 로그인한 동안 활성화한다. Live 2D 설정은 계정·서버별로 저장하며 기본은 꺼짐이다. 켜면 앱 내 사용 의사를 기록하고, 대기 중인 결과물이 있을 때 최초 Android 시스템 적용 화면을 연다. 첫 홈화면 설정을 취소하면 설정을 끄고, 잠금화면 적용은 기존 설정을 유지한 채 다시 시도할 수 있다. 실제 엔진의 적용 확인 전에는 서버에 완료를 보고하지 않는다. 끄거나 로그아웃하면 움직임을 중지하고 정적인 화면은 유지한다. 일반 알람을 꺼도 Android의 필수 foreground service 실행 알림은 유지한다. 계정 정보는 조회 전용이다.

## GitHub Actions에서 빌드하고 PC에서 서명하기

1. GitHub 저장소의 **Settings → Secrets and variables → Actions → Variables**에 공개 운영 HTTPS origin을 `UPSCALE_COMPANION_API_BASE_URL` 저장소 변수로 등록한다. 기존 APK와 같은 origin을 사용한다. URL 경로·인증 정보는 넣지 않는다.
2. 워크플로가 포함된 변경을 `inho/eyemotionfix`에 push하면 Ubuntu에서 무서명 릴리스 APK를 빌드한다. 성공한 실행의 **Actions → Artifacts**에서 `android-1.8.4-51-unsigned`를 내려받아 압축을 푼다. APK와 SHA-256 파일이 들어 있다.
3. 서명은 Android SDK Build Tools 36.0.0과 Java가 설치된 PC에서 진행한다. JKS는 Git 밖에 둔다. 아래 PowerShell 예시에서 도구·JKS·기존 APK 경로를 지정하고, 비밀번호는 `apksigner`의 로컬 프롬프트에 입력한다.

```powershell
$env:JAVA_HOME = '<JDK 17 경로>'
$tools = '<Android SDK 경로>\build-tools\36.0.0'
$store = '<팀 JKS의 전체 경로>'
$previous = '<기존 1.8.2 APK의 전체 경로>'
$unsigned = 'Upscale_Lab-1.8.4+51-unsigned.apk'
$aligned = 'Upscale_Lab-1.8.4+51-aligned-unsigned.apk'
$signed = 'Upscale_Lab-1.8.4-.apk'
$expected = (Get-Content -LiteralPath "$unsigned.sha256" -TotalCount 1) -split '\s+' | Select-Object -First 1
if ((Get-FileHash -LiteralPath $unsigned -Algorithm SHA256).Hash -ine $expected) { throw '다운로드 SHA-256 불일치' }

& "$tools\zipalign.exe" -P 16 4 $unsigned $aligned
if ($LASTEXITCODE) { throw 'APK 정렬 실패' }
if (Test-Path -LiteralPath $signed) { throw '같은 이름의 출력 APK가 있습니다.' }
& "$tools\apksigner.bat" sign --ks $store --ks-key-alias upscalelab --out $signed $aligned
if ($LASTEXITCODE) { throw 'APK 서명 실패' }
& "$tools\zipalign.exe" -c -P 16 4 $signed
if ($LASTEXITCODE) { throw '서명된 APK 정렬 검증 실패' }

$certificates = foreach ($apk in @($previous, $signed)) {
    $report = & "$tools\apksigner.bat" verify --print-certs $apk
    if ($LASTEXITCODE) { throw 'APK 서명 검증 실패' }
    $signers = @($report | Select-String '^Signer #\d+ certificate SHA-256 digest: ([0-9a-fA-F]{64})$')
    if ($signers.Count -ne 1) { throw 'APK 서명자 정보를 확인할 수 없습니다.' }
    $signers[0].Matches[0].Groups[1].Value
}
if ($certificates[0] -ine $certificates[1]) { throw '기존 APK와 서명 인증서가 다릅니다.' }
$badging = & "$tools\aapt2.exe" dump badging $signed
if ($LASTEXITCODE -or !($badging -match "^package: name='com\.livelayer\.app' versionCode='51' versionName='1\.8\.4'( |$)")) {
    throw '신규 APK의 패키지 ID 또는 버전이 예상과 다릅니다.'
}
$badging | Select-String '^package:'
```

이 명령은 기존·신규 APK의 서명 인증서 SHA-256이 같은지, 신규 APK가 `com.livelayer.app`의 `1.8.4+51`인지 확인한다. 서명된 APK의 업데이트 검증은 기존 앱을 설치한 기기에서 앱을 삭제하지 않고 진행한다.
