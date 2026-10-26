# Upscale Lab

Upscale Lab은 기존 C# ASP.NET Core 8 백엔드, PostgreSQL, 인증, S3와 Flutter/Kotlin 코드를 재사용하며 웹 중심의 이미지 제작·기기 배경화면 관리 서비스로 전환하고 있습니다. 웹 화면은 TypeScript·HTML·CSS로 개발하고, EXE/APK는 계정 로그인과 기기 자격을 함께 검증해 배경화면을 적용하는 구조입니다. 이미지 바이너리는 S3에, 사용자·프로젝트·레이어·화면·명령 메타데이터는 PostgreSQL(RDS)에 저장합니다.

현재 변경은 **백엔드·DB·연동 규격**을 제공합니다. 웹 UI와 새 EXE/APK는 아직 구현/배포하지 않았습니다. [전환 계획](docs/web-transition.md), [기기·웹 API](docs/agent-api.md), [클라우드 AI 담당자 계약](docs/cloud-processing.md)을 참고하세요. 클라우드 업스케일 공급자 연결은 담당 팀원이 `IProjectImageProcessor`를 구현해 연결하며, 연결 전 업스케일 요청은 503으로 거부합니다.

Android의 **Live 2D** 버튼은 일반 사진을 기기에서 **고정 배경 + 투명 인물·사물** 두 장으로 분리합니다. 미리보기에서 결과를 확인한 뒤 보관함에 저장하거나 Android 라이브 배경화면 설정으로 적용할 수 있습니다. 자이로 기울기에 따라 전경만 움직이며, 적용한 레이어는 앱 종료·기기 재시작 후에도 유지됩니다. 분리에는 Google Play 서비스 모델의 최초 다운로드가 필요합니다. 상세 동작과 검증 방법은 [Live 2D 가이드](docs/live-2d.md)를 참고하세요.

기존 .NET 계층형 API를 유지합니다. 서버의 See-through Python CLI 어댑터는 기존 레이어 분리를 위한 선택적인 호환 경로로 남겼으며, 현재 Android의 ML Kit 분리와도 별개입니다. 기존 모바일 클라이언트의 편집/적용 흐름은 후속 에이전트 전환 전까지 유지합니다.

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
  ai-worker/                    # 공식 See-through CLI/PSD 변환 어댑터
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

모바일 실행 방법은 [mobile/README.md](mobile/README.md), AI 환경 구성은 [backend/ai-worker/README.md](backend/ai-worker/README.md)를 참고하세요.

## LiveLayer 처리 흐름

1. `POST /api/projects`가 원본을 `users/{userId}/projects/{projectId}/original/`에 저장합니다.
2. `POST /api/projects/{id}/process`가 DB에 `ProcessingJob`을 만들고 인메모리 queue에 등록합니다.
3. 백그라운드 worker가 재시작 시 대기 작업을 복구하고 See-through 어댑터를 실행합니다.
4. 어댑터는 공식 `inference_psd.py --save_to_psd` 결과를 PNG 레이어와 manifest로 변환합니다.
5. 레이어는 `users/{userId}/projects/{projectId}/layers/`에 업로드되고 메타데이터는 PostgreSQL에 저장됩니다.
6. Flutter 미리보기는 레이어별 depth/movement와 필터링한 가속도 값을 결합해 parallax를 렌더링합니다.

See-through가 비활성화되었거나 GPU 환경이 준비되지 않은 경우 job은 `failed`로 기록되고 사용자에게 설정 오류를 노출합니다. HTTP 요청은 추론 완료를 기다리지 않습니다.

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
- `SeeThrough__Enabled`
- `SeeThrough__RepositoryPath`
- `SeeThrough__PythonExecutable`
- `SeeThrough__AdapterScriptPath`
- `SeeThrough__TimeoutMinutes`

전체 변수와 예시는 [backend/.env.example](backend/.env.example)에 있습니다. 운영 배포와 AWS 준비 사항은 [docs/deployment.md](docs/deployment.md)를 참고하세요.

## 현재 배포 상태

GitHub Actions 자동 배포는 아직 구성하지 않았습니다. 2026-09-28 점검 시 EC2의 기존 저장소에는 실행 서비스나 자동 배포 설정이 없었으므로, 검증되지 않은 자동 배포를 새로 활성화하지 않고 수동 `dotnet publish` + systemd 절차만 문서화했습니다.
