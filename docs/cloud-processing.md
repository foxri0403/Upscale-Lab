# 클라우드 AI 담당자 연동 계약

클라우드 공급자, 모델, GPU 환경 및 외부 API 연결은 담당 팀원이 구현한다. 현재 저장소 변경은 공급자에 독립적인 C# 계약, 작업 상태, 결과 검증/저장/다운로드 경로만 제공한다. 로컬 Intel GPU 러너와 미연결 LocalGpu 어댑터는 제거했다. 기존 Replicate 클라이언트는 선택적인 공급자 구현 재료로 유지한다.

## 요청과 응답

`POST /api/projects/{projectId}/process`는 다음 body를 받는다. 기존 body 없는 요청은 Layers/2로 처리한다.

```json
{ "operation": "Upscale", "upscaleFactor": 2 }
```

- operation: `Layers`, `Upscale`, `UpscaleAndLayers`.
- upscaleFactor: 2 또는 4. Layers에서는 배율을 적용하지 않는다.
- 작업 시작 응답: 202와 jobId. 상태는 기존 `/processing-status`에서 읽는다.
- 결과 이미지 URL: `POST /api/projects/{projectId}/jobs/{jobId}/download-url`.
- 미지원 공급자는 작업을 만들기 전에 503을 반환한다. 현재 기본 구현은 기존 Layers만 지원한다. 실제 업스케일 사용은 팀원의 공급자 연결 후 가능하다.

`UpscaleLab.Application/Processing/ProcessingContracts.cs`의 `IProjectImageProcessor`를 구현하고 `Program.cs`의 기본 `ExistingLayerProjectProcessor` 등록을 교체한다. 공급자 자격은 서버 보안 설정에서만 읽는다. 취소 토큰을 원격 요청과 polling까지 전달하고 공급자 오류 응답에 secret/원본 URL을 포함시키지 않는다.

```csharp
bool Supports(ProcessingOperation operation);
Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken cancellationToken);
```

입력은 API가 S3에서 작업 디렉터리로 받은 원본 경로와 결과 디렉터리, 작업 종류, 배율, 안정적인 `JobId`다. 어댑터가 원격 클라우드 API에 업로드/요청하고 완료 결과를 지정된 결과 디렉터리에 내려받아 반환한다. JobId를 원격 중복 제출 방지 키로 사용할 수 있다. 이미지 바이너리를 DB에 넣거나 클라우드의 임시 URL을 최종 결과로 저장하지 않는다.

출력은 `Layers`, 선택적인 `ImagePath`, 공통 캔버스 `Width/Height`다.

| 작업 | Layers | ImagePath |
|---|---|---|
| Layers | 1~64개의 PNG | null |
| Upscale | 빈 목록 | PNG 한 장 |
| UpscaleAndLayers | 1~64개의 PNG | 업스케일 PNG 한 장 |

모든 출력은 작업 결과 디렉터리 내부의 PNG여야 하며 각 레이어는 공통 캔버스 크기와 일치해야 한다. 최대 64MP, 한 변 최대 32768px다. 이미지 결과는 실제 입력 픽셀 크기의 지정 배율과 일치해야 한다. LayerOrder는 0~63의 고유값이다. transform/movement는 기존 프로젝트 범위를 따른다. 배경+전경 Live 2D 결과물 생성에는 최소 2개 레이어와 Background가 필요하다. 전경 PNG의 알파를 보존하고 피사체가 가린 배경을 채운다.

`ProjectProcessingService`가 최종 PNG를 검증하고 S3에 저장한 뒤 DB를 완료 처리한다. 업스케일 단독 작업은 기존 레이어를 유지하며, 레이어 작업은 성공한 뒤 기존 레이어를 교체한다. 실패/취소 시 새 업로드를 정리하고 기존 레이어를 보존한다. `ProcessingJob`에는 종류, 배율, 결과 object key, 실제 크기를 기록한다. 프런트엔드와 설치 앱은 원격 GPU 업체를 알 필요가 없다.

## 운영 경계

현재 큐는 DB 작업 상태와 단일 프로세스 채널을 이용하고 재시작 시 미완료 작업을 복구한다. API/처리 worker는 한 인스턴스로 운영한다. 클라우드 호출이 재전송될 수 있으므로 어댑터는 입력/작업 식별 기반 중복 제출 방지나 공급자 작업 ID 영속화를 구현해야 한다. 공급자 장기 작업·여러 worker로 확장할 때는 원격 작업 ID, lease, durable queue를 추가한다. 현재 구현을 다중 worker에서 중복 추론 없이 동작하는 분산 큐로 간주하지 않는다.

실제 클라우드 API 계약을 정하기 전 업체별 토큰, callback endpoint, SDK, 자동 배포는 추가하지 않는다. 공급자 통합 테스트는 테스트 전용 이미지와 자격으로 수행한다.

## 확인된 RunPod 작업 브랜치와 연결

2026-10-26 최종 원격 조회에서 `feature/runpod-serverless`의 `6e42cbf`를 확인했다. 이 브랜치는 이번 main에 병합하지 않았다. 현재 원격 작업의 `RunPodUpscaleService.UpscaleAsync`를 재사용하되, 외부 호출은 담당 팀원이 `IProjectImageProcessor` 어댑터 안에서 연결한다.

| 원격 작업의 필드/계약 | 이번 main의 공통 계약 |
|---|---|
| 문자열 Operation | ProcessingOperation enum |
| UpscaleFactor | UpscaleFactor 유지 |
| UpscaledObjectKey | ResultObjectKey |
| OutputWidth / OutputHeight | ResultWidth / ResultHeight |
| StartAsync의 문자열 operation/배율 인자 | ProcessProjectRequest |
| RunPodUpscaleService 직접 호출 | IProjectImageProcessor 구현 내부에서 호출 |

원격 브랜치의 `AddLocalGpuProcessing`과 이번 `AddWebDeviceControl`은 공통 job 필드를 각각 추가하므로 migration 파일을 그대로 합치지 않는다. 실제 DB migration history를 확인하고 중복 필드/스냅샷을 조정한다. 로컬 GPU 어댑터나 과거 모바일 파일을 RunPod 브랜치 병합으로 다시 복원하지 않도록 비교한다. 공급자 API 호출 결과는 위의 PNG/해상도 검증과 S3 저장 흐름을 통과시킨다.
