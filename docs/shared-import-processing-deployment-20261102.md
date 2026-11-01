# 공유 이미지 가져오기·클라우드 분리 수정 배포

- 운영 전환: 2026-11-02 00:49:13 KST (`2026-11-01T15:49:13Z`).
- 운영: https://d3bjir1z2hduu0.cloudfront.net/.
- 소스: `67259f727f28b0ae9f36eee81d5bd3dd1a49b180` (main 커밋·푸시).
- 릴리스: `processing-fix-20261102-004500`.
- API: `/home/ubuntu/upscale-api-releases/processing-fix-20261102-004500`.
- 웹: `/var/www/upscale-lab/releases/processing-fix-20261102-004500`.
- 소스: `/home/ubuntu/upscale-api-sources/processing-fix-20261102-004500`.

## 수정 내용

웹의 과거 갤러리 게시물 가져오기가 S3 서명 URL을 브라우저에서 직접 읽어 다시 업로드하던 경로를 제거했다. 이미 존재하던 `GalleryImportService`를 DI와 인증된 `POST /api/gallery/{id}/import`에 연결했다. 내 이미지 저장과 바로 적용은 같은 서버 복사 경로를 사용한다. 서버에서 공개 상태와 소유권을 확인하고 현재 계정의 독립된 비공개 프로젝트를 생성한다. 가져오기는 다운로드 횟수를 증가시키지 않는다.

Live 2D 캐릭터 처리는 대기·추론 진행률을 보고하지 않아 원격 추론 중에도 15%로 표시됐다. 요청별 실행 제한과 TTL을 API 설정(운영 2400초)에 맞췄고, GPU 대기·파츠 생성·결과 검증 단계를 표시한다. 작업별 S3 metadata와 ZIP을 통해 재시작 시 같은 원격 작업을 복구하고 원래 제한 시간을 유지한다. 제한 시간 초과 시 원격 취소를 시도하고 안전한 오류 문구를 표시한다.

배경분리 실제 검증에서는 기존 L4/A5000 워커가 모두 `THROTTLED`로 바뀌고 작업이 `IN_QUEUE`에 머무는 현상을 재현했다. `list-gpu-types`의 SERVERLESS 재고 조회에서 RTX 4090은 HIGH, L4/A5000은 LOW였다. CUDA 12.4 기반 분리 이미지와 호환되는 RTX 4090 풀을 추가했다.

- 배경분리 `byeifzkkz3m8tg`: `AMPERE_24` → `ADA_24, AMPERE_24`.
- 캐릭터 `damufrlbs3mr0a`: `AMPERE_24` → `AMPERE_24, ADA_24`.
- Blackwell MIG 제외, 이미지, CUDA 조건, 최소 0·최대 1 워커와 각 idle timeout은 유지했다.
- 배경분리는 원격 실행 중 작업 0건을 확인하고 최대 워커를 일시적으로 0 → 1로 전환해 재할당했다. 큐와 작업은 삭제하지 않았다. 이후 RTX 4090 워커가 생성됐고 THROTTLED는 0이 됐다.
- 조회 시 실행 단가: 기존 풀 $0.75/시간, RTX 4090 $1.34/시간. 상시 실행 워커는 추가하지 않았다.

## 결제 복구 확인

배포 전 운영 소스와 결제·구독·토큰 관련 C# 파일 37개가 모두 일치했다. 결제 웹 UI `Billing.tsx`의 SHA-256도 운영 복구본과 일치했다 (`1df2734e769116121d4d35db2aa2b4702ba710354a09457300f9e688d2587dae`). 현재 월간 토큰 정책과 결제 복구 코드를 보존했다.

토스 클라이언트·시크릿 키는 기존 테스트 모드다. 초기 청구 기능은 켜져 있고 자동 갱신 청구·구독 복구·구독 만료 기능의 기존 설정은 꺼져 있다. 설정을 변경하거나 실제 외부 결제를 요청하지 않았다. 초기 점검에서 미지급 확인 중 주문·예약 토큰은 각각 0건이었고, 최근 24시간 결제 복구 오류 로그도 0건이었다.

