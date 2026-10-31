# 클라우드 배경 분리 워커

`BiRefNet_HR-matting`으로 전경의 연속 알파를 추정하고 `big-lama`로 피사체 뒤의 배경을 채운다. NVIDIA CUDA가 없거나 모델 파일이 없으면 실패한다. API 서버·Android·브라우저에서 분리 추론을 실행하는 대체 경로는 없다. 1차 출력은 배경 한 장과 모든 피사체를 합친 전경 한 장이다.

## 수동 이미지 준비

업스케일 워커와 별도의 RunPod Serverless endpoint로 운영한다. 두 모델을 함께 올릴 여유를 위해 24GB VRAM GPU에서 먼저 실측한다. 이는 검증 전 시작 구성이며 최소 메모리나 처리 시간을 보증하는 수치가 아니다.

1. 공식 [BiRefNet_HR-matting](https://huggingface.co/ZhengPeng7/BiRefNet_HR-matting) 모델과 [LaMa 소스](https://github.com/advimman/lama)의 사용할 commit SHA를 검토하고 고정한다. BiRefNet 모델 카드의 라이선스는 MIT, LaMa 저장소의 라이선스는 Apache-2.0이다. 해당 배포본의 라이선스 파일도 이미지에 보존한다.
2. 빌드 호스트에서 `pip install huggingface-hub` 후 이 폴더에서 실행한다. `BIREFNET_SHA`, `LAMA_SHA`는 검토한 40자리 commit SHA로 교체한다.

```powershell
python prepare_models.py --birefnet-revision BIREFNET_SHA --lama-revision LAMA_SHA
```

3. [LaMa 공식 README의 모델 링크](https://github.com/advimman/lama#readme)에서 받은 `big-lama` 배포본의 `config.yaml`, `models/best.ckpt`를 각각 `models/big-lama/config.yaml`, `models/big-lama/models/best.ckpt`에 둔다. 체크포인트는 신뢰한 공식 배포본만 사용하고 검토한 SHA-256을 배포 기록에 남긴다. 모델 및 소스 snapshot은 Git에서 제외되어 있다.
4. 이 폴더를 build context로 `docker build --build-arg LAMA_CHECKPOINT_SHA256=검토한64자리SHA256 -t YOUR_REGISTRY/upscalelab-layers:YOUR_TAG .`를 실행한다. 체크포인트 hash가 일치해야 빌드한다. 업스케일 컨테이너는 변경하지 않는다. 추론 시 네트워크 모델 다운로드는 금지되어 있으며, 이미지에 포함된 검토한 코드만 읽는다.
5. 이미지를 레지스트리에 수동 등록하고 RunPod queue endpoint에 연결한다. 동시 실행은 GPU당 1로 시작한다. 처음 요청은 모델을 GPU에 올리는 시간이 추가된다.
6. API의 보안 환경 설정에 새 `RunPod__LayersEndpointId`, `RunPod__LayersTimeoutSeconds=900`을 설정한 뒤 수동 배포한다. 배경 분리 전용 키는 `RunPod__LayersApiKey`에 설정한다. 생략 시 기존 `RunPod__ApiKey`를 사용하며, 이 경우 새 endpoint에 대한 추론 권한도 있어야 한다. 업스케일용 `RunPod__EndpointId`는 기존 업스케일 워커를 유지한다. Replicate 토큰은 이 워커에 필요하지 않다. 설정 전에는 capabilities의 `layers=false`와 503이 정상이다.

실제 CUDA 추론/컨테이너 기동/모델 호환성은 배포 호스트에서 검증해야 한다. 저장소 단위 테스트는 이미지 계약과 어댑터 흐름을 검증하며 모델 품질·실제 FPS의 측정값을 대신하지 않는다.

## 네트워크 계약

API가 `/run`에 다음 input을 보낸다. 각 URL은 작업별 S3 key에만 권한이 있는 짧은 수명의 presigned URL이다. AWS 키는 GPU 워커에 전달하지 않는다.

```json
{
  "operation": "layers",
  "job_id": "32자리 작업 ID",
  "input_sha256": "정규화한 입력 PNG의 SHA256",
  "image_url": "입력 PNG GET URL",
  "background_url": "배경 PNG PUT URL",
  "foreground_url": "전경 PNG PUT URL",
  "completion_url": "완료 JSON PUT URL",
  "width": 1920,
  "height": 1080
}
```

워커는 두 PNG 업로드 이후 `complete.json`을 마지막으로 기록한다. 입력 비율과 원래 캔버스를 유지한다. BiRefNet 추론은 2048², LaMa는 긴 변 최대 2048에서 실행하고 마스크 영역만 원본 캔버스에 합성한다. 마스크 밖의 배경은 원본 픽셀을 유지한다. 큰 가림 영역의 복원 결과와 4K 경계 세부 품질은 실제 이미지로 확인해야 한다.

허용 범위는 64MP, 한 변 32768px, 파일당 256MiB다. API는 입력 hash, 작업 ID, 크기, PNG 형식, 전경 투명도와 배경 불투명도를 다시 검증한다. 오류 body와 signed URL은 사용자 오류에 포함하지 않는다. 모델을 못 읽거나 CUDA 오류가 나면 작업을 실패 처리한다.

S3 `cloud-jobs/layers/{jobId}/remote.json`에 RunPod 작업 ID를 보존해 API 재시작 시 재조회한다. `complete.json`은 RunPod의 결과 조회 보존 시간 이후에도 완료된 작업을 복구하게 한다. `/run` 응답을 받은 직후 원격 ID 저장 전에 프로세스가 종료되는 극히 짧은 구간은 원자적이지 않다. 현재 단일 API worker 전제를 유지하고, 다중 worker는 lease/분산 큐가 필요하다.

`cloud-jobs/`는 중간 저장소다. 복구 여유를 고려해 7일 후 만료하는 S3 lifecycle rule을 운영자가 수동 설정한다. 최종 결과는 기존 `users/{userId}/projects/.../layers/`에 따로 저장된다. API 자격은 해당 prefix의 Get/Put 및 presign에 필요한 S3 권한을 가져야 한다. 작업 입력 URL은 재시작 후 갱신하지 않으므로 만료된 진행 중 작업은 실패로 처리하고 사용자가 새 작업으로 다시 요청한다.

## 검증

```powershell
python -m unittest discover -s . -p test_pipeline.py -v
```

배포 호스트에서는 머리카락/털/반투명 경계/가려진 복잡한 배경/세로 이미지/4K 사진으로 실제 추론을 확인한다. 두 결과가 같은 크기인지, 전경 PNG에 부드러운 알파가 있는지, 움직일 때 실루엣이 배경에 남지 않는지 확인한다.
