# 클라우드 AI 담당자 연동 계약

현재 작업실은 자동 분리 후 표시한 범위를 BiRefNet_HR-matting으로 다시 추론한다. [선택 범위 보완 계약](subject-selection-additive.md)과 [운영 배포 기록](birefnet-roi-deployment-20261031.md)을 따른다. 기존 직접 알파 마스크 계약은 [호환 API 문서](subject-mask-refinement.md)에 남아 있다.

현재 업스케일 경로는 `RunPodProjectImageProcessor` → `RunPodUpscaleService` → NVIDIA CUDA 워커의 `RealESRGAN_x4plus`다. 배경 분리는 `ExistingLayerProjectProcessor` → `RunPodImageLayerProcessor` → 별도 RunPod CUDA 워커의 `BiRefNet_HR-matting` + `big-lama`다. 로컬 GPU 추론 대체 경로는 사용하지 않는다. 공급자 자격은 API 서버 보안 설정에서만 읽는다. 모델 준비·컨테이너·입출력 계약은 [분리 워커 문서](../gpu-worker-runpod/layers/README.md)를 따른다.

## 요청과 응답

`POST /api/projects/{projectId}/process`는 다음 body를 받는다. 기존 body 없는 요청은 Layers/2로 처리한다.

```json
{ "operation": "Upscale", "targetLongEdge": 2560 }
```

- operation: `Layers`, `Upscale`, `UpscaleAndLayers`.
- targetLongEdge: FHD 1920, QHD 2560, 4K 3840. 원본 비율과 세로/가로 방향을 유지하고 짧은 변은 반올림한다. FHD 1920×1080 입력에서 QHD는 2560×1440, 4K는 3840×2160이다. 1536×1024 입력의 QHD 결과는 2560×1707이다.
- 현재 이미지 이하의 목표와 이미 완료된 동일/상위 해상도의 중복 요청은 GPU 작업 생성 전에 거부한다. 작업 실행 전 실제 파일 헤더도 다시 확인한다.
- 기존 설치 앱의 `upscaleFactor: 2 | 4` 요청은 계속 지원한다. Layers에서는 배율을 적용하지 않는다. 새 웹 UI는 배율 대신 목표 해상도만 전송한다.
- 작업 시작 응답: 202와 jobId. 상태는 기존 `/processing-status`에서 읽는다.
- 결과 이미지 URL: `POST /api/projects/{projectId}/jobs/{jobId}/download-url`.
- RunPod 설정이 없으면 업스케일 작업을 만들기 전에 503을 반환한다.

`UpscaleLab.Application/Processing/ProcessingContracts.cs`의 `IProjectImageProcessor`가 공급자 경계다. 취소 토큰을 원격 요청과 polling까지 전달하고 공급자 오류 응답에 secret/원본 URL을 포함시키지 않는다.

```csharp
bool Supports(ProcessingOperation operation);
Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken cancellationToken);
```

입력은 S3 원본 경로, 결과 디렉터리, 작업 종류, 호환 배율, `JobId`, 선택한 `TargetLongEdge`와 진행률 콜백이다. 이미지 바이너리를 DB에 넣거나 클라우드 임시 URL을 최종 결과로 저장하지 않는다. `ProcessingJob.TargetLongEdge` nullable integer 컬럼은 `AddUpscaleTargetResolution` migration으로 추가한다.

현재 클라우드 워커(`gpu-worker-runpod/handler.py`)의 입력 2MB/1MP, 출력 5MB/4MP 제한을 지키기 위해 원본을 겹치는 타일로 나눠 순차 제출한다. 32px 문맥을 추가한 타일을 원격 GPU로 2×/4× 처리하고 겹치는 가장자리를 잘라 합성한다. 원격 출력 타일은 최대 1024×1024라 PNG 응답 제한을 충족한다. 전체 합성본을 Lanczos로 한 번만 조정해 선택한 긴 변과 원본 비율을 정확히 맞춘다. 모든 타일이 원격 GPU에서 성공해야 결과를 저장하며, 실패 시 브라우저 보간이나 로컬 GPU로 대체하지 않는다. 전체 요청에 600초의 기본 제한을 적용하고 타일 완료마다 진행률을 기록한다.