전용 계정의 실제 PostgreSQL 데이터로 합성된 결제 완료 결과를 정산해 10개 구매 토큰 지급과 중복 지급 차단을 확인했다. 토큰 예약 후 완료·환불을 각각 반복해 중복 처리 방지를 검증하고 해당 트랜잭션은 롤백했다. 실제 사용자 주문이나 잔액은 변경하지 않았다.

## 검증

- 배포 소스에서 백엔드 249개, 웹 단위 검사 102개 통과. Linux API publish와 TypeScript/Vite 클라우드 빌드 성공.
- 실제 HTTPS 회귀 검사 14개 통과. 공개 웹 파일 9개의 SHA-256이 새 산출물과 일치했다.
- 전용 두 계정으로 갤러리 가져오기, 비공개 사본·소유권, 다운로드 횟수 유지, 공유 해제 후 가져오기 차단, 정규화 후 픽셀 일치를 확인했다.
- 공개 배경화면 결과물 가져오기도 독립된 비공개 asset과 manifest를 생성했다.
- 가상 검증 기기로 적용 명령 생성·전달·manifest 읽기·모의 에이전트 Applied 보고를 확인했다. 물리 Windows/Android 화면 변경을 실측한 검증은 아니다.
- 인증 없는 새 갤러리 가져오기 요청은 401, health는 Healthy다.
- 결제 상품 5개·Free 지갑·구독 상태 API 정상, pending migration 0개.
- 기존 Windows·Android 1.2.2 설치 파일의 다운로드와 크기·해시를 유지했다. 새 EXE/APK는 생성하지 않았다.

실제 배경분리 작업 `3b64ce50-70bf-4791-a4c5-7040e1a3dda7`은 **Completed / 100%**, 레이어 2개를 반환했다. 실제 Live 2D 작업 `eb86e184-3908-4767-8dd2-bd7ba058d922`도 **Completed / 100%**, 레이어 7개를 반환했다. 두 작업 모두 각 레이어의 인증된 content API를 읽어 PNG 형식을 확인했다. 최종 endpoint-health 재조회에서 두 endpoint 모두 `inQueue: 0`, `inProgress: 0`, `unhealthy: 0`이었다.

전용 계정 2개와 해당 계정의 합성 주문·지갑·기기·적용 기록·프로젝트 및 S3 파일을 정리하고 남은 검증 계정 0개를 확인했다. 실제 사용자 계정·주문·적용 내역은 정리 대상에 포함하지 않았다.

## 백업과 복구

배포 전 API·웹·환경설정·PostgreSQL custom dump를 백업하고 DB 백업 목록을 검증했다. API는 활성 처리 작업이 없을 때 전환했다. DB migration·데이터 초기화·결제 설정·Nginx·CI/CD는 변경하지 않았다. 기존 미커밋 `CropEditor.tsx`, `ImageInformation.tsx`와 미추적 문서는 포함하지 않았다.

- 백업: `/home/ubuntu/deploy-backups/processing-fix-20261102-004500`.
- 로컬 도구·검증 산출물: `tmp/deploy/processing-fix-20261102/`.
- 서버 검증 기록: 같은 백업 폴더의 `verification/` (공개 파일 해시, 코드 대조, 기능 검증 결과, 결제 최종 점검, 릴리스 확인).
- 기존 웹/API 복구: 활성 처리 작업이 없고 현재 릴리스가 동일한지 확인한 뒤 `sudo -n python3 /home/ubuntu/deploy-backups/processing-fix-20261102-004500/rollback.py`.

위 복구 도구는 웹/API만 되돌린다. GPU 선택의 이전 값은 두 endpoint 모두 `AMPERE_24`이며 기존 제외 목록을 함께 유지해야 한다. 공급자 설정은 앱 릴리스와 별개다.
