# 연결 앱 로컬 테스트

현재 테스트 파일은 **1.4.1**, Android build **42**이다. 운영 배포와 원격 Git 푸시는 하지 않는다.

## 실행 파일

- Windows: `artifacts/companion-local/UpscaleLab-Connect-1.4.1-local-win-x64.exe`
- Android: `artifacts/companion-local/Upscale_Lab-1.4.1-local.apk`
- 해시·크기: 같은 폴더의 `manifest.json`

Windows EXE에는 .NET 런타임이 포함된다. 더블클릭하면 현재 사용자 계정에 설치하는 창이 열린다. 바탕화면 바로 가기와 시작 메뉴 등록을 선택하고, 기본 미선택인 기기·화면 정보 수집·이용 동의를 확인한 뒤 설치한다. `--run`으로 직접 실행하거나 기존 설치를 업데이트하는 경우에도 새 동의가 없으면 첫 실행 동의 화면이 먼저 표시된다. 설치하지 않고 바로 실행하려면 저장소 루트 PowerShell에서 다음 명령을 사용한다.

```powershell
& '.\artifacts\companion-local\UpscaleLab-Connect-1.4.1-local-win-x64.exe' --run
```

Android Studio에서 휴대폰 또는 태블릿 AVD를 실행한 뒤 APK를 에뮬레이터 창에 끌어 놓는다. x86_64 및 ARM64 에뮬레이터에 사용할 수 있는 APK다. 테스트 패키지 ID는 `com.livelayer.app.localtest`이며 기존 운영 앱과 별도로 설치된다. Windows도 `Upscale Lab Connect (Local Test)` 경로와 바로 가기를 사용한다. Windows 계정 세션·기기 자격은 서버별 DPAPI 프로필로 분리된다.

## 로컬 API와 웹

앱 안에는 서버 주소 입력 기능이 없다. 이 테스트 EXE는 `http://127.0.0.1:5080`, APK는 Android 에뮬레이터에서 PC를 가리키는 `http://10.0.2.2:5080`으로 연결한다. 로그인과 디스플레이 연동에는 로컬 API가 필요하다. UI 확인만으로 운영 서버의 계정·기기 상태는 변경되지 않는다.

먼저 [루트 README](../README.md)의 로컬 PostgreSQL·user-secrets·마이그레이션 설정을 마친다. 운영 DB 자격을 복사하지 않는다. 앱은 브라우저 데모 계정 대신 로컬 API에 실제 등록된 동일 계정으로 로그인한다. API와 웹을 서로 다른 PowerShell 창에서 실행한다.

```powershell
# 저장소 루트: 로컬 API. 최초 1회 필요한 restore/build는 자동 수행한다.
.\.dotnet\dotnet.exe run --project backend/src/UpscaleLab.Api --no-launch-profile --urls http://127.0.0.1:5080
```

```powershell
cd web
pnpm dev:production-ui
```

`http://localhost:5173`에서 같은 계정으로 로그인하고 기기 관리를 연다. API 상태는 `http://127.0.0.1:5080/health`에서 확인한다. 서버가 실행되지 않으면 연결 앱은 로그인 오류를 표시한다. APK의 HTTP 허용은 이 명시적인 로컬 테스트 빌드에만 적용한다.

계정과 API 없이 웹 카드 디자인을 먼저 확인하려면 `pnpm dev` 실행 후 [기기 UI 검증 페이지](http://localhost:5173/tests/device-ui.html)를 연다. 이 페이지는 모의 API와 별도 세션 키를 사용하며 운영 번들에 포함되지 않는다.

## 확인할 동작

1. 앱 아이콘이 설치 창, 바탕화면/시작 메뉴, 트레이와 Android 런처에 표시되는지 확인한다.
2. 서버 입력 없이 로그인하면 기기가 자동 등록되고 실제 디스플레이가 웹에 표시되는지 확인한다.
3. 휴대폰과 태블릿 AVD를 각각 연결한다. 태블릿도 기존 기기 카드·홈/잠금 화면·메타데이터·버튼 구조를 사용하고 테두리만 넓은 태블릿 형태로 표시한다. 내장 화면의 짧은 변을 배율로 나눈 값이 600dp 이상이면 태블릿으로 분류한다. 외부 모니터는 분류에서 제외한다.
4. Windows 설치에서 바탕화면 바로 가기를 선택/해제했을 때 선택한 항목만 생성되는지 확인한다.
5. 동기화 중지, Live 2D 최초 승인, 로그아웃, 연결 해제 후 앱의 기기 다시 연결을 확인한다. 실제 OS 배경화면 변경은 수동으로 확인한다.
6. Windows 설치 및 Android 첫 실행에서 수집 목적·기기/화면 정보 항목을 확인한다. 동의 항목을 선택하기 전에는 진행할 수 없어야 한다. Android OS 설치 화면 자체는 변경하지 않는다.
7. 동의 후에는 로그인 UI만 표시한다. 로그인 실패 시에도 화면 정보는 읽거나 표시하지 않아야 하며, 로그인 성공 후에만 기기 카드·화면 정보·동기화·재연결을 표시한다. 로그아웃하면 화면 목록과 감지를 중지한다. 저장된 자동 로그인도 동의가 있어야 복원한다.
8. 웹의 기기 관리 상단에서 연결 앱 다운로드 버튼이 사라졌는지 확인한다. 앱 다운로드는 별도 메뉴에서 이용한다.

## 다시 빌드

APK를 다시 생성하기 전에 AGENTS.md의 버전 증가·문서 동기화 규칙을 적용한다. API 주소는 빌드 시 포함되며 앱 사용자에게 입력받지 않는다.

```powershell
$env:UPSCALE_SIGNING_PROPERTIES_FILE = 'C:\UpscaleLab-Secrets\key.properties'
.\companion\Build-Release.ps1 -LocalTest -Flutter 'C:\dev\flutter\bin\flutter.bat' -Dotnet '.\.dotnet\dotnet.exe' -ApkSigner "$env:LOCALAPPDATA\Android\Sdk\build-tools\36.0.0\apksigner.bat"
```

`-LocalTest`는 테스트 폴더에만 결과물을 저장한다. `web/public/downloads`의 기존 배포 파일을 교체하지 않는다. 일반 배포용 빌드는 `-ApiBaseUrl`로 실제 배포 대상 주소를 명시해야 하며, Windows/Android 앱이 해당 주소를 내부적으로 사용한다. 서명키와 운영 주소를 소스에 추가하지 않는다.
