# 내 이미지 카드 설정 메뉴 수정 운영 배포

2026-10-28 22:11 KST 수동 배포 완료. 운영 주소: https://d3bjir1z2hduu0.cloudfront.net/

- 웹: `/var/www/upscale-lab/releases/card-menu-20261028-131103`
- JavaScript: `/assets/index-CWknRxeZ.js`, CSS: `/assets/index-BMnTguzZ.css`
- 이전 웹: `/var/www/upscale-lab/releases/device-layout-20261028-124613`
- 검토 소스: `/home/ubuntu/upscale-web-sources/card-menu-20261028-131103-reviewed/`
- 복구 포인터와 파일 해시: `/home/ubuntu/deploy-backups/card-menu-20261028-131103/`

## 변경

이미지 카드를 세로 flex로 구성하고 본문이 남은 높이를 채우게 했다. 같은 행의 다른 카드에서 장르나 기능 배지가 여러 줄을 차지하더라도 이미지 설정 버튼은 카드 우측 하단에 고정된다.

설정 메뉴는 document pointerdown과 focusin에서 메뉴 바깥 선택을 감지해 닫으며, 다른 카드의 설정·이미지·빈 영역을 선택할 때 기존 메뉴가 닫힌다. 메뉴에서 Esc를 누르면 닫고 설정 버튼으로 포커스를 되돌린다. 이벤트 구독은 카드가 해제될 때 정리한다.

별도의 사진 설정 메뉴와 관련 edit 콜백을 제거했다. 이미지 또는 제목을 누르는 기존 작업실 진입은 유지한다. 업로드 ON/OFF, 공유 시 이름 변경 잠금, 삭제 동작은 유지한다.

업로드 후 선택 메뉴가 위로 이동하던 원인은 static인 details가 업로드 배지와 함께 높이가 달라지는 상위 영역을 위치 기준으로 사용했기 때문이다. details 자체를 relative로 바꿔 메뉴의 기준을 고정된 설정 버튼으로 옮겼다.

## 검증과 범위

- TypeScript 및 cloud Vite 빌드 통과. 로컬 웹 테스트 40건 통과, 운영 HTTPS 점검 포함 최종 웹 테스트 54건 통과.
- 변경 소스를 직전 운영 검토본과 비교해 ImageCard.tsx, Workspace.tsx, styles.css 세 파일의 변경만 패키지에 포함했다. 패키지 정적 파일의 SHA-256을 서버에서 확인했다.
- 원본 서버·CloudFront의 새 번들 제공, 사진 설정 문구 제거, health와 API·nginx 활성 상태를 확인했다. 업로드 파일·배포 도구·staging은 정리했고 이전 웹 릴리스는 보존했다.
- 로그인 후 실제 메뉴 클릭·기하 위치의 브라우저 검증은 이번 배포에서 수행하지 않았다. 54건은 기존 웹 회귀 및 HTTP 점검이며 이 UI 동작을 직접 검증하는 브라우저 테스트 수를 의미하지 않는다.
- API는 `/home/ubuntu/upscale-api-releases/device-layout-20261028-124613` 그대로이며 API 재시작, DB·GPU·인증·TLS·CI/CD·연결 앱 변경은 없다. Windows/Android 다운로드는 1.2.2를 유지한다.

로컬 검증 로그와 배포 도구는 무시되는 `artifacts/card-menu-release/`에 보존한다.
