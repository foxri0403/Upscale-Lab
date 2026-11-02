# Upscale Lab Web

React·TypeScript·Vite 웹서비스다. 업로드·편집·업스케일·배경 분리·Live 2D·보관함·공유/탐색·계정·결제·기기 관리를 제공한다. 사용자 데이터는 ASP.NET Core API를 거쳐 PostgreSQL/S3에 저장한다. [현재 동작](../docs/service-behavior.md), [선택 마스크](../docs/subject-selection.md), [Live 2D](../docs/live-2d.md)를 참고한다.

## 개발과 빌드

package.json에 지정한 pnpm과 호환되는 Node.js를 사용한다.

```powershell
cd web
pnpm install --frozen-lockfile
pnpm dev
pnpm dev:production-ui
pnpm test
pnpm build
pnpm build:cloud
```

pnpm build는 TypeScript 검사와 일반 Vite 빌드를 수행한다. build:cloud는 설치 파일을 검증하고 실제 API를 쓰도록 dist/config.js를 { mode: "cloud", apiBaseUrl: "" }로 설정한 뒤 산출물 다운로드를 다시 검증한다. 같은 origin에서 /api, /health, /hubs를 API로 전달한다. 다른 API origin은 공개 config.js의 apiBaseUrl과 서버/S3 CORS를 맞춘다.

클라우드 조회 실패 시 데모 데이터로 대체하지 않고 연결 오류를 표시한다. 데모 샘플 이미지는 [ASSETS.md](ASSETS.md)에 출처를 기록했다. 로컬 데모·소개 화면·테스트에서 사용하므로 유지한다. VITE_ 변수와 config.js는 공개되며 비밀번호·JWT secret·공급자 key를 넣지 않는다.

## 기능 소개 로컬 테스트

