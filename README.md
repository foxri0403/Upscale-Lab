# Upscale Lab

기존 Upscale Lab의 ASP.NET Core 8 백엔드, PostgreSQL 스키마, 인증, S3 이미지 저장소와 갤러리를 보존하면서 AI 레이어 분해 기반 2.5D 라이브 배경화면 서비스로 확장한 저장소입니다. 이미지 바이너리는 S3에, 사용자·프로젝트·레이어 메타데이터는 PostgreSQL(RDS)에 저장합니다.

API는 기존 .NET 계층형 구조를 유지하며, 별도 로컬 GPU 컴퓨터의 Python 워커를 통해 Real-ESRGAN 업스케일링과 PartGLEE/See-through 레이어 변환을 실행할 수 있습니다. 모바일 클라이언트는 Flutter/Android Kotlin으로 구성합니다.

## 저장소 구조

```text
backend/
  UpscaleLab.sln
  src/
    UpscaleLab.Api/             # HTTP API, 인증, Swagger, 예외 처리
    UpscaleLab.Application/     # DTO 및 서비스 계약
    UpscaleLab.Domain/          # Entity와 Enum
    UpscaleLab.Infrastructure/  # EF Core, JWT, BCrypt, S3, Replicate
  tests/
    UpscaleLab.Tests/
  compose.yaml                  # 선택 사항: 로컬 PostgreSQL
  .env.example                  # 변수 이름만 포함하는 예시
  ai-worker/                    # LAN GPU HTTP 워커와 세 모델 어댑터
mobile/                         # Flutter 클라이언트 + Android WallpaperService
docs/
  architecture.md
  deployment.md
deploy/
  upscale-lab.service.example
```

## 사전 준비

- .NET 8 SDK
- Git
- PostgreSQL 14 이상 또는 Docker Desktop
- Visual Studio 2022 / VS Code / Rider 중 하나

현재 개발 PC에는 검증용 .NET 8 SDK가 저장소의 `.dotnet` 폴더에 격리 설치되어 있습니다(이 폴더는 Git에서 제외됨). 시스템 PATH에 .NET이 없다면 저장소 루트 PowerShell에서 현재 세션에만 다음처럼 추가할 수 있습니다.

```powershell
$env:PATH = "$PWD\.dotnet;$env:PATH"
dotnet --info
```

## 로컬 실행

```powershell
git clone https://github.com/foxri0403/Upscale-Lab.git
cd Upscale-Lab\backend
dotnet restore
dotnet tool restore
dotnet user-secrets set --project src/UpscaleLab.Api "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=upscale_lab;Username=upscale_lab;Password=YOUR_LOCAL_PASSWORD"
dotnet user-secrets set --project src/UpscaleLab.Api "Jwt:Secret" "YOUR_RANDOM_SECRET_AT_LEAST_32_CHARACTERS"
dotnet user-secrets set --project src/UpscaleLab.Api "Cognito:ClientId" "YOUR_COGNITO_APP_CLIENT_ID"
dotnet user-secrets set --project src/UpscaleLab.Api "Cognito:UserPoolId" "YOUR_COGNITO_USER_POOL_ID"
# App client에 secret을 생성한 경우에만 설정합니다.
dotnet user-secrets set --project src/UpscaleLab.Api "Cognito:ClientSecret" "YOUR_COGNITO_APP_CLIENT_SECRET"
dotnet ef database update --project src/UpscaleLab.Infrastructure --startup-project src/UpscaleLab.Api
dotnet run --project src/UpscaleLab.Api
```

Development 환경의 Swagger UI는 기본 실행 프로필 기준 `http://localhost:5080/swagger`에서 확인할 수 있습니다. 상태 확인은 `GET /health`입니다.

로컬 PostgreSQL을 Docker로 실행하려면 `backend/.env.example`을 `backend/.env`로 복사하고 실제 로컬 비밀번호를 입력한 뒤 다음을 실행합니다. `.env`는 Git에서 제외됩니다.

```powershell
docker compose up -d postgres
```

## 테스트

```powershell
cd backend
dotnet build UpscaleLab.sln
dotnet test UpscaleLab.sln
```

