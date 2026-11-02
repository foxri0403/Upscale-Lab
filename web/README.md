# Upscale Lab Web

React·TypeScript·Vite 웹서비스다. 업로드·편집·업스케일·배경 분리·Live 2D·보관함·공유/탐색·계정·결제·기기 관리를 제공한다. 사용자 데이터는 ASP.NET Core API를 거쳐 PostgreSQL/S3에 저장한다. [현재 동작](../docs/service-behavior.md), [선택 마스크](../docs/subject-selection.md), [Live 2D](../docs/live-2d.md)를 참고한다.

## 개발과 빌드

package.json에 지정한 pnpm과 호환되는 Node.js를 사용한다.

```powershell
cd web
pnpm install --frozen-lockfile
pnpm dev
pnpm test
pnpm build
pnpm build:cloud
```

pnpm build는 TypeScript 검사와 일반 Vite 빌드를 수행한다. build:cloud는 설치 파일을 검증하고 실제 API를 쓰도록 dist/config.js를 { mode: "cloud", apiBaseUrl: "" }로 설정한 뒤 산출물 다운로드를 다시 검증한다. 같은 origin에서 /api, /health, /hubs를 API로 전달한다. 다른 API origin은 공개 config.js의 apiBaseUrl과 서버/S3 CORS를 맞춘다.

클라우드 조회 실패 시 데모 데이터로 대체하지 않고 연결 오류를 표시한다. 데모 샘플 이미지는 [ASSETS.md](ASSETS.md)에 출처를 기록했다. 로컬 데모·소개 화면·테스트에서 사용하므로 유지한다. VITE_ 변수와 config.js는 공개되며 비밀번호·JWT secret·공급자 key를 넣지 않는다.

## 기능 소개 로컬 테스트

`pnpm dev` 실행 후 [http://localhost:5173/](http://localhost:5173/)에서 로그인 없이 확인한다. 기존 로그인 세션이 있으면 로그아웃하거나 별도 브라우저 프로필을 사용한다.

- 01: 바다 사진 위 슬라이더를 드래그하거나 방향키·Home·End로 저해상도/고해상도 예시를 비교한다. 소개용 흐림 효과 비교이며 서버의 AI 처리를 실행하거나 토큰을 사용하지 않는다.
- 03: 여우 사진 위에서 마우스를 움직이면 분리된 피사체만 이동하고 숲 배경은 고정된다.
- 04: 여성 버튜버 캐릭터가 마우스와 자이로 입력에 따라 조금씩 이동한다. 미리보기 영역에 포커스하고 방향키로 이동하거나 Home으로 중립 위치로 돌아갈 수도 있다. 이 소개 데모는 준비된 PNG를 사용한다.
- 03·04의 `자이로 켜기`는 사용자 조작 후 브라우저 권한을 요청하고 첫 유효 센서 입력을 중립값으로 보정한다. 센서 지원 기기의 HTTPS 또는 localhost 보안 컨텍스트가 필요하다. 휴대폰에서 일반 HTTP LAN 주소로 접속하면 센서를 사용할 수 없으며, PC에 실제 센서가 없을 때는 응답 없음 안내가 표시된다. `멈추기`는 움직임을 중립으로 돌리고, OS의 움직임 줄이기 설정도 따른다.
- 05: 모니터에는 기존 호수 사진, 태블릿에는 여우 사진, 휴대폰에는 새 캐릭터가 표시된다. 문의 메뉴와 임시 문의 저장 기능은 제거했다.

[브라우저 회귀 검사](http://localhost:5173/tests/feature-showcase.html)에서 실행 버튼을 누르면 실제 소개 컴포넌트의 비교·마우스·방향키·정지·센서 권한 거부/허용·센서 지연과 배경 고정을 검사한다. 센서 모의 응답은 이 개발용 검사 페이지에만 적용되며 실제 휴대폰의 물리 센서 검증을 대신하지 않는다. 390px 등 좁은 화면에서도 같은 검사를 실행할 수 있다.

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
