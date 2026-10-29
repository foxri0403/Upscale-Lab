# 기기 관리 UI 및 배경화면 삭제 배포

2026-10-28 UTC, 수동 운영 배포 완료. 주소: https://d3bjir1z2hduu0.cloudfront.net/

- API: `/home/ubuntu/upscale-api-releases/devices-20261028-093442`
- 웹: `/var/www/upscale-lab/releases/devices-20261028-093442`
- 번들: `/assets/index-CzIOp3kX.js`
- 이전 API: `layer-ui-final-20261028-071640`, 이전 웹: `gyro-20261028-081623`
- 운영 자격·TLS·GPU endpoint·CI/CD 설정은 변경하지 않았다.

## 변경 사항

기기 연결 버튼과 카드 하단 계정 연결 문구를 제거했다. 카드 상단의 최근 연결 표시를 상태 새로고침으로 바꿨다. 최근 적용 내역은 기기 → 화면 → 이미지 → 상태 네 열로 표시하며 요청 시간은 제거했다. 연결됨은 녹색, 첫 heartbeat 대기는 주황색, 통신이 오래됐거나 키가 폐기된 연결은 적색이다.

배경화면 변경 아래에 삭제 버튼을 추가했다. 선택 화면 확인 후 서버에 Clear 명령을 저장하고 최신 연결 앱이 실제 OS 초기화를 수행한다. 대기 중에는 기존 관측 이미지를 유지하고, 성공 보고 후 기본 배경화면 및 삭제 완료로 표시한다. 다른 화면과 내 이미지/S3 원본은 유지한다. 예전 앱은 삭제 기능을 광고하지 않으므로 버튼이 비활성화되며 앱 업데이트가 필요하다.

## 연결 앱

- Windows 1.2.0: `UpscaleLab-Connect-1.2.0-win-x64.exe`, 83,377,023 bytes, SHA-256 `537e523f0de9cc2f2c3fea0fc1ce8d515f6f45b96fbe5ae25a9cb35c76836cd0`.
- Android 1.2.0+34: `Upscale_Lab-1.2.0-.apk`, 48,980,954 bytes, SHA-256 `e1930751c1e5df5bda631583fb774420b089225f0d3938420369d5cc12149600`.
- APK는 apksigner 검증 통과. 인증서 SHA-256 `d2e2dc332f34de8ea72a0d1b4aa2c428306ba7ae4529f26ed6dd504018572b93`, 기존 1.1.0과 일치한다. Windows는 기존과 같이 Authenticode 미서명이며 단일 실행 파일 압축을 적용했다.
- 홈페이지 다운로드 manifest를 1.2.0으로 변경하고 이전 다운로드 URL은 유지했다. 서명 자격과 바이너리는 Git에서 제외한다.

## DB와 복구

`20261028085315_AddDeviceWallpaperRemoval` 하나만 적용했다. SupportsWallpaperRemoval 기본 false, command.Action 기본 Apply, WallpaperAssetId nullable 변경이다. 관련 다섯 테이블과 EF 이력은 `/home/ubuntu/deploy-backups/devices-20261028-093442/affected-device-tables.dump`에 백업했다. 첫 시도는 pg_dump의 TLS 인증서 위치 문제로 DB 변경 전 중단됐으며 기존 서비스를 복구했다. 시스템 신뢰 저장소를 사용해 인증서 검증을 유지한 두 번째 시도가 통과했다. 첫 시도의 진단 로그는 별도 보존한다.

기존 AddInpaintingRunPodJobId 이력과 실제 스키마의 불일치는 이번 범위에서 변경하지 않았다. 전체 migration 자동 실행을 추가하지 않았다. Clear 명령은 null asset을 사용하므로 실제 삭제 이력이 생긴 뒤에는 이전 API로 단순 롤백하지 않는다. 웹만 이전 symlink로 복구하고 새 API를 유지할 수 있다. Down migration도 삭제 명령이 있으면 중단한다. DB 전체 복구는 이 부분 백업의 범위가 아니며, 운영자 판단 없이 실행하지 않는다.

배포 공간은 이번 대화에서 만든 이전 세 API 릴리스의 동일 라이브러리 89개를 SHA-256으로 확인한 뒤 하드링크로 공유해 약 125MB 확보했다. 첫 시도 배포본은 활성 상태가 아님을 확인하고 제거했다. 공유된 라이브러리는 불변이며 변경 시 새 파일로 교체해야 한다. 변경된 자체 어셈블리와 설정은 독립 복사본으로 배포했다.

## 검증

- 백엔드 xUnit 187개 통과. 소유권, 연결 앱 지원 여부, 멱등성, 만료·폐기·대체, 실패 시 기존 이미지 유지, Android Home/Lock 분리 포함.
- Windows 테스트 9개 통과. 선택 모니터 삭제 및 OS 확인 실패 시 Live 창 유지 포함.
- Android `:app:testReleaseUnitTest` 63개 통과. 삭제 성공·실패 및 다른 대상 보존 포함. 전체 Gradle 하위 프로젝트의 기존 image_picker 테스트 10개는 리소스 누락으로 실패했으며 앱 자체 범위에서는 실패가 없다.
- 운영 HTTPS 웹 테스트 54개 통과. 다운로드 manifest·헤더·바이트 크기·실행 파일 매직·SPA/API 경로·건강 상태 포함.
- Edge 브라우저에서 실제 운영 번들에 API 응답을 대체해 상태 색상, 버튼 순서, 확인 취소, 삭제 대기 시 현재 미리보기 유지, 완료 보고 반영, 다른 기기 유지, 휴대폰 가로 넘침 없음을 확인했다.
- 운영 HTTPS API는 임시 계정·가상 기기로 실제 인증, 명령 생성/수신/완료 보고, 이전 앱 차단, 중복 요청, 다른 화면 보존, 이중 인증, 오래된 명령 차단과 키 폐기를 검증했다. 임시 계정·기기·명령·메타데이터는 정리했다. 이 검증은 사용자 기기의 OS나 S3 이미지를 변경하지 않았다.
- 실제 Windows 바탕화면과 Android 실기기에서의 OS 삭제는 자동 실행하지 않았다. 사용 기기에 1.2.0을 설치하고 연결한 뒤 최종 확인이 필요하다.

검증 로그와 브라우저 캡처는 무시되는 `artifacts/device-removal/`에 보존한다. API 계약은 [agent-api.md](agent-api.md), 연결 앱 설명은 [companion-apps.md](companion-apps.md)를 참고한다.
