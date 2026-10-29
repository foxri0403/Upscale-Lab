# 클라우드 배경 분리 운영 배포 — 2026-10-28

운영 주소는 [Upscale Lab](http://13.124.225.10)이다. API와 웹은 `layers-20261028-110000`, `web-20261028-110000` 릴리스로 전환했고 실제 계정·GPU 작업 및 공개 배포 경로를 검증했다. 기존 인페인팅, FHD/QHD 입력 크기 조정, S3 업스케일 전송 코드를 보존해 통합했다. DB 스키마 변경은 수행하지 않았다.

## Runpod

`get-endpoint`, `get-template`의 배포 후 읽기 결과:

| 항목 | 값 |
|---|---|
| Endpoint | `upscalelab-layers` / `byeifzkkz3m8tg` |
| Template | `upscalelab-layers-v1.1.1` / `21x8mq6fpp` |
| 이미지 태그 | `gusrb03/upscalelab-layers:v1.1.1` |
| 실제 고정 이미지 | `gusrb03/upscalelab-layers@sha256:453a8f14c98fa294625e90390eac0789abc2317d18beba522aa48d0cd3d24d39` |
| 방식 | QUEUE, FlashBoot 사용 |
| GPU | AMPERE_24, 1 GPU/worker, Blackwell MIG 제외 |
| CUDA 호스트 | 조회 시 재고가 있는 `13.2` |
| Workers | 최소 0, 최대 1, 유휴 제한 30초 |
| 작업 실행 제한 | 900초 |
| Network volume | 없음; 모델을 이미지에 포함 |

`get-gpu-type` 카탈로그의 AMPERE_24 Serverless 요율은 **실행 중 시간당 $0.69**였다. 실제 검증 워커는 `list-endpoint-workers`에서 **NVIDIA L4 24GB / EU-RO-1**로 확인했다. 상시 최소 워커는 설정하지 않았다. FlashBoot는 기동 시간을 줄이는 보조 기능이고, 유휴 30초 동안은 워커가 유지될 수 있다. 상시 워커를 선택하면 계속 과금되므로 별도 운영 판단이 필요하다. 조회 단가를 그대로 730시간 환산하면 $503.70이며, 현재 설정은 최소 0이다. 모델을 포함한 이미지는 최초 내려받기에 시간이 들며, 이후 워커 시작 시 모델을 다시 인터넷에서 받지 않는다.

- 비동기: `https://api.runpod.ai/v2/byeifzkkz3m8tg/run`
- 동기: `https://api.runpod.ai/v2/byeifzkkz3m8tg/runsync`
- 상태: `https://api.runpod.ai/v2/byeifzkkz3m8tg/status/{job_id}`

원시 워커를 점검할 때는 [워커 계약](../gpu-worker-runpod/layers/README.md)의 요청을 `input`으로 감싼 `runpod-layer-job.json`을 준비한다. URL은 API가 발급하는 작업별 S3 임시 URL이어야 한다.

```bash
curl --request POST \
  --url https://api.runpod.ai/v2/byeifzkkz3m8tg/run \
  --header "Authorization: Bearer $RUNPOD_API_KEY" \
  --header "Content-Type: application/json" \
  --data-binary @runpod-layer-job.json
```

여기서 `$RUNPOD_API_KEY`는 배경 분리용 키를 의미한다. 키 값은 기록에 포함하지 않는다. 일반 사용은 웹의 배경 분리 버튼과 인증된 프로젝트 API를 이용한다.

작업 후 `stream-worker-logs` 시스템 로그에서 컨테이너 종료(03:19:43 UTC)와 제거(03:19:46 UTC)를 확인했다. 마지막 endpoint health의 작업 집계는 완료 2건, 실패 0건이었다.

## 설정

- `RunPod__EndpointId`: 기존 업스케일 워커 `2j7i9zd3bvmhqk`.
- `RunPod__ApiKey`: 기존 업스케일용 자격을 유지.
- `RunPod__LayersEndpointId=byeifzkkz3m8tg`.
- `RunPod__LayersApiKey`: 새 배경 분리용 자격. 생략하면 `RunPod__ApiKey`를 사용한다.
- `RunPod__LayersTimeoutSeconds=900`.
- `Replicate__ApiToken`은 이 Runpod 분리 경로에 필요하지 않다. 별도 Replicate 어댑터를 사용하는 경우에만 [API 토큰](https://replicate.com/account/api-tokens)을 발급한다.

실제 비밀 값은 서버의 `/etc/upscale-lab/upscale-lab.env`와 Git에서 제외한 로컬 설정에서 관리한다. 클라이언트와 GPU 워커에 AWS 자격을 전달하지 않는다.

## 모델과 검증

- [BiRefNet_HR-matting](https://huggingface.co/ZhengPeng7/BiRefNet_HR-matting): `5d6b6f8adcb5b417c871b1d84ceaae9871355b7f`, 2048² FP16. 가중치 파일의 텐서 원소 약 2.207억 개는 FP16 약 441MB에 해당하며, 실제 필요 VRAM은 활성화 메모리도 포함한다.
- [LaMa 소스](https://github.com/advimman/lama): `786f5936b27fb3dacd2b1ad799e4de968ea697e7`, 공식 README가 연결한 big-lama 배포본. 체크포인트 SHA256 `fccb7adffd53ec0974ee5503c3731c2c2f1e7e07856fd9228cdcc0b46fd5d423`.
- BiRefNet MIT 고지와 LaMa 라이선스를 이미지에 보존했다. LaMa의 generator 설정 참조값은 OmegaConf로 해석하며, 빌드 단계에서 실제 모델 설정을 검증한다.
- PyTorch `2.5.1+cu124`, torchvision `0.20.1+cu124`. 로컬에서는 라이브러리·설정만 검증했고 추론은 Runpod CUDA에서 실행했다.

`get-job-status`로 확인한 직접 GPU 검증:

| 항목 | 결과 |
|---|---|
| Runpod Job | `08283710-c5d8-4ed6-adcc-1f5db6f40678-u2` |
| 결과 | COMPLETED |
| 대기 / 실행 | 8.191초 / 16.138초 |
| 이미지 | 비개인 테스트 이미지, 1536×1024 |
| 출력 | 원본 캔버스의 불투명 배경 + RGBA 전경 |
| 부드러운 알파 | 62,927픽셀 |
| 투명 픽셀 | 1,249,415픽셀 |
| 마스크 외 배경 | 원본 픽셀과 동일 |

검증된 동물 피사체 전체의 알파와 복원 배경을 직접 확인했다. 이는 한 테스트 이미지의 관측값이며 모든 사진의 품질·속도 보장은 아니다.

운영 API 검증은 별도 테스트 계정 두 개로 로그인, capabilities, 이미지 업로드, 다른 소유자의 접근 거부, Layers 작업 완료, 보호된 결과 다운로드와 1536×1024/알파 검증을 수행했다. 테스트 계정과 프로젝트는 정리했다. 이후 endpoint health에서 완료 2건, 실패 0건을 확인했다. 웹 운영 점검 14개가 통과했으며 API 통합 테스트 전체 163개와 전용 키를 포함한 레이어 테스트 5개가 통과했다.

## 앱과 움직임

- Android `1.1.0+33`, Windows `1.1.0`. APK의 apksigner 검증과 보존된 1.0.2 APK의 동일 인증서 비교를 완료했다.
- [Android APK](http://13.124.225.10/downloads/Upscale_Lab-1.1.0-.apk): SHA256 `d722b815f12d86456290727368b12b1047ce7b13f4b41b78000fb2b8195fedb2`.
- [Windows 앱](http://13.124.225.10/downloads/UpscaleLab-Connect-1.1.0-win-x64.exe): SHA256 `8b9c921cc90131459b3dcd3efd5b2adb84122cbd658826ba8b08a8da692b8b2e`.
- 운영 서버의 파일 SHA256과 다운로드 manifest/HEAD/Range 검증이 통과했다.
- Android 자이로, PC 마우스, ON/OFF 및 새로고침 주기에 맞춘 프레임 갱신을 사용한다. 로컬 브라우저 RAF 측정은 약 60Hz였으며 Android/Windows 실기기 FPS는 아직 측정하지 않았다.
- 현재 주소는 기존 HTTP 테스트 주소다. 웹 자이로는 코드에서 보안 컨텍스트를 요구하므로 HTTPS 구성이 필요하며, Android 앱의 자이로와 PC 마우스 동작은 이 웹 제약과 별개다.

## 복구와 남은 운영 설정

이전 API와 웹 릴리스는 보존했다. `/home/ubuntu/deploy-backups/layers-20261028-110000`에 이전 포인터와 비공개 환경 설정을 백업했다. 전환 스크립트에는 상태 확인 실패 시 복구 절차를 포함했다. 자동 배포나 CI/CD는 추가하지 않았다.

서버 역할로 S3 lifecycle 설정을 조회했을 때 `AccessDenied`가 반환되어 만료 규칙을 변경하지 않았다. S3 관리 권한을 가진 운영자가 `cloud-jobs/layers/`에 7일 만료 규칙을 설정해야 한다. 최종 사용자 결과는 별도 `users/` 경로에 있다. 직접 GPU 테스트용 객체는 정리했으며 실제 앱 작업의 임시 marker는 복구용으로 보존된다.

기존 DB에는 `RunPodJobId` 컬럼이 있지만 해당 마이그레이션 이력은 빠져 있었다. 이번 배포에서는 DB를 변경하지 않았으며 향후 마이그레이션 전에 이 기존 차이를 정리해야 한다.
