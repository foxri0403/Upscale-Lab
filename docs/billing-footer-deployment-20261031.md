# 토큰 메뉴 중복 수정과 작업 브랜치 정리

- 웹 코드: `60398b8c8ee1bcefaccf7882664b5b8714289ef9`
- 웹 릴리스: `billing-footer-20261030-235938`
- 전환: 2026-10-31 00:00:40 KST
- 운영: <https://d3bjir1z2hduu0.cloudfront.net/>

`Workspace.tsx`의 상단 navigation 배열에서 billing 항목 하나만 제거했다. 아래쪽 토큰·요금제 링크, 페이지 라우팅, 헤더 지갑과 기존 스타일은 유지했다. 원래 미커밋된 자르기·이미지 정보 변경은 포함하지 않고 별도 깨끗한 작업 폴더에서 빌드했다.

두 `gusrb03/background-separation`, `gusrb03/selection-billing-release`의 끝 커밋 `1ce9707`과 원격 끝 커밋이 모두 main에 포함된 것을 검사했다. 원격 ref가 바뀌면 삭제가 중단되도록 명시적 lease를 사용해 원격 두 브랜치를 함께 삭제했고 로컬도 merged 확인 후 삭제했다. 현재 작업 브랜치는 main이다. 통합 작업 폴더의 파일은 detached 상태로 보존했다.

TypeScript/Vite cloud 빌드와 운영 HTTP smoke 검사 15개를 통과했다. 웹만 원자적으로 전환했으며 API DLL 해시와 실행 포인터가 그대로임을 확인했다. API·DB·결제 설정을 변경하거나 서비스를 재시작하지 않았다. health는 Healthy였다.

이전 웹 백업과 독립 복구 스크립트는 `/home/ubuntu/deploy-backups/billing-footer-20261030-235938/`에 보관했다. 이번 업로드·staging·임시 실행 스크립트는 정리했다. 배경 분리 성능에 대한 [검토안](background-separation-improvement-review-20261031.md)은 별도로 기록했으며 추론 경로에는 적용하지 않았다.
