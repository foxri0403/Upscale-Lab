# 작업실·기기 관리 화면 정리 운영 배포

2026-10-28 21:49 KST 수동 운영 배포 완료. 주소: https://d3bjir1z2hduu0.cloudfront.net/

- API: `/home/ubuntu/upscale-api-releases/device-layout-20261028-124613`
- 웹: `/var/www/upscale-lab/releases/device-layout-20261028-124613`
- JavaScript: `/assets/index-Bd4WLyKK.js`, CSS: `/assets/index-Dzb0Y-Y-.css`
- 이전 API·웹: `motion-20261028-105741`, 복구 포인터와 검증 로그: `/home/ubuntu/deploy-backups/device-layout-20261028-124613/`
- 배포 소스: `/home/ubuntu/upscale-api-sources/device-layout-20261028-124613-reviewed/`

## 반영 사항

작업실의 이미지 추가 화면에서 최대 너비 제한과 우측 상단 내 이미지 버튼을 제거했다. 다른 화면과 같은 좌우 여백을 사용한다.

기기 관리의 계정 사용 설명을 제거하고 새로고침은 접근성 이름을 가진 아이콘 버튼으로 표시한다. 이미지 제목을 미리보기 바로 위에서 모니터 이름·연결 상태와 같은 행의 가운데에 배치했다. 별도의 현재 배경화면 영역과 해당 라벨을 제거했다. 긴 이름과 제목은 말줄임으로 처리한다.

기기 이름·모니터 이름·운영체제·화면 크기(픽셀 해상도)·주사율과 기존 화면 연결 정보는 계속 수집하고 표시한다. 제조사·제품 모델·CPU 구조만 연결 앱의 수집·전송, API 계약·저장·응답, 웹·모바일 표시에서 제거했다. 구형 앱이 보내는 해당 JSON 필드는 무시한다. 기존 DB 열은 스키마 호환을 위해 유지하며 저장값은 해당 기기의 다음 유효 heartbeat에서 비운다. Android 신규 등록은 OS에 설정된 기기 이름을 사용하고, 읽을 수 없을 때만 일반 이름을 사용한다. 이미 연결된 기기의 이름은 변경하지 않는다.

최근 적용 내역 UI, 웹의 내역 저장·조회·매핑 코드와 계정용 GET 명령 내역 API를 제거했다. 이전 로컬 라이브러리의 기록은 화면을 열 때 현행 필드로 다시 저장하면서 제거한다. 기기 명령 저장은 배경화면 전달·재시도·멱등성·현재 결과물 복구에 사용한다. 에이전트는 대기 중인 명령만 조회한다.

## 연결 앱

- Windows 1.2.2: `UpscaleLab-Connect-1.2.2-win-x64.exe`, 83,377,369 bytes, SHA-256 `b501a5b75203c822cd62d5bdc8f9db7e7bad091fc95f3d641db97481d61eacaa`.
- Android 1.2.2+36: `Upscale_Lab-1.2.2-.apk`, 48,980,950 bytes, SHA-256 `db8c24b1a14d00a06c61868e454d63e3df5af0833fede53af91133595c57cbd6`.
- APK는 Android apksigner 검증 통과. 인증서 SHA-256 `d2e2dc332f34de8ea72a0d1b4aa2c428306ba7ae4529f26ed6dd504018572b93`은 이전 1.2.1 정식 릴리스와 일치한다. 버전 이름 1.2.2와 Android versionCode 36을 확인했다. Windows는 기존과 같이 Authenticode 미서명이다.
- 운영 다운로드 manifest는 1.2.2를 제공하며 이전 다운로드 URL도 유지한다. Windows는 이전 트레이 앱을 종료하고 새 파일을 실행한다. Android는 APK 업데이트가 필요하다. 구형 앱의 하드웨어 필드는 서버에서 이미 저장하지 않지만, 기기 자체의 수집·전송 중단에는 앱 업데이트가 필요하다.

## 검증과 복구

- 백엔드 전체 xUnit 191건, Windows 15건, Android 앱 `:app:testReleaseUnitTest` 66건, 운영 HTTPS 웹 테스트 54건 통과.
- 실제 운영 HTTPS에서 임시 계정·가상 Windows/Android 기기를 만들고 기기 이름·모니터 이름·OS·2560×1440 해상도·59.94Hz 유지, 제조사·제품 모델·CPU 구조 응답 제외와 기존 저장값 삭제를 확인했다.
- 최근 내역 GET은 405이며 기기 키·계정 세션의 이중 인증은 유지된다. 가상 기기 삭제 명령의 생성·수신·완료 보고와 현재 화면 상태 반영도 통과했다. 검증 계정·기기·명령을 정리했다. 사용자 기기, GPU 작업, S3 이미지는 변경하지 않았다.
- 운영 API·nginx 활성 상태, health, CloudFront의 새 번들·설정·다운로드 manifest·파일 해시·바이트 크기·실행 파일 형식을 확인했다.
- DB migration, 인증 자격, TLS/CloudFront 정책, GPU endpoint와 CI/CD는 변경하지 않았다. 배포 전 API 변경 파일과 유지할 종속성 해시를 비교했고, 실패 시 이전 API·웹 포인터로 복구하도록 준비했다. 이전 릴리스는 보존했다.
- 이번 배포에서 로그인 후 화면을 브라우저로 시각 검증하지는 않았다. 실제 Windows/Android 사용자 기기의 OS 적용은 새 앱 설치 후 확인해야 한다.

로컬 빌드·검증 도구와 로그는 무시되는 `artifacts/device-layout-release/`에 보존한다. 운영 업로드·검증 staging은 정리했다.