임의 배율에서 AI 추론 후 최종 크기를 조정하는 방식은 [Real-ESRGAN의 공식 outscale 구현](https://github.com/xinntao/Real-ESRGAN/blob/master/realesrgan/utils.py)을 참고했다. 원격 제출·상태 조회는 [RunPod의 비동기 요청 계약](https://docs.runpod.io/serverless/endpoints/send-requests)을 유지한다.

출력은 `Layers`, 선택적인 `ImagePath`, 공통 캔버스 `Width/Height`다.

| 작업 | Layers | ImagePath |
|---|---|---|
| Layers | 1~64개의 PNG | null |
| Upscale | 빈 목록 | PNG 한 장 |
| UpscaleAndLayers | 1~64개의 PNG | 업스케일 PNG 한 장 |

모든 출력은 작업 결과 디렉터리 내부의 PNG여야 하며 각 레이어는 공통 캔버스 크기와 일치해야 한다. 최대 64MP, 한 변 최대 32768px다. 해상도 요청은 실제 원본 비율에서 계산한 목표 픽셀 크기와 정확히 일치해야 하며 기존 배율 요청은 지정 배수와 일치해야 한다. LayerOrder는 0~63의 고유값이다. transform/movement는 기존 프로젝트 범위를 따른다. 배경+전경 Live 2D 결과물 생성에는 최소 2개 레이어와 Background가 필요하다. 전경 PNG의 알파를 보존하고 피사체가 가린 배경을 채운다.

`ProjectProcessingService`가 최종 PNG를 검증하고 S3에 저장한 뒤 DB를 완료 처리한다. 업스케일 뒤 배경 분리는 마지막 성공 이미지에서 시작한다. 배경 분리 뒤 업스케일은 전체 이미지의 GPU 결과를 저장하고 기존 전경 알파·레이어를 새 캔버스 크기로 리샘플링하며 위치·움직임 크기도 맞춘다. 레이어 재표본화는 새 AI 분리 추론이 아니다. 프로젝트 목록·상세의 `imageResult`는 레이어 작업 이후에도 마지막 성공 이미지의 URL·작업 ID·실제 크기를 반환한다. `features`에는 업스케일·배경 분리·저장된 Live 2D 활성 상태를 함께 반환한다. 후속 실패는 기존 성공 결과를 숨기지 않는다. 레이어 작업은 성공한 뒤 기존 레이어를 교체한다. 실패/취소 시 새 업로드를 정리한다.

`GET /api/projects/capabilities`는 인증된 사용자에게 연결된 처리 종류를 알린다. `RunPod:LayersApiKey`(생략 시 `RunPod:ApiKey`), `RunPod:LayersEndpointId`, S3 bucket이 없으면 분리 작업 생성 전에 503으로 거부한다. 업스케일의 `RunPod:EndpointId`와 배경 분리의 `RunPod:LayersEndpointId`는 서로 다른 워커 계약을 사용한다. See-through 어댑터는 운영 DI에서 제외했다. 새 endpoint와 모델은 수동으로 배포하고 설정해야 한다. 이미 분리된 프로젝트의 레이어 보존과 Live 2D 편집은 계속 사용할 수 있다.

분리는 배경 한 장과 모든 피사체를 합친 전경 한 장을 반환한다. 전경 알파를 유지하고 LaMa는 피사체 영역을 확장한 마스크로 배경을 채운다. 두 PNG가 S3에 올라간 뒤 완료 marker를 기록하며 API가 입력 hash·작업 ID·PNG·크기·전경 투명도를 다시 확인한다. `cloud-jobs/layers/{jobId}/remote.json`의 원격 ID로 서버 재시작 후 같은 작업을 조회한다. 최종 결과는 기존 사용자별 레이어 경로에 저장하고 중간 파일에는 운영자가 7일 만료 lifecycle을 수동 설정한다.

웹의 배경 분리 버튼은 기존 Layers 작업에 연결한다. Live 2D ON/OFF와 강도는 저장/기기 적용에 반영한다. 분리된 결과물은 OFF에서도 레이어를 보존하며 manifest의 `motionEnabled=false`로 센서·프레임 갱신을 중단하고 중앙 합성본을 표시한다. 저장된 결과물의 설정 변경은 `/api/wallpapers/{id}/snapshot`으로 별도 버전을 생성한다. 모바일도 같은 클라우드 처리 후 레이어만 기기에 보관하며 ML Kit을 실행하지 않는다.

움직임은 웹 `requestAnimationFrame`, Android `Choreographer`와 하드웨어 Canvas, Windows WPF의 `CompositionTarget.Rendering`으로 갱신한다. 클라우드에서 애니메이션 프레임을 계속 전송하지 않는다. 입력과 렌더링을 분리하고 경과 시간에 따라 보간한다. 60Hz 이상 화면의 전면 실행에서 60fps 이상을 목표로 하며, 실제 FPS/프레임 지연은 기기·해상도별 실측이 필요하다. 숨긴 웹 탭·OFF·화면 비표시는 갱신을 중단한다.

## 운영 경계

현재 큐는 DB 작업 상태와 단일 프로세스 채널을 이용하고 재시작 시 미완료 작업을 복구한다. API/처리 worker는 한 인스턴스로 운영한다. 클라우드 호출이 재전송될 수 있으므로 어댑터는 입력/작업 식별 기반 중복 제출 방지나 공급자 작업 ID 영속화를 구현해야 한다. 공급자 장기 작업·여러 worker로 확장할 때는 원격 작업 ID, lease, durable queue를 추가한다. 현재 구현을 다중 worker에서 중복 추론 없이 동작하는 분산 큐로 간주하지 않는다.

실제 클라우드 API 계약을 정하기 전 업체별 토큰, callback endpoint, SDK, 자동 배포는 추가하지 않는다. 공급자 통합 테스트는 테스트 전용 이미지와 자격으로 수행한다.

## RunPod 통합 이전의 브랜치 기록

2026-10-26 당시 `feature/runpod-serverless`의 `6e42cbf`를 확인했고, 이후 `feature/main-runpod-integration`의 통합 내용이 main에 반영됐다. 아래는 기존 배율 계약의 대응 기록이며 현재 연결과 해상도 모드는 위 절차를 따른다.

| 원격 작업의 필드/계약 | 이번 main의 공통 계약 |
|---|---|
| 문자열 Operation | ProcessingOperation enum |
| UpscaleFactor | UpscaleFactor 유지 |
| UpscaledObjectKey | ResultObjectKey |
| OutputWidth / OutputHeight | ResultWidth / ResultHeight |
| StartAsync의 문자열 operation/배율 인자 | ProcessProjectRequest |
| RunPodUpscaleService 직접 호출 | IProjectImageProcessor 구현 내부에서 호출 |

원격 브랜치의 `AddLocalGpuProcessing`과 이번 `AddWebDeviceControl`은 공통 job 필드를 각각 추가하므로 migration 파일을 그대로 합치지 않는다. 실제 DB migration history를 확인하고 중복 필드/스냅샷을 조정한다. 로컬 GPU 어댑터나 과거 모바일 파일을 RunPod 브랜치 병합으로 다시 복원하지 않도록 비교한다. 공급자 API 호출 결과는 위의 PNG/해상도 검증과 S3 저장 흐름을 통과시킨다.
