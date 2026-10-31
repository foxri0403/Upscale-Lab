> **이 문서는 v2 당시의 위치 설명입니다.** 최신 v3에서는 캐릭터 분리 기능이 `배경 분리` 탭에서 **Live 2D 탭**으로 이동했습니다. `LIVE2D_STUDIO_INTEGRATION_KO.md`를 먼저 참고하세요.

# Upscale Lab — 사진 업로드 → 캐릭터 파츠 자동 분리 → 레이어 조합 v2

## 기준 파일 및 보존 범위

- 기준: 사용자 제공 `Upscale-Lab-feature-main-runpod-toss-release.zip` (2026-10-30 코드).
- 이전 전달 `UpscaleLab-layer-stack-v1-changed-files.zip`의 **자동 합성/수동 레이어 조합** 수정까지 합쳐 반영한 누적 버전.
- 기존 웹 헤더, 탐색, 로그인, 결제, 업스케일, 일반 배경 분리, Flutter/Kotlin 동작을 교체하지 않음.
- UI 추가 위치: 웹 작업실 기존 **배경 분리** 탭의 우측 설정 패널. 기존 `studio.css`의 `--line`, `--primary`, `--tint`, 글꼴, 둥근 정도, 버튼 스타일 재사용.

## 사용자 흐름

1. 웹에서 **작업실 → 새 이미지 → 파일 선택** 또는 드래그 앤 드롭으로 사진 업로드. **기존 `StudioUpload` 사용**.
2. 업로드 직후 열린 작업실에서 좌측 **배경 분리** 탭을 선택.
3. 우측 **분리 방식**에서 `배경 분리`(기존 배경+피사체) 또는 `캐릭터 파츠`(See-through 다중 파츠) 선택.
4. **캐릭터 파츠 분리 시작**을 누르면 로그인된 사용자의 프로젝트 ID로 API 작업 생성.
5. **서버 작업 큐**에서 별도 RunPod See-through Endpoint로 보내고, 분리 ZIP을 검증하여 최대 64개 PNG 레이어를 S3에 저장.
6. 작업 완료 시 자동 조회/미리보기 갱신. 기존 `LayerStackEditor`에서 **수동 레이어 조합** 누르면 아래→위 순서로 사용자가 직접 쌓고 서버에 순서를 저장할 수 있음.
7. 현재 조합 결과를 PNG로 다운로드할 수 있음. 배경 없는 캐릭터 전용 파츠는 **현재 단계에서 정지 PNG로** 보관함/기기 적용용 자산을 생성. 향후 머리·눈·머리카락 리깅/애니메이션은 포함하지 않음.

## 주요 코드 수정

- `backend/src/UpscaleLab.Domain/Enums/WallpaperEnums.cs`: 새 `ProcessingOperation.CharacterLayers` 추가. **DB의 job.Operation은 문자열 저장**이라 기존 enum 값은 유지, 신규 마이그레이션 없음.
- `backend/src/UpscaleLab.Api/Program.cs`: 별도 See-through 프로세서 HTTP 클라이언트 DI 등록.
- `backend/src/UpscaleLab.Api/Controllers/ProjectsController.cs`: 기존 capabilities 응답에 `characterLayers` 추가.
- `backend/src/UpscaleLab.Infrastructure/Processing/RunPodProjectImageProcessor.cs`: `CharacterLayers`만 전용 처리기로 라우팅; 기존 `Layers`, `Upscale` 경로 유지.
- `backend/src/UpscaleLab.Infrastructure/Processing/RunPodPretrainedSeeThroughProcessor.cs`: 설정이 완성되었을 때만 캐릭터 처리 가능으로 노출.
- `backend/src/UpscaleLab.Infrastructure/Processing/ProjectProcessingService.cs`: 캐릭터 작업 결과를 image가 아닌 레이어로 분류하고, S3/DB 순서 저장.
- `backend/src/UpscaleLab.Infrastructure/Projects/ProjectEffectResetter.cs`: 배경 분리 효과 초기화 시 캐릭터 레이어 작업 이력도 정리.
- `web/src/Studio.tsx` 및 `web/src/studio.css`: 우측 설정 패널에 분리 방식 선택 UI; 새 화면/글로벌 네비게이션 추가 없음.
- `web/src/StudioCanvas.tsx`, `web/src/LayerPreviewSelector.tsx`: 배경 없는 다중 파츠를 순서대로 보여줌.
- `web/src/model.ts`: 다운로드 시 배경 없는 레이어를 저장한 순서대로 합성해 투명 PNG 생성.
- `web/src/cloud-store.ts`: 캐릭터 결과 자산을 생성할 때 업로드 원본 대신 **레이어 합성본**을 정지 PNG로 사용.
- `web/src/StudioUpload.tsx`: 기존 파일 선택 화면 하단에 짧은 안내문만 추가.
- 기존 `LayerStackEditor.tsx`, 레이어 정렬 API 및 테스트 유지.
- 신규 `backend/tests/UpscaleLab.Tests/CharacterLayerPipelineTests.cs`: 레이어 작업에서 이미지 결과 없이 두 레이어가 DB에 저장되는 통합 테스트 추가.

