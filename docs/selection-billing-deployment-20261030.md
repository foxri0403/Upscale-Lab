# 토큰·결제 기능과 피사체 선택의 통합 배포

- 운영 코드: `073167984d23e4cded3448ce2a1bc2f69e0cfb60`
- 배포: `selection-billing-20261030-230852`
- 전환 시각: 2026-10-30 23:35:41 KST
- 운영: <https://d3bjir1z2hduu0.cloudfront.net/>
- main 및 `gusrb03/background-separation`에 통합하고 push했다. 통합 검토 브랜치는 `gusrb03/selection-billing-release`다.

## 팀원 변경 확인과 보존

기존 운영 웹은 `token-popup-20261030-135607`, API는 `subscription-test-20261030`이었다. 웹의 SOURCE_REVISION은 이전 값을 유지하고 API에는 해당 파일이 없어, 메타데이터만으로 운영 소스를 판단하지 않았다. 운영 PDB를 읽어 `/home/ubuntu/UpscaleLab-Payment-Integration`의 원본 코드 198개를 체크섬으로 대조했고 모두 일치했다.

원격 `feature/payment-runpod-integration`의 토큰 예약·완료·환불 연결과 `feature/main-runpod-toss-release`의 최신 지갑·토스 테스트 결제·구독 UI 및 RunPod v5 변경을 병합했다. 원격에 없던 운영의 주간 토큰 갱신, 재시도 시 중복 정산 방지, 작업 삭제 시 토큰 거래 보존, 예약이 남은 임시 프로젝트 보호 로직도 보존했다. 토큰 제공 주기 표시는 기존 운영의 주간 정책에 맞췄으며 월 구독 청구 주기는 변경하지 않았다.

업스케일은 v5의 FHD/QHD/4K resolution 요청과 전체 이미지 처리 경로를 사용한다. 기존 운영의 32MiB PNG 응답 제한을 유지했다. 처리 요청·완료·실패 시 헤더의 토큰 잔액을 갱신한다. 잔액 부족은 작업 생성 없이 HTTP 400으로 반환한다. 배경 분리·피사체 마스크 보정에는 업스케일 토큰 예약을 적용하지 않는다.

토스는 기존 공개 클라이언트 키와 서버 비밀 설정을 그대로 사용하는 **테스트 결제 모드**다. 자동 청구 등의 기존 서버 설정값을 변경하지 않았다. RunPod endpoint, GPU 이미지, 운영 환경설정 및 서명된 연결 앱 설치 파일을 변경하지 않았다.

## 화면과 분리 동작

운영의 기존 작업실·토큰 지갑·결제 화면 디자인을 유지했다. 배경 분리 부분에는 사용자가 로컬에서 확인한 변경을 적용했다.

1. 위의 **배경·피사체 분리 시작**으로 최초 자동 분리를 실행한다.
2. 브러쉬·올가미로 대략 표시하고 지우개로 선택을 뺀다. 이 단계에서는 레이어를 갱신하지 않는다.
3. 아래의 **선택한 영역으로 피사체 적용**은 AI로 선택 경계만 재인식해 미리 표시한다. 이전 적용 버튼의 흰 배경·파란 글자 UI를 사용한다.
4. 결과를 확인한 뒤 위의 **배경·피사체 분리 시작**으로 실제 전경·배경 레이어를 갱신한다. 미인식 힌트가 있으면 시작을 잠그고 인식 후 다시 활성화한다.

별도의 피사체 다시 분리·수정 사항 적용 버튼, 선택/제외 모드 버튼과 브러쉬 우클릭 지우기는 제공하지 않는다. 탭 진입이나 그리기만으로 처리를 시작하지 않는다. [선택 동작 설명](smart-subject-selection.md)을 참고한다.

## 검증과 복구

백엔드 xUnit 225개, 웹 단위 검사 74개, 실제 운영 HTTPS smoke 검사 15개가 통과했다. Linux API 발행과 TypeScript/Vite cloud 빌드도 통과했다. 별도 작업 폴더의 커밋 소스로 빌드해 기존 미커밋 자르기·이미지 정보 수정은 포함하지 않았다.

운영에서는 소유자 전용 임시 계정으로 지갑·상품·구독 API, 분리 마스크 경로와 크기 검증, 잔액 부족 시 작업 생성 롤백을 확인했다. 실제 PostgreSQL 트랜잭션 안에서 예약·완료·환불의 중복 방지와 작업 삭제 후 거래 기록 보존을 확인하고 롤백했다. 이 검증에서 AI 작업과 결제를 제출하지 않았다. 사용자가 완료한 로컬 기능 테스트를 재실행하지 않았다.

DB에는 필요한 migration이 이미 모두 적용돼 있었고 pending migration은 0개였다. 추가 migration이나 데이터 초기화를 실행하지 않았다. 전환 전 웹·API·환경설정·DB custom dump를 `/home/ubuntu/deploy-backups/selection-billing-20261030-230852/`에 백업하고 무결성을 검증했다. 동일 경로의 `rollback.py`는 이전 웹·API 포인터를 복구한다. 이전 운영의 PDB와 체크섬으로 확인한 소스도 보관했다.

새 웹은 `/var/www/upscale-lab/releases/selection-billing-20261030-230852`, API는 `/home/ubuntu/upscale-api-releases/selection-billing-20261030-230852`다. 기존 번들 URL과 images/downloads를 유지했다. 임시 계정·프로젝트·등록 파일과 이번 배포의 staging·업로드·도우미 파일을 정리했다. 최종 HTTPS/API health는 Healthy, 진행 중 이미지 작업과 임시 검증 프로젝트는 0개였다.
