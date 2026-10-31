# 수동 분리·기기 관리·연결 앱 다운로드 운영 복구 배포

- 운영 전환: **2026-11-01 02:49:28 KST** (`2026-10-31T17:49:28Z`).
- 사이트: <https://d3bjir1z2hduu0.cloudfront.net/>.
- 릴리스: `restoration-20261101-023828`.
- 배포 소스: `921b40397360a2180a99d3af4f703864527ecee8`.
- API: `/home/ubuntu/upscale-api-releases/restoration-20261101-023828`.
- 웹: `/var/www/upscale-lab/releases/restoration-20261101-023828`.
- 전체 소스: `/home/ubuntu/upscale-api-sources/restoration-20261101-023828`.

## 운영 변경 보존과 복구 범위

배포 전 API는 `live2d-layer-save-fix-20261031`이었다. 실행 중인 DLL의 PDB와 별도 운영 소스 **206개**를 대조해 모두 일치함을 확인했고, 현재 main과의 차이가 수동 보정·편집 기본값 복구에 해당함을 확인했다. 운영의 Live 2D 레이어 순서 저장 수정과 새 캐릭터 파츠·리깅 기능을 유지했다.

커밋 소스만 별도 폴더로 추출해 Linux API와 cloud 웹을 빌드했다. 기존 미커밋 `CropEditor.tsx`·`ImageInformation.tsx` 변경과 미추적 문서는 이번 배포에 포함하지 않았다. 배경·피사체 수동 보정, 선택 보정 API·워커 계약, 자르기 자동 저장, 미리보기 이동과 기기 관리 화면을 반영했다.

활성 웹 경로에는 다운로드 디렉터리가 없었다. 기존 `/var/www/upscale-lab/releases/device-layout-20261028-124613/downloads`의 **Windows·Android 1.2.2**를 크기·SHA-256으로 대조한 뒤 새 릴리스에 연결했다. EXE·APK를 새로 생성하지 않았으며 기존 immutable 웹 번들도 유지했다.

RunPod `get-endpoint` 조회에서 `upscalelab-layers` (`byeifzkkz3m8tg`)의 이미지는 다음과 같았다.

```text
gusrb03/upscalelab-layers@sha256:9f956f00da6aeb5d166e2388687b94e5fe3cf613256e39cdcc2fdf02dbf4f515
```

이 이미지의 검증된 코드 층과 복구본 `handler.py`·`pipeline.py`가 일치해 이미지를 교체하지 않았다. 같은 워커로 실제 자동 분리·ROI 보정 요청을 검증했다. 최종 재조회에서도 이미지, GPU 선택, CUDA 조건, 최소 0·최대 1·idle timeout 30초 설정이 동일했다.

## 검증 결과

- 별도 커밋 소스: .NET 8 **244개**, 웹 로컬 **101개** 통과. Linux API publish와 cloud 웹 빌드 성공.
- 실제 HTTPS 운영 회귀 검사 **14개 통과**. 앞서 운영 주소가 없어 생략했던 검사까지 실행했다.
- 공개 HTML·JS·CSS·설정·메타데이터 등 **9개 파일**의 SHA-256이 배포 산출물과 일치한다. 새 번들에 수동 보정과 Live 2D 캐릭터 기능이 함께 포함됐다.
- Windows 1.2.2 EXE(83,377,369바이트), Android 1.2.2 APK(48,980,950바이트)의 다운로드 HTTP 200과 manifest·파일 크기·해시 일치를 확인했다. 누락된 다운로드는 SPA 대신 404를 반환한다.
- `/subject-selection`·`/subject-mask`의 미인증 요청은 HTTP 401이며 health는 `Healthy`다. 실제 인증 후 capabilities에서 upscale·layers·characterLayers가 모두 true였다.
- 실제 자동 분리 작업: `02aba414-1baf-4ba1-a334-ef049e13cfc4`, **Completed**.
- 실제 부분 보정 작업: `b0b997ea-0955-4a78-af4d-08a4ecfa4669`, **Completed**.
- 1536×1024 검증 사진: 추가된 전경 122,845픽셀, 표시 범위에서 제외된 배경 44,293픽셀, 새 부드러운 알파 10,332픽셀. **수정 범위 밖 변경 0, 의도하지 않은 기존 알파 감소 0**.
- 실제 타 계정 요청 404, 오래된 기준 레이어 요청 409를 확인했다.
- 검증 전용 계정 2개·프로젝트 1개와 소유한 작업 저장소 2개를 정리했다. 마지막 endpoint-health 조회에서 `inQueue: 0`, `inProgress: 0`, `unhealthy: 0`이었다.

## 백업과 운영 경계

전환 전에 웹·API 전체, 운영 소스, 설정 파일, PostgreSQL custom dump를 서버의 접근 제한된 폴더에 보관하고 DB 백업 목록을 확인했다. API는 처리 중 작업이 없을 때 재시작했다. **DB migration·데이터 초기화·결제 설정·GPU 설정·Nginx 설정·CI/CD 변경은 수행하지 않았다.** API appsettings와 서버 환경 설정의 해시도 유지됐다.

- 백업과 독립 복구 도구: `/home/ubuntu/deploy-backups/restoration-20261101-023828`.
- 서버 검증 증거: 같은 폴더의 `verification/`와 `verification.tar.gz`.
- 로컬 배포 도구·기록: `tmp/deploy/restoration-20261101/`.
- 로컬 픽셀·정리 증거: `artifacts/production-restoration-20261101/verification/`.

복구가 필요한 경우 활성 작업이 없고 현재 릴리스가 그대로임을 확인한 뒤 서버에서 다음 도구로 이전 웹·API를 함께 복원한다. DB를 되돌리지 않는다.

```bash
sudo -n python3 /home/ubuntu/deploy-backups/restoration-20261101-023828/rollback.py
```

검증용 자격은 서버의 임시 파일에만 보관했고 출력·공유하지 않았다. 검증과 데이터 정리가 끝난 후 이번 배포 staging 디렉터리를 제거했다. 운영 릴리스·소스·백업·검증 증거는 유지한다.