## 서버 전용 설정 — 실제 연결 필수

`backend/.env.example`의 항목들은 설명용으로 비워둠. 실 운영 서버는 보안 환경변수/시크릿 저장소를 사용:

```dotenv
SeeThrough__Enabled=true
SeeThrough__Provider=RunPod
SeeThrough__RunPodEndpointId=<전용 endpoint ID>
SeeThrough__RunPodApiKey=<RunPod API Key — 절대 GitHub에 커밋 금지>
SeeThrough__RunPodTimeoutSeconds=2400
```

별도 전용 See-through worker에서 `character_layers_pretrained` operation을 받아 ZIP의 `manifest.json`과 `layers/*.png`를 S3에 PUT해야 함. RunPod의 Docker 이미지 교체·빌드는 이 파일과 무관하며 별도로 실행하지 않았음. `characterLayers` capability가 false면 웹의 캐릭터 시작 버튼을 비활성화함. **이미지 16MB 상한**은 서버 처리기가 강제하며, 일반 이미지 업로드는 기존 크기 제한을 유지함.

기존 업스케일 GPU endpoint와 일반 배경 분리 설정은 바꾸지 않았음.

## 안전 및 한계

- 계정 소유자 인증/처리 중 충돌 검사/공유 중인 프로젝트 편집 잠금은 기존 API/서비스 로직 재사용.
- 원본 PNG와 원래 레이어 파일은 변경하지 않고 수동 조합은 `layerOrder`만 업데이트.
- 캐릭터용 합성 결과에 **Background 레이어가 없는 경우 Live2D manifest의 실제 움직임은 지원하지 않음**. 다운로드/공유/기기 적용은 정지 합성 이미지로 처리함.
- 장신구 여성 캐릭터의 드레스처럼 한 레이어가 앞뒤 영역을 동시에 포함하는 문제는 단순 순서 변경만으로 해결되지 않음; 향후 수동 영역 분할 기능 대상.
- 기존 분리 결과가 배경을 갖는 경우 기존 Live2D 방식은 그대로 유지함.
- 실 운영 AWS 서버 설정/배포는 실행하지 않았고 .NET 컴파일도 이 작업 환경에서는 확인하지 못함.

## 로컬 검증

```powershell
cd web
corepack pnpm install --frozen-lockfile
corepack pnpm test
corepack pnpm build
```

```powershell
cd backend
dotnet restore
dotnet build UpscaleLab.sln
dotnet test UpscaleLab.sln
```

권장 수동 테스트: 사진 업로드 → 배경 분리 → 원래 결과가 나오나 확인 → 새 사진으로 캐릭터 파츠 선택 → 작업 완료 → 15+ 레이어 확인 → 수동 순서 편집 → 저장 → 다시 열기 → 합성 PNG 다운로드. 두 모드 사이 교차 처리, 16MB 초과, GPU 설정 미구성, 다른 사용자 프로젝트, 공유 중인 프로젝트 등도 확인.

## 수동 편집용 파츠 명칭

See-through의 `semanticLayerType`이 이미 서버 도메인 enum에 존재하면 `Eye`, `Eyebrow`, `BackHair`, `FrontHair` 등의 상세 명칭을 저장하도록 `SeeThroughManifestReader`를 개선. 수동 조합 UI는 해당 타입을 `눈`, `눈썹`, `뒷머리`, `앞머리` 등 한국어로 표시함. DB 구조 변경은 없음. 기존 구형 결과의 분류는 자동 변경되지 않으므로, 새로 분리한 작업부터 해당 이름이 적용됨.
