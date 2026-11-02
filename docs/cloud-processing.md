# 클라우드 AI 담당자 연동 계약

현재 업스케일 경로는 `RunPodProjectImageProcessor` → `RunPodUpscaleService` → NVIDIA CUDA 워커의 `RealESRGAN_x4plus`다. 배경 분리는 `ExistingLayerProjectProcessor` → `RunPodImageLayerProcessor` → 별도 RunPod CUDA 워커의 `BiRefNet_HR-matting` + `big-lama`다. 로컬 GPU 추론 대체 경로는 사용하지 않는다. 공급자 자격은 API 서버 보안 설정에서만 읽는다. 모델 준비·컨테이너·입출력 계약은 [분리 워커 문서](../gpu-worker-runpod/layers/README.md)를 따른다.

## 요청과 응답

`POST /api/projects/{projectId}/process`는 다음 body를 받는다. 기존 body 없는 요청은 Layers/2로 처리한다.

```json
{ "operation": "Upscale", "targetLongEdge": 2560 }
```

- operation: 현재 구현은 `Layers`, `Upscale`, `CharacterLayers`를 지원하며 설정된 종류는 capabilities에서 확인한다. `UpscaleAndLayers`는 호환 enum/응답 계약으로 남아 있지만 현재 RunPodProjectImageProcessor는 이를 처리 가능으로 노출하지 않는다.
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

현재 `RunPodProjectImageProcessor`는 `UseS3Transfer=false`일 때 원본(필요하면 전송용 PNG)과 목표 해상도를 `RunPodUpscaleService.UpscaleAsync`로 보내며 타일 합성 메서드를 호출하지 않는다. `UseS3Transfer=true`일 때는 S3 임시 입력/출력 URL과 목표 해상도를 `RunPodS3UpscaleService`로 보낸다. 두 경로 모두 성공 결과를 검증한 뒤 저장하며 실패하면 기존 결과를 보존한다.

저장소의 기본 업스케일 worker에는 입력 2MB/1MP, 출력 5MB/4MP 제한이 있다. 실제 endpoint 이미지의 버전과 목표 해상도·S3 전송 계약이 API 설정과 맞는지 수동 확인해야 한다. 이번 로컬 정리는 worker 이미지·endpoint·키를 변경하지 않는다. 로컬 GPU나 브라우저 보간으로 실패를 대체하지 않는다.

출력은 `Layers`, 선택적인 `ImagePath`, 공통 캔버스 `Width/Height`다.

| 작업 | Layers | ImagePath |
|---|---|---|
| Layers | 1~64개의 PNG | null |
| Upscale | 빈 목록 | PNG 한 장 |
| UpscaleAndLayers | 1~64개의 PNG | 업스케일 PNG 한 장 |

모든 출력은 작업 결과 디렉터리 내부의 PNG여야 하며 각 레이어는 공통 캔버스 크기와 일치해야 한다. 최대 64MP, 한 변 최대 32768px다. 해상도 요청은 실제 원본 비율에서 계산한 목표 픽셀 크기와 정확히 일치해야 하며 기존 배율 요청은 지정 배수와 일치해야 한다. LayerOrder는 0~63의 고유값이다. transform/movement는 기존 프로젝트 범위를 따른다. 배경+전경 Live 2D 결과물 생성에는 최소 2개 레이어와 Background가 필요하다. 전경 PNG의 알파를 보존하고 피사체가 가린 배경을 채운다.

`ProjectProcessingService`가 최종 PNG를 검증하고 S3에 저장한 뒤 DB를 완료 처리한다. 업스케일 뒤 배경 분리는 마지막 성공 이미지에서 시작한다. 배경 분리 뒤 업스케일은 전체 이미지의 GPU 결과를 저장하고 기존 전경 알파·레이어를 새 캔버스 크기로 리샘플링하며 위치·움직임 크기도 맞춘다. 레이어 재표본화는 새 AI 분리 추론이 아니다. 프로젝트 목록·상세의 `imageResult`는 레이어 작업 이후에도 마지막 성공 이미지의 URL·작업 ID·실제 크기를 반환한다. `features`에는 업스케일·배경 분리·저장된 Live 2D 활성 상태를 함께 반환한다. 후속 실패는 기존 성공 결과를 숨기지 않는다. 레이어 작업은 성공한 뒤 기존 레이어를 교체한다. 실패/취소 시 새 업로드를 정리한다.

