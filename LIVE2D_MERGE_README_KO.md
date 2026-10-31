# Upscale Lab — 메인 RunPod/Toss 배포 코드 + Live2D 기능 통합

## 병합 기준

- 기준 프로젝트: `Upscale-Lab-feature-main-runpod-toss-release(2).zip` (업로드한 최신 ZIP; 동일한 이전 ZIP과 SHA-256 일치).
- 추가 기능: 사용자의 `inho/live2d-see-through` 작업에서 발전시킨 **Live2D v3 웹 작업실** 기능 및 서버 프로젝트/파츠 처리 기능.
- 구현 출처: 이전 작업의 `UpscaleLab-live2d-studio-v3-full-project.zip`에서 기준 프로젝트와 다른 파일만 반영.
- 최신 호환성 패치: `character-rig.test.mjs`의 Node.js 26 테스트 호환 수정 포함.
- 기존 기준 프로젝트의 결제·구독·이미지 관리·기기 선택·모바일 등 Live2D와 무관한 소스는 그대로 보존.

## 추가된 주요 기능

1. 기존 웹 작업실의 **Live 2D 탭**에서 캐릭터 파츠 분리 요청 (`CharacterLayers`). 기존 배경 분리(`Layers`)는 유지.
2. 분리된 투명 레이어 자동 합성 및 원본 레이어 순서 정보 확인.
3. 레이어를 아래에서 위로 선택하는 **수동 레이어 조합/순서 저장**.
4. 얼굴·눈·머리카락의 이동/회전 기반 리깅 **웹 미리보기**, 강도·중심점 설정 및 저장.
5. 서버 작업 구분, 레이어 순서 저장 API, `SavedEditJson` 내 웹 리깅 설정 보존.

## 의도적 비포함

- 모바일/Windows 원생 Live2D 렌더러까지 새 리깅이 연결되는 것은 **아님**.
- See-through GPU worker 이미지 재빌드/재배포, 계정 환경변수, 비밀키/실서버 설정은 변경하지 않음.
- 다른 브랜치의 결제/기기/인증 코드로 기준 버전을 덮어쓰지 않음.

## 로컬 확인 명령

```cmd
cd web
corepack pnpm install --frozen-lockfile
corepack pnpm test
corepack pnpm build:cloud
```

```bash
cd backend
dotnet restore
dotnet build UpscaleLab.sln
dotnet test UpscaleLab.sln
```

## 검증과 배포 주의

- 백엔드 .NET API도 수정되어 있으므로 **웹 파일만 배포하면 캐릭터 분리/레이어 순서 저장 API는 동작하지 않을 수 있음**.
- 배포 담당자는 기준 운영 서비스의 결제/구독 및 기기 관리 기능을 보존하며 웹과 백엔드를 검증해야 함.
- 본 병합 ZIP은 **기존 서버를 직접 변경하지 않음**.
- 로컬 자동 테스트는 클라우드 통합/실제 RunPod 연결을 대체하지 못함.
- 이 ZIP 내부의 `web/` 및 `backend/`를 함께 사용. 원래 사용자 브랜치 전체를 재차 덮어쓰면 기준 코드가 손상될 수 있음.

## 파일 변화 요약

- 기존 소스 변경: 23개
- Live2D 관련 신규 파일: 17개
- 별도 병합 안내문 1개 추가
- 변경·신규 파일 목록은 `LIVE2D_MERGE_FILES.txt` 참조
