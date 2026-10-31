# Upscale Lab — 기존 작업실 Live 2D 탭 통합 v3

## 기준 및 변경 범위

- 기준 코드: 사용자 제공 `Upscale-Lab-feature-main-runpod-toss-release.zip`에 이전 v2 캐릭터 분리 + 수동 레이어 조합 코드를 적용한 상태.
- 작업실 스크린샷과 동일한 **기존 3열 레이아웃**(왼쪽 도구 / 가운데 캔버스 / 오른쪽 설정 패널) 유지. 상단 메뉴·네비게이션·로그인·업스케일·결제·공유 기능의 구조 변경 없음.
- 이전 v2에서 `배경 분리` 탭 오른쪽에 놓였던 **캐릭터 파츠 분리** 버튼은 이제 **Live 2D 탭 오른쪽 패널**로 이동.
- 기존 `배경 분리` 탭은 예전과 동일하게 배경/피사체 분리를 실행(`Layers`).
- 모바일 Flutter/Kotlin 파일, GPU worker, 클라우드 배포 및 API 비밀키를 수정하지 않음.

## 웹 화면 사용 흐름

1. 왼쪽 **내 이미지**에서 새 이미지를 업로드하고 작업실을 연다.
2. 왼쪽 도구에서 **Live 2D** 탭을 클릭한다.
3. 오른쪽 `캐릭터 파츠 분리 시작` 버튼으로 별도 See-through 처리(`CharacterLayers`)를 요청한다. 기능을 사용하려면 기존 v2 안내의 서버 SeeThrough 설정이 정상이어야 한다.
4. 처리가 완료되면 가운데 캔버스에 모든 투명 파츠가 자동으로 합성되고, 우측에 분리된 레이어 목록이 쌓이는 순서대로 표시된다.
5. 겹침이 이상하면 **수동 레이어 조합**을 클릭한다. 빈 캔버스에서 **맨 아래부터 원하는 파츠를 차례로 추가**하고 최종 순서를 저장한다. 레이어 이미지 원본은 수정하지 않는다.
6. 오른쪽 **움직임 미리보기**를 켠다. 고개·눈동자·머리카락 강도, 좌우 회전 중심, 기본 흔들림 등을 설정한다. 아래의 테스트 슬라이더 또는 마우스 움직임으로 결과를 확인한다.
7. `보관함에 저장`을 누르면 레이어 순서(`layerOrder`)와 웹 리깅 설정(`rig`)이 별도 데이터로 저장된다. 레이어 순서는 기존 API를, 움직임 설정은 기존 `SavedEditJson` JSON 필드를 사용한다.

## 오른쪽 패널 구성 (기존 UI 스타일 유지)

- 캐릭터 파츠 분리 설명 및 처리 버튼
- 현재 작업 진행 상태, 서비스 연결 안내
- 분리된 레이어 목록 / 썸네일 / 앞뒤 순서
- 수동 레이어 조합 버튼 (기존 레이어 조합 팝업 재사용)
- 고개 좌우·상하 / 눈동자 좌우·상하 테스트, 얼굴 회전 중심, 머리 흔들림 및 기본 흔들림
- 기존 저장 및 내보내기 / 기기 적용 버튼 **그대로 유지**

색상·상자·버튼은 `web/src/studio.css`에 존재하는 `--line`, `--primary`, `--tint`를 재사용하고 공통 UI 테마를 변경하지 않는다.

## 구현 구분 및 제한

- **완료**: RunPod 캐릭터 분리 작업 요청, 기존 See-through 결과 자동 합성, 수동 합성 순서 저장, 웹에서 PNG 레이어 단위 머리·눈·머리카락 회전/이동 미리보기, 설정 저장/복구 모델.
- **아직 미연결**: 저장된 `rig`를 Android/Windows 실제 **움직이는 라이브 배경화면 렌더러**에 적용하는 단계. 기존 배경+피사체 Live2D와 별개이며, 캐릭터 전용 레이어에 배경 PNG가 없으면 서버는 현재 Native Live2D 에셋을 만들지 않는다. **기기 적용 버튼이 보이더라도 캐릭터 움직임까지 적용되었다고 간주하지 말 것.**
- 이것은 원본 Live2D Cubism/메쉬/오토 리깅 모델이 아니며, 이전 Flutter `HeadMotionEngine` 아이디어를 웹에 이식한 **2D 파츠 변환 기반 MVP**다.
- 몸통·의상·팔·장신구는 고정, `Face`, `Eye`, `Eyebrow`, `Hair`, `FrontHair`, `BackHair` 등 인식된 레이어만 변형한다. 범용적으로 분할 정확도를 보장하지 않으며, 장신구/드레스가 하나의 파츠에 앞뒤 섞이면 순서만으로 해결할 수 없다.
- 실제 서버 RunPod 연결, .NET 빌드, CloudFront 배포는 이 환경에서 수행하지 않았다.

## 주요 변경 파일

| 파일 | 용도 |
|---|---|
| `web/src/Studio.tsx` | Live 2D 탭으로 분리·레이어 목록·수동 조합·움직임 설정 이동 |
| `web/src/StudioCanvas.tsx` | Live 2D 탭에서 실제 PNG 레이어 합성 및 파츠별 움직임 표시 |
| `web/src/character-rig.ts` | 레이어 분류·기본 설정·움직임 목표값 계산 |
| `web/src/use-character-rig.ts` | 브라우저 프레임 갱신, 마우스·슬라이더, 머리 지연 스프링 |
| `web/src/CharacterMotionControls.tsx` | 기존 디자인의 움직임 설정 UI |
| `web/src/model.ts`, `cloud-store.ts` | 리깅 설정 타입 및 클라우드 설정 읽기/쓰기 |
| `web/src/studio.css` | 기존 색상/레이아웃 재사용 스타일 |
| `backend/src/UpscaleLab.Application/Images/ImageEditState.cs` | 기존 JSON 컬럼에 선택적 `rig` 설정 보존 |
| `web/tests/character-rig.test.mjs` | 파츠 분류, 고정 영역 보존, 동작 수치 테스트 |
| `backend/tests/UpscaleLab.Tests/CharacterRigEditTests.cs` | 리깅 설정의 기존 JSON 저장 및 유효성 테스트 |

## 테스트

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

이 작업 환경에서는 `npm --prefix web test` 결과 **81개 중 67개 통과 / 14개 기존 건너뜀 / 실패 0개**. 또한 수정된 TS/TSX 파일에 대해 TypeScript 파서 구문 검사 성공. 웹 의존성 및 .NET SDK 부재로 실제 웹 전체 빌드, 서버 컴파일·실행, 모바일 렌더링은 검증하지 못했다.

### 적용 시 주의

- **원본 최신 배포 코드만 로컬에 있는 경우**: `UpscaleLab-live2d-studio-v3-cumulative-changes.zip` 사용(기존 v2 기능 포함).
- **이전 v2 변경 파일을 이미 적용한 경우**: `UpscaleLab-live2d-studio-v3-incremental-changes.zip` 사용.
- ZIP 내부 경로를 프로젝트 루트에 맞춰 복사한 뒤, GitHub Desktop의 **Changes** 탭에서 기존 팀원 변경과 비교하여 커밋.
- `.env`, API Key, RunPod Endpoint 정보는 ZIP에 새로 기록하지 않았다. 기존 배포 값 유지.
