# 연결 앱 로컬 테스트

현재 테스트 파일은 **1.5.0**, Android build **43**이다. 로컬 커밋까지만 진행하며 운영 배포와 원격 Git 푸시는 하지 않는다.

## 실행 파일

- Windows: `artifacts/companion-local/UpscaleLab-Connect-1.5.0-local-win-x64.exe`
- Android: `artifacts/companion-local/Upscale_Lab-1.5.0-local.apk`
- 해시·크기: 같은 폴더의 `manifest.json`

Windows EXE에는 .NET 런타임이 포함된다. 더블클릭하면 현재 사용자 계정에 설치하는 창이 열린다. 바탕화면 바로 가기와 시작 메뉴 등록을 선택하고, 기본 미선택인 기기·화면 정보 수집·이용 동의를 확인한 뒤 설치한다. 설치 없이 실행하거나 기존 설치를 업데이트한 경우에도 저장된 동의가 없으면 첫 실행 동의 화면을 표시한다.

```powershell
& '.\artifacts\companion-local\UpscaleLab-Connect-1.5.0-local-win-x64.exe' --run
```

Android Studio에서 휴대폰 또는 태블릿 AVD를 실행한 뒤 APK를 에뮬레이터 창에 끌어 놓는다. x86_64·ARM64 에뮬레이터를 지원한다. 테스트 패키지 ID는 `com.livelayer.app.localtest`이고 런처 이름에는 `(테스트 앱)`이 붙는다. Windows는 `Upscale Lab Connect (Local Test)` 설치 경로를 사용하고 창 제목과 화면에 `테스트 앱`을 표시한다.

## 서버 없이 로그인

이 테스트 파일은 UI 미리보기용이다. API·DB를 실행하거나 계정을 만들 필요가 없다. 개인정보 수집 안내에 동의한 뒤 아이디와 비밀번호에 아무 값을 입력해 로그인한다. 두 칸을 모두 비워도 시작할 수 있다. 입력한 비밀번호를 검증·저장·전송하지 않으며 API 세션이나 기기 토큰을 만들지 않는다.

로그인 후 실제 기기의 화면 정보를 로컬에서 읽어 표시한다. 이 정보는 서버로 보내지 않는다. 동기화 스위치는 UI 상태만 바꾸고, 기기 다시 연결은 화면 정보를 새로 확인한다. 실제 배경화면 적용·기기 등록·웹 연동은 수행하지 않는다. 로그아웃하면 화면 목록을 비우고 앱을 종료하면 테스트 로그인 상태를 초기화한다. 테스트 빌드에서는 자동 로그인과 Windows 시작 실행 옵션을 비활성화한다.

일반 빌드는 기존 서버 인증과 계정·기기 이중 인증을 유지한다. 테스트 로그인은 Windows의 빌드 메타데이터 `CompanionLocalTest=true`, Android의 빌드 상수 `COMPANION_LOCAL_TEST=true`에서만 동작한다. 앱 화면이나 로그인 입력으로 이 모드를 켤 수 없다.

## 웹 UI를 함께 확인

웹 카드 디자인은 다음 명령으로 실행한 뒤 [기기 UI 검증 페이지](http://localhost:5173/tests/device-ui.html)에서 확인한다. 이 페이지는 모의 API와 별도 세션 키를 사용한다. 연결 앱의 테스트 로그인은 웹 계정과 연동되지 않는다.

```powershell
cd web
pnpm dev
```

실제 API 개발 환경 설정은 [루트 README](../README.md)를 따른다. 서버 주소 입력 UI는 제공하지 않는다.

## 확인할 동작

1. Windows 창 제목·헤더, Android 런처·상단 바에 테스트 앱 표시가 보이는지 확인한다.
2. 필수 동의 전에는 진행할 수 없고, 로그인 전에는 화면 정보를 감지하거나 표시하지 않는지 확인한다.
3. 임의 문자열·이메일 형식이 아닌 값·빈칸으로 모두 로그인되는지 확인한다. API가 꺼져 있어도 동작해야 한다.
4. 로그인 후 화면 개수·해상도·주사율을 표시하는지 확인한다. 휴대폰·태블릿 AVD에서 각각 레이아웃을 확인한다.
5. 동기화 미리보기·화면 정보 새로 확인·로그아웃을 조작하고 실제 배경화면이 변경되지 않는지 확인한다.
6. Windows 설치에서 선택한 바로 가기만 생성되는지, Android 첫 실행에서 동의 안내를 표시하는지 확인한다. Android 시스템의 APK 설치 화면 자체는 변경하지 않는다.
7. 웹 기기 관리 상단에서 연결 앱 다운로드 버튼이 사라졌는지 확인한다. 앱 다운로드는 별도 메뉴에서 이용한다.

## 다시 빌드

APK 생성 전에 AGENTS.md의 버전 증가·문서 동기화 규칙을 적용한다. 서명키는 Git 밖 파일을 사용한다.

```powershell
$env:UPSCALE_SIGNING_PROPERTIES_FILE = 'C:\UpscaleLab-Secrets\key.properties'
.\companion\Build-Release.ps1 -LocalTest -Flutter 'C:\dev\flutter\bin\flutter.bat' -Dotnet '.\.dotnet\dotnet.exe' -ApkSigner "$env:LOCALAPPDATA\Android\Sdk\build-tools\36.0.0\apksigner.bat"
```

`-LocalTest`는 테스트 폴더에만 결과물을 저장한다. `web/public/downloads`의 운영 배포 파일은 교체하지 않는다. 테스트 빌드에 포함된 로컬 서버 주소는 UI 미리보기에서 사용하지 않는다. APK는 팀 공유 키로 서명하고 `apksigner`로 검증한다.
