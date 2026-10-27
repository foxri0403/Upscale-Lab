# Upscale Lab – 사전 학습 See-through 캐릭터 레이어 분리

## 개발 목표

- **AI 직접 학습 없음**. 공식 See-through의 공개된 사전 학습 가중치를 추론에만 사용합니다.
- 기존 업스케일링 RunPod 엔드포인트/Worker와 분리된 **전용 Serverless 엔드포인트**로 동작합니다.
- 모바일 기존 UI의 **Live 2D → 사물 / 캐릭터** 흐름을 유지합니다.
- 사물: 기존 Android ML Kit 분리 + 자이로 이동/회전(변경 없음).
- 캐릭터: 기존 프로젝트 API로 업로드하고, 별도 RunPod Worker가 `See-through → PSD → PNG 레이어`를 수행합니다.
- 고개·눈·머리카락 동작은 모바일에서 처리합니다. See-through는 자동 리깅까지 제공하지 않습니다.

## 구조

```
Flutter 캐릭터 화면
  → 기존 ASP.NET Core POST /api/projects/{id}/process (Layers)
  → RunPodPretrainedSeeThroughProcessor
  → S3 임시 입력 이미지 (서명된 GET URL)
  → RunPod See-through 전용 엔드포인트 (사전 학습 가중치로 추론)
  → PSD → 투명 PNG 레이어 + manifest.json → ZIP
  → S3 임시 결과 ZIP (서명된 PUT URL)
  → 백엔드 ZIP 검증/추출 → 기존 ProcessedLayer / S3 영구 레이어 저장
  → 모바일 캐릭터 미리보기의 자이로 개별 레이어 움직임
```

## RunPod 배포 준비

공식 See-through: https://github.com/shitagaki-lab/see-through
RunPod Serverless: https://docs.runpod.io/serverless/overview

1. See-through용 GPU 메모리 확인: 공식 기본 추론은 약 **12~16GB VRAM(1280해상도)**, 8GB 구성은 공식 양자화 버전 등 추가 최적화가 필요합니다.
2. `backend/ai-worker` 디렉터리에서 전용 Worker 이미지 빌드:

   ```bash
   docker build -f runpod-see-through/Dockerfile -t YOUR_IMAGE_TAG .
   docker push YOUR_IMAGE_TAG
   ```

3. RunPod 콘솔에서 **새 Serverless 엔드포인트**를 만들고 위 이미지 지정. 기존 업스케일러 엔드포인트를 수정하지 않습니다.
4. 사전 학습 가중치는 공식 코드가 Hugging Face에서 가져오므로, Worker 이미지/네트워크 정책에 따라 초기 다운로드와 캐시 공간을 준비합니다. **모델 가중치는 이 ZIP에 포함되지 않습니다.**
5. API 백엔드 호스트(앱이 아닌 서버)의 환경변수 또는 시크릿 저장소에 아래 설정:

   ```text
   SeeThrough__Enabled=true
   SeeThrough__Provider=RunPod
   SeeThrough__RunPodEndpointId=NEW_CHARACTER_ENDPOINT_ID
   SeeThrough__RunPodApiKey=SET_IN_SECRET_MANAGER
   SeeThrough__RunPodTimeoutSeconds=2400
   ```

   기존 `RunPod__EndpointId`, `RunPod__ApiKey`는 **업스케일러용으로 그대로 유지**합니다.
6. 기존 AWS S3 버킷 환경변수(`AWS__S3BucketName`) 및 S3 접근 권한 확인. 백엔드 IAM 역할에는 `s3:PutObject`, `GetObject`, `DeleteObject`가 필요합니다. Worker에는 별도 영구 AWS 자격증명이 필요하지 않습니다.
7. HTTPS S3 임시 URL은 업로드/다운로드만 허용되는 한시적 권한으로 처리합니다. 다른 사람에게 URL을 공유하거나 로그에 기록하지 마세요.

## API 계약

Worker는 RunPod 비동기 `/run` 호출을 받습니다.

```json
{
  "input": {
    "operation": "character_layers_pretrained",
    "input_url": "<S3 presigned GET URL>",
    "output_url": "<S3 presigned PUT URL>"
  }
}
```

반환은 `{"status":"completed","layer_count":N}`이며 이미지 바이트는 JSON에 넣지 않습니다.
S3 결과 ZIP에는 `manifest.json`, `layers/000.png` ... 가 들어갑니다.

## 중요한 구분

- 원본 See-through 공식 추론 코드(`inference/scripts/inference_psd.py`)를 실행하므로 직접 AI 학습은 필요 없습니다.
- See-through는 세부 파츠를 분리하는 도구이지 메쉬 리깅 생성기가 아닙니다.
- 기존 모바일 캐릭터 화면은 초기 리깅/미리보기이며, 다층 라이브 배경화면까지 자동 완성되는 버전은 아닙니다.
- `SeeThrough__Enabled=false`인 기본 상태에서는 기존 동작을 유지하며 GPU 서버를 호출하지 않습니다.
- 현재 업스케일러가 지원하는 RunPod 요청과 이 Worker의 요청 형식은 다릅니다. 전용 엔드포인트가 준비되기 전에는 캐릭터 분리 요청을 보내지 마세요.

## 정적·단위 검사

```bash
python -m unittest discover -s backend/ai-worker/tests -v
python -m unittest discover -s backend/ai-worker/runpod-see-through/tests -v
```

모델 다운로드 및 실제 GPU 추론은 실행 환경을 필요로 하며 위의 단위 검사 범위에 포함되지 않습니다.