모바일 실행 방법은 [mobile/README.md](mobile/README.md), **별도 GPU 컴퓨터 설치·API 사용·테스트는 [로컬 GPU 가이드](docs/local-gpu.md)**를 참고하세요. 일반 사진은 PartGLEE 기반 시차 레이어, 캐릭터는 See-through 레이어를 사용합니다. PartGLEE 깊이/배경은 시차 효과를 위한 근사이며 실제 3D 복원이 아닙니다.

## LiveLayer 처리 흐름

1. `POST /api/projects`가 원본을 `users/{userId}/projects/{projectId}/original/`에 저장합니다.
2. `POST /api/projects/{id}/process`가 선택한 작업(`Upscale`/`Layers`/`UpscaleAndLayers`)과 엔진, 배율을 DB `ProcessingJob`에 보존하고 queue에 등록합니다. 본문 없는 기존 호출은 See-through 레이어 처리를 유지합니다.
3. 백그라운드 worker가 재시작 시 대기 작업을 복구하고 인증된 LAN GPU 워커로 원본 바이트를 전송합니다.
4. 워커가 로컬 Real-ESRGAN 및 PartGLEE/See-through 모델을 실행하고 PNG/manifest ZIP을 반환합니다.
5. API가 결과를 검증한 뒤 레이어는 `users/{userId}/projects/{projectId}/layers/`, 업스케일 PNG는 `.../upscaled/`에 업로드합니다. PostgreSQL에는 메타데이터만 저장합니다.
6. Flutter 미리보기는 레이어별 depth/movement와 필터링한 가속도 값을 결합해 parallax를 렌더링합니다.

GPU 환경이 준비되지 않으면 job은 `Failed`로 기록됩니다. 클라이언트 HTTP 요청은 추론 완료를 기다리지 않습니다. `LocalGpu:Enabled=false`이면 기존 API 컴퓨터에서의 See-through 실행 설정을 사용하며 업스케일/PartGLEE는 비활성입니다. 클라우드 GPU로 자동 전환하지 않습니다. 실행 전 새 `AddLocalGpuProcessing` migration을 로컬 DB에 적용하세요.

## EF Core Migration

새 스키마 변경은 다음 명령으로 생성합니다.

```powershell
dotnet ef migrations add ChangeName --project src/UpscaleLab.Infrastructure --startup-project src/UpscaleLab.Api --output-dir Database/Migrations
dotnet ef database update --project src/UpscaleLab.Infrastructure --startup-project src/UpscaleLab.Api
```

운영 RDS에는 현재 스키마와 백업을 확인하기 전 `database update`를 실행하지 마세요. 이 저장소의 초기 migration은 신규 데이터베이스용입니다.

## 설정과 Secret

코드는 AWS SDK 기본 자격 증명 체인을 사용합니다. EC2에서는 Access Key 파일보다 인스턴스 IAM Role을 우선합니다. 다음 값은 코드나 Git에 커밋하지 않습니다.

- `ConnectionStrings__DefaultConnection`
- `Jwt__Secret`
- `Cognito__ClientId`
- `Cognito__UserPoolId` (만료된 미인증 계정 정리에 필요)
- `Cognito__ClientSecret` (app client에 secret이 없는 경우 생략)
- `Cognito__Region` (기본 `ap-northeast-2`)
- `AWS__S3BucketName`
- `Replicate__ApiToken`
- `LocalGpu__Enabled`
- `LocalGpu__BaseUrl`
- `LocalGpu__ApiKey`
- `LocalGpu__TimeoutMinutes`
- `SeeThrough__Enabled`
- `SeeThrough__RepositoryPath`
- `SeeThrough__PythonExecutable`
- `SeeThrough__AdapterScriptPath`
- `SeeThrough__TimeoutMinutes`

전체 변수와 예시는 [backend/.env.example](backend/.env.example)에 있습니다. 운영 배포와 AWS 준비 사항은 [docs/deployment.md](docs/deployment.md)를 참고하세요.

## 현재 배포 상태

GitHub Actions 자동 배포는 아직 구성하지 않았습니다. 2026-09-28 점검 시 EC2의 기존 저장소에는 실행 서비스나 자동 배포 설정이 없었으므로, 검증되지 않은 자동 배포를 새로 활성화하지 않고 수동 `dotnet publish` + systemd 절차만 문서화했습니다.
