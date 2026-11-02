# Upscale Lab

Upscale Lab은 React·TypeScript 웹, ASP.NET Core 8 API, PostgreSQL, S3로 구성된 이미지 제작·공유·기기 배경화면 관리 서비스다. 업로드·편집·업스케일·배경 분리·Live 2D는 웹에서 사용하고 Windows/Android 연결 앱은 같은 계정으로 로그인해 기기 정보를 보고하고 OS 배경화면을 적용한다.

## 저장소

| 경로 | 역할 |
|---|---|
| backend/src | API·애플리케이션 계약·도메인·EF Core/AWS/RunPod·결제 |
| backend/tests | xUnit 서비스·HTTP·인증·처리 계약 테스트 |
| backend/tools | DB 유지보수 도구와 격리된 개발용 SeparationLab |
| web | React·TypeScript 웹과 회귀 테스트 |
| companion/windows | .NET Windows 연결 앱 |
| companion/tests | Windows 연결 앱 회귀 테스트 |
| mobile | Flutter 연결 화면과 Kotlin 배경화면·센서 서비스 |
| gpu-worker-runpod | 업스케일 및 별도 배경 분리 클라우드 워커 |
| deploy | 수동 배포 스크립트·nginx/systemd 예시 |
| docs | 현행 아키텍처·API·운영·기능 문서 |
| third_party | 재사용 코드 출처·라이선스 |

## 로컬 실행

.NET 8 SDK, PostgreSQL 14 이상(또는 Docker), Node.js/pnpm이 필요하다. SDK가 PATH에 없으면 저장소 루트 PowerShell에서 `$env:PATH = "$PWD\.dotnet;$env:PATH"`로 로컬 SDK를 추가한다.

```powershell
cd backend
dotnet restore
dotnet tool restore
dotnet user-secrets set --project src/UpscaleLab.Api "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=upscale_lab;Username=upscale_lab;Password=YOUR_LOCAL_PASSWORD"
dotnet user-secrets set --project src/UpscaleLab.Api "Jwt:Secret" "YOUR_RANDOM_SECRET_AT_LEAST_32_CHARACTERS"
dotnet user-secrets set --project src/UpscaleLab.Api "AWS:S3BucketName" "YOUR_S3_BUCKET"
dotnet user-secrets set --project src/UpscaleLab.Api "Cognito:ClientId" "YOUR_COGNITO_APP_CLIENT_ID"
dotnet user-secrets set --project src/UpscaleLab.Api "Cognito:UserPoolId" "YOUR_COGNITO_USER_POOL_ID"
dotnet ef database update --project src/UpscaleLab.Infrastructure --startup-project src/UpscaleLab.Api
dotnet run --project src/UpscaleLab.Api
```

이 migration 예시는 비어 있는 로컬 DB용이다. 운영·공유 DB는 스키마·이력·백업과 SQL을 먼저 검토한다. 선택적 Cognito ClientSecret과 AWS/RunPod 설정은 [backend/.env.example](backend/.env.example), [인증 API](docs/auth-api.md), [클라우드 처리](docs/cloud-processing.md)를 따른다. .env 파일을 쓸 때는 셸이나 설정 공급자가 환경변수로 로드해야 한다. 로컬 PostgreSQL은 backend/.env를 설정한 뒤 backend에서 docker compose up -d postgres로 실행할 수 있다.

Development Swagger는 기본 실행 프로필의 http://localhost:5080/swagger, 상태 확인은 GET /health다. 웹의 개발/실제 API 설정과 다운로드 파일 검증은 [웹 가이드](web/README.md)를 따른다.

## 검증

```powershell
cd backend
dotnet build UpscaleLab.sln
dotnet test UpscaleLab.sln
cd ../web
pnpm test
pnpm build:cloud
```

Android 검증은 [mobile/README.md](mobile/README.md), Windows 연결 앱과 설치 파일 빌드는 [연결 앱 가이드](docs/companion-apps.md)를 따른다. APK를 새로 생성할 때만 AGENTS.md의 버전·공유 서명·apksigner 검증 규칙을 적용한다.

## 구조와 운영

이미지 바이너리는 S3에 저장하고 PostgreSQL에는 메타데이터와 object key만 보관한다. 프로젝트 처리는 DB 작업 상태와 백그라운드 큐를 사용한다. 업스케일과 배경/캐릭터 분리는 RunPod endpoint를 사용하며 로컬 GPU 대체 실행은 없다. 공급자가 설정되지 않은 기능은 처리 가능으로 노출하지 않는다.

계정 소유권과 세션을 API에서 확인하고 기기 API에는 계정 JWT와 해당 기기 자격이 모두 필요하다. 비밀값·keystore·운영 설정은 Git 밖 보안 공급자에 둔다. 현재 수동 배포를 유지하며 이 저장소 정리에서 운영 서버와 DB를 변경하지 않았다.

- [아키텍처](docs/architecture.md) · [웹 전환 경계](docs/web-transition.md)
- [현재 서비스 동작](docs/service-behavior.md) · [피사체 선택](docs/subject-selection.md) · [Live 2D/레이어](docs/live-2d.md)
- [인증 API·세션](docs/auth-api.md) · [기기 API](docs/agent-api.md)
- [배포 가이드](docs/deployment.md) · [로컬 정리 결과와 삭제 목록](docs/repository-cleanup.md)