`GET /api/projects/capabilities`는 인증된 사용자에게 연결된 처리 종류를 알린다. `RunPod:LayersApiKey`(생략 시 `RunPod:ApiKey`), `RunPod:LayersEndpointId`, S3 bucket이 없으면 분리 작업 생성 전에 503으로 거부한다. 업스케일의 `RunPod:EndpointId`와 배경 분리의 `RunPod:LayersEndpointId`는 서로 다른 워커 계약을 사용한다. 서버 로컬 See-through 어댑터는 제거했으며 캐릭터 전용 RunPod 프로세서는 유지한다. 새 endpoint와 모델은 수동으로 배포하고 설정해야 한다. 이미 분리된 프로젝트의 레이어 보존과 Live 2D 편집은 계속 사용할 수 있다.

분리는 배경 한 장과 모든 피사체를 합친 전경 한 장을 반환한다. 전경 알파를 유지하고 LaMa는 피사체 영역을 확장한 마스크로 배경을 채운다. 두 PNG가 S3에 올라간 뒤 완료 marker를 기록하며 API가 입력 hash·작업 ID·PNG·크기·전경 투명도를 다시 확인한다. `cloud-jobs/layers/{jobId}/remote.json`의 원격 ID로 서버 재시작 후 같은 작업을 조회한다. 최종 결과는 기존 사용자별 레이어 경로에 저장하고 중간 파일에는 운영자가 7일 만료 lifecycle을 수동 설정한다.

웹의 배경 분리 버튼은 기존 Layers 작업에 연결한다. Live 2D ON/OFF와 강도는 저장/기기 적용에 반영한다. 분리된 결과물은 OFF에서도 레이어를 보존하며 manifest의 `motionEnabled=false`로 센서·프레임 갱신을 중단하고 중앙 합성본을 표시한다. 저장된 결과물의 설정 변경은 `/api/wallpapers/{id}/snapshot`으로 별도 버전을 생성한다. 모바일도 같은 클라우드 처리 후 레이어만 기기에 보관하며 ML Kit을 실행하지 않는다.

Live 2D 캐릭터 파츠 작업(`CharacterLayers`)은 별도 See-through endpoint를 사용한다. 요청별 `policy.executionTimeout`과 `ttl`을 `SeeThrough:RunPodTimeoutSeconds`(60~3600초)에 맞춰 기본 endpoint 제한과의 불일치를 피한다. 20은 GPU 할당 대기, 45는 파츠 생성, 70~75는 결과 읽기·검증 단계다. 진행률은 실제 원격 단계이며 추론 완료율을 추정하지 않는다. 작업별 `cloud-jobs/character-layers/{jobId}/remote.json`에 원격 ID·입력 hash·endpoint·제출 시각·완료 여부를 저장한다. API 재시작은 동일 작업을 조회하고 원래 제한 시간을 유지하며, 시간 초과 시 원격 취소를 시도한다. ZIP과 완료 metadata는 결과 보존 기간이 지난 후에도 복구할 수 있게 보관한다. 운영자는 기존 photo layers와 함께 `cloud-jobs/`의 임시 파일에 7일 만료 lifecycle을 적용한다.

움직임은 웹 `requestAnimationFrame`, Android `Choreographer`와 하드웨어 Canvas, Windows 연결 앱의 시차 렌더러으로 갱신한다. 클라우드에서 애니메이션 프레임을 계속 전송하지 않는다. 입력과 렌더링을 분리하고 경과 시간에 따라 보간한다. 60Hz 이상 화면의 전면 실행에서 60fps 이상을 목표로 하며, 실제 FPS/프레임 지연은 기기·해상도별 실측이 필요하다. 숨긴 웹 탭·OFF·화면 비표시는 갱신을 중단한다.

## 운영 경계

현재 큐는 DB 작업 상태와 단일 프로세스 채널을 이용하고 재시작 시 미완료 작업을 복구한다. API/처리 worker는 한 인스턴스로 운영한다. 클라우드 호출이 재전송될 수 있으므로 어댑터는 입력/작업 식별 기반 중복 제출 방지나 공급자 작업 ID 영속화를 구현해야 한다. 공급자 장기 작업·여러 worker로 확장할 때는 원격 작업 ID, lease, durable queue를 추가한다. 현재 구현을 다중 worker에서 중복 추론 없이 동작하는 분산 큐로 간주하지 않는다.

새 공급자나 API 계약은 별도 변경으로 검토하며 자동 배포를 추가하지 않는다. 공급자 통합 테스트는 테스트 전용 이미지와 자격으로 수행한다.