`pnpm dev:production-ui` 실행 후 [http://localhost:5173/](http://localhost:5173/)에서 로그인 없이 확인한다. 운영 서버와 같은 React 화면·글꼴·클라우드 UI 분기를 사용하므로 로컬 테스트 계정 안내는 표시하지 않는다. 기본 헤더·메뉴·작업실 레이아웃을 새 UI로 바꾸지 않는다. 일반 샘플 계정 모드는 기존 `pnpm dev`로 계속 실행할 수 있다.

실제 빌드 결과로 확인하려면 `pnpm build:production-ui` 다음 `pnpm preview:production-ui`를 실행한다. 계정과 개인 데이터 API는 기본 `http://127.0.0.1:5080`으로 전달하며, 별도 로컬 API는 `.env.local`의 `LOCAL_API_TARGET`으로 지정한다. 운영 API 주소나 자격을 코드에 넣지 않는다. 소개 데모는 API가 없어도 동작하며, 실제 로그인·작업실 데이터 처리는 연결된 API가 필요하다.

- 01: 05번 모니터와 같은 호수 사진 위 슬라이더를 드래그하거나 방향키·Home·End로 저해상도/고해상도 예시를 비교한다. 소개용 흐림 효과 비교이며 서버의 AI 처리를 실행하거나 토큰을 사용하지 않는다.
- 03: PC에서는 페이지의 마우스 이동에 분리된 여우 피사체가 기본 반응하며 숲 배경은 고정된다. 모바일에서는 자이로 입력이 기본이다.
- 04: 기존 See-through 전용 엔드포인트에서 생성한 캐릭터 파츠를 항상 전체 합성으로 표시한다. 몸·의상은 고정하고 고개 회전·눈동자 이동·앞/뒷머리의 지연 흔들림을 작업실과 같은 PNG 파츠 리그로 기본 재생한다. 목 파츠는 옷깃 뒤쪽 면 위에 합성하고 앞쪽 옷깃 곡선으로 잘라 목과 상의가 자연스럽게 이어지도록 보정했다. PC 마우스·모바일 자이로가 기본이며 방향키와 Home도 지원한다. 정지·자이로 전환·파츠 선택 버튼은 없다. 정적 파츠 파일을 포함하므로 소개 화면을 열 때 GPU 작업을 생성하지 않는다.
- 03·04는 포인터 입력 장치에 따라 PC 마우스/모바일 자이로를 자동 선택한다. 창 크기만 줄인 PC는 마우스를 유지한다. 명시적 센서 권한 API가 없는 모바일 브라우저에서는 센서가 바로 연결되고, 권한 API가 있는 브라우저에서는 첫 화면 터치/클릭에 권한 요청 하나를 연결해 두 데모가 공유한다. 권한 요청에 사용자 조작이 필요한 것은 [Device Orientation 권한 규격](https://www.w3.org/TR/orientation-event/#dom-deviceorientationevent-requestpermission)에 따른 제약이다. 센서 지원 기기의 HTTPS 또는 localhost가 필요하며 첫 유효 입력을 중립으로 보정한다. 권한 거부·응답 없음은 안내 문구로 표시한다. 화면 밖·숨긴 탭·OS의 움직임 줄이기 설정에서는 프레임 갱신을 중단한다.
- 05: 모니터에는 기존 호수 사진, 태블릿에는 여우 사진, 휴대폰에는 새 캐릭터가 표시된다. 문의 메뉴와 임시 문의 저장 기능은 제거했다.

[브라우저 회귀 검사](http://localhost:5173/tests/feature-showcase.html)에서 PC 마우스·모바일 자동 센서·첫 터치 권한·권한 거부 시나리오를 선택한다. 실행 버튼은 검사 페이지에만 있으며 실제 소개 화면에는 없다. 기본 입력·전체 합성·목/옷깃 픽셀 합성·고정 몸통·개별 파츠 리그·방향키·움직임 줄이기·배경 고정을 검사한다. 센서와 포인터 종류의 모의 응답은 이 개발용 검사 페이지에만 적용되며 실제 휴대폰의 물리 센서 검증을 대신하지 않는다. 이 검사 페이지는 Vite 개발 서버에서 제공하며 빌드된 운영 번들에는 포함하지 않는다.

## 연결 앱 다운로드

public/downloads/manifest.json과 그 manifest가 가리키는 서명된 Android APK·Windows EXE가 필요하다. Git에서 제외된 현재 배포 파일이므로 정리할 때 일반 빌드 산출물로 취급하지 않는다. `node scripts/verify-downloads.mjs public/downloads`로 크기·SHA-256을 검증한다. iOS는 준비 중이다. [연결 앱 빌드와 제약](../docs/companion-apps.md)을 따른다.

## 개발용 피사체 분리 실험실

개발 모드 /lab/separation은 실제 자동 분리, 선택 수정, 마스크 기반 재분리와 PNG 확인을 제공한다. [SeparationLab](../backend/tools/UpscaleLab.SeparationLab/README.md)의 스크립트를 사용한다. 운영 DB·배포를 변경하지 않으며 실제 클라우드 실행에는 별도 자격이 필요하다. SlimSAM worker와 transformers 의존성은 이 개발 경로에서 사용하므로 유지한다.

tests/*.html과 브라우저 진입 파일은 선택 마스크·기기·작업실 브라우저 회귀 도구다. 기본 Vite 운영 진입점에 포함되지 않아도 검증에 사용하므로 유지한다. pnpm test의 클라우드 smoke는 관련 환경변수를 지정한 경우에만 원격 서비스를 검증한다.

## 수동 배포

자동 배포는 구성하지 않는다. [배포 가이드](../docs/deployment.md), [nginx 예시](../deploy/nginx-web.conf.example), [수동 설치 스크립트](../deploy/install-web.sh)를 따른다. 기존 서명 설치 파일을 포함한 cloud 빌드를 만든 다음 수동으로 전달한다.

```powershell
pnpm build:cloud
New-Item -ItemType Directory -Force ../tmp | Out-Null
tar -czf ../tmp/upscale-web.tar.gz -C dist .
```

운영 반영은 별도 작업이다. 로컬 빌드·단위 테스트만으로 실제 AWS 권한·GPU 품질·결제 승인·Windows/Android OS 적용까지 검증한 것으로 간주하지 않는다.
