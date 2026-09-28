# Upscale Lab

Windows(WPF)와 Android(Flutter)가 공통 계정과 이미지 데이터를 사용하는 ASP.NET Core 8 Web API 백엔드입니다. 이미지 바이너리는 S3에, 사용자 및 이미지 메타데이터는 PostgreSQL(RDS)에 저장하도록 설계되어 있습니다.

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

전체 변수와 예시는 [backend/.env.example](backend/.env.example)에 있습니다. 운영 배포와 AWS 준비 사항은 [docs/deployment.md](docs/deployment.md)를 참고하세요.

## 현재 배포 상태

GitHub Actions 자동 배포는 아직 구성하지 않았습니다. 2026-09-28 점검 시 EC2의 기존 저장소에는 실행 서비스나 자동 배포 설정이 없었으므로, 검증되지 않은 자동 배포를 새로 활성화하지 않고 수동 `dotnet publish` + systemd 절차만 문서화했습니다.
