# LiveLayer

기존 Upscale Lab의 ASP.NET Core 8 백엔드, PostgreSQL 스키마, 인증, S3 이미지 저장소와 갤러리를 보존하면서 AI 레이어 분해 기반 2.5D 라이브 배경화면 서비스로 확장한 저장소입니다. 이미지 바이너리는 S3에, 사용자·프로젝트·레이어 메타데이터는 PostgreSQL(RDS)에 저장합니다.

기존 저장소가 FastAPI가 아닌 .NET 계층형 구조로 이미 구현되어 있어 API를 전면 교체하지 않았습니다. See-through AI만 공식 Python CLI를 별도 프로세스로 호출하며, 모바일 클라이언트는 Flutter/Android Kotlin으로 구성합니다.

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
