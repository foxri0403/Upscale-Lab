# Upscale Lab 백엔드 구축 결과

기준일: 2026-09-28

인증 기능 업데이트: 2026-10-02

## 1. 현재 GitHub Repository 구조

- 원격 저장소 기본 브랜치: `main`
- 확인된 원격 브랜치: `main` 1개
- 분석 당시 최신 커밋: `05f9bd2 Initial commit`
- 분석 당시 파일: `README.md` 1개(2줄)
- 기존 backend/.NET/DB/Docker/GitHub Actions/자동 배포/.gitignore: 없음
- 작업 브랜치: 로컬 `codex/backend-setup`; `main` push 또는 force push 없음

## 2. AWS EC2 현재 상태

- Ubuntu 26.04.1 LTS, host kernel 7.0.0 AWS
- Git 2.53.0, 저장소 `/home/ubuntu/UpscaleLab`, branch `main`, remote `git@github.com:foxri0403/Upscale-Lab.git`
- 기존 .NET SDK 10.0.112 / Runtime 10.0.12 유지
- `/home/ubuntu/.dotnet8`에 ASP.NET Core Runtime 8.0.31과 .NET Runtime 8.0.31을 병렬 설치
- 사용자 quota 때문에 .NET 8 SDK 전체 설치는 실패하여 불완전 디렉터리를 정리한 뒤 Runtime만 설치
- PostgreSQL client, Docker, nginx, AWS CLI: 미설치
- Upscale Lab systemd/user service, 관련 프로세스, cron: 없음
- EC2 IAM Role: 없음
- 기존 서비스 중지, 파일 삭제, DB/S3 변경: 없음

## 3. 생성한 Backend 구조

`backend/UpscaleLab.sln` 아래 Domain/Application/Infrastructure/Api/Tests 5개 프로젝트로 구성했습니다. Controller, DTO, Entity, 서비스 계약, EF Core 접근, 인증, S3, Replicate, 설정, 예외 처리를 분리했습니다.

## 4. 설치한 NuGet Package

| 패키지 | 버전 | 용도 |
|---|---:|---|
| Microsoft.EntityFrameworkCore | 8.0.11 | ORM |
| Microsoft.EntityFrameworkCore.Design | 8.0.11 | migration 도구 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 8.0.11 | PostgreSQL provider |
| Microsoft.AspNetCore.Authentication.JwtBearer | 8.0.31 | JWT 검증 |
| BCrypt.Net-Next | 4.2.0 | 비밀번호 hash/verify |
| AWSSDK.S3 | 4.0.103.4 | S3 업로드/삭제/pre-signed URL |
| Swashbuckle.AspNetCore | 6.6.2 | Swagger/OpenAPI |
| Microsoft.EntityFrameworkCore.InMemory | 8.0.11 | 서비스 테스트 |
| xunit / runner | 2.9.3 / 3.1.4 | 테스트 |
| Microsoft.NET.Test.Sdk / coverlet | 17.14.1 / 6.0.4 | 테스트 실행/coverage 기반 |

## 5. Database ERD / Entity 관계

- User 1:N Device, Image, GalleryPost, Comment, GalleryLike
- User 1:1 UserSetting
- Image 1:N OptimizedImage, GalleryPost
- Device 1:N OptimizedImage
- GalleryPost 1:N Comment, GalleryLike
- Email/Username unique, UserSetting.UserId unique, Like(PostId, UserId) unique
- OptimizedImage(ImageId, DeviceId, Orientation) unique
- 전체 주요 entity에 UTC CreatedAt/UpdatedAt 적용

상세 Mermaid ERD는 `docs/architecture.md`에 있습니다. `InitialCreate` migration과 idempotent SQL 생성까지 검증했으며 실제 DB에는 적용하지 않았습니다.

## 6. 구현 API

- Health: `GET /health`
- Auth: `POST /api/auth/register`, `POST /api/auth/verify-email`, `POST /api/auth/resend-verification`, `POST /api/auth/login`, `GET /api/auth/me`
- Device: `GET/POST /api/devices`, `PUT/DELETE /api/devices/{id}`
- UserSetting: `GET/PUT /api/settings`
- Image: `GET/POST /api/images`, `GET/DELETE /api/images/{id}`, `POST /api/images/{id}/download-url`
- Gallery: `GET/POST /api/gallery`, `GET/DELETE /api/gallery/{id}`
- Comment: `POST /api/gallery/{id}/comments`, `DELETE /api/comments/{id}`
- Like: `POST/DELETE /api/gallery/{id}/likes`
- Download: `POST /api/gallery/{id}/download-url`
- SignalR 기반: `/hubs/notifications`

## 7. Authentication 방식

BCrypt work factor 12로 비밀번호를 저장하고 JWT HS256 access token에 UserId, Email, Username claim을 넣습니다. 신규 계정에는 Resend로 6자리 이메일 인증 코드를 보내며 인증이 완료되기 전에는 JWT를 발급하지 않습니다. 인증 코드 원문 대신 별도 secret을 사용하는 HMAC-SHA256 결과만 저장합니다. JWT secret은 설정 파일에 존재하지 않으며 32바이트 미만이면 앱 시작이 거부됩니다. 모든 사용자 소유 데이터는 서비스 쿼리에서 UserId를 함께 검사합니다.

## 8. AWS S3 연동 상태

S3 upload/delete/pre-signed URL service와 이미지 API 연결은 구현됐습니다. AWS SDK 기본 credential chain을 사용하므로 EC2 IAM Role을 권장합니다. 실제 bucket 이름과 EC2 Role이 없어 실연동 테스트는 하지 않았습니다.

## 9. RDS 연동 상태

Npgsql/EF Core/Code First/migration은 준비됐습니다. RDS endpoint 및 자격 증명이 없어 연결·migration 적용은 하지 않았습니다. 운영 적용 전 schema, `__EFMigrationsHistory`, backup/snapshot 검토가 필요합니다.

## 10. Replicate API 연동 상태

Bearer token 설정, Real-ESRGAN prediction 생성/조회 서비스는 구현했습니다. Token이 없으므로 호출하지 않았습니다. polling, 결과 S3 복사, OptimizedImage 기록을 background job으로 묶는 단계는 남아 있습니다.

## 11. EC2 실행 방법

로컬/CI에서 framework-dependent publish 후 `/home/ubuntu/UpscaleLab/backend/publish`로 업로드하고 `/home/ubuntu/.dotnet8/dotnet UpscaleLab.Api.dll`로 실행합니다. 검토 가능한 systemd 예시만 추가했으며 service를 설치·시작하지 않았습니다. 상세 절차는 `docs/deployment.md`에 있습니다.

## 12. 로컬 실행 방법

루트 `README.md`에 clone, restore, tool restore, user-secrets, migration, run, Swagger, test, Docker PostgreSQL 명령을 기록했습니다. 이 작업공간에는 검증용 .NET SDK 8.0.425를 `.dotnet`에 격리 설치했습니다.

## 13. 필요한 환경변수/Secret

- `ConnectionStrings__DefaultConnection`
- `Jwt__Secret`
- `EmailVerification__ApiKey`
- `EmailVerification__FromAddress`
- `EmailVerification__CodeHashKey`
- `AWS__Region` (기본 `ap-northeast-2`)
- `AWS__S3BucketName`
- `Replicate__ApiToken`
- `Cors__AllowedOrigins__0`부터 필요한 origin 목록
- 선택: `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`

## 14. GitHub 배포 구조

기존 workflow와 EC2 연결이 없었습니다. 사용자의 지시대로 GitHub Actions를 임의 생성하지 않았습니다. 현재는 build/test/publish 후 승인된 수동 배포 구조이며, 후속 CI/CD는 Environment 승인과 OIDC 또는 제한된 deploy key를 권장합니다.

## 15. 사용자가 제공해야 하는 AWS 정보

- RDS endpoint, port, database name, username, password, security group 연결
- S3 bucket name/region과 EC2 IAM Role 생성·연결 승인
- JWT 운영 secret
- Replicate API token
- 운영 Web origin/domain, HTTPS 종료 방식
- Secrets Manager 사용 여부와 비용/권한 승인

## 16. 아직 구현되지 않은 기능

- 실제 RDS/S3/Replicate 통합 테스트
- Replicate 비동기 작업 queue/polling/retry와 OptimizedImage 자동 기록
- SignalR 도메인 이벤트 연결과 오프라인 sync
- refresh token, password reset, API 전체 rate limiting
- 썸네일/바이러스 또는 이미지 디코딩 검증, pagination
- nginx/ALB HTTPS, 운영 systemd 활성화, GitHub Actions CI/CD
- WPF/Flutter 클라이언트 연동

## 17. 다음 개발 단계

1. AWS 정보를 확인하고 EC2 IAM Role/S3/RDS security group을 최소 권한으로 준비
2. RDS 현황과 backup을 검토하고 idempotent migration SQL 승인 후 적용
3. staging에서 Auth/Device/Image/S3 end-to-end 테스트
4. Replicate background job과 OptimizedImage API 완성
5. pagination/rate limiting/refresh token 보강
6. GitHub Actions build/test와 승인형 EC2 배포 구성

## 검증 결과

- `dotnet build -c Release`: 경고 0, 오류 0
- `dotnet test -c Release`: 6/6 통과
- API 직접 실행: `GET /health` 200 `Healthy`
- Swagger: title `Upscale Lab API`, 15개 path 생성 확인
- EF migration: 8개 table과 5개 unique index를 포함한 idempotent SQL 생성 확인
- Secret pattern 검사: 실제 key/token/private key 발견 없음
- NuGet 직접/전이 패키지 취약점 검사: 보고된 취약 패키지 없음

## 변경 파일 목록

| 파일 | 변경 이유 | 주요 내용 |
|---|---|---|
| `.gitignore` | Secret/산출물 보호 | env, production config, key, bin/obj, 로컬 SDK/cache 제외 |
| `global.json` | SDK 일관성 | .NET 8 feature-band 선택 |
| `README.md` | 개발자 온보딩 | 로컬 설정, migration, 실행, 테스트, Secret 문서화 |
| `backend/UpscaleLab.sln` | 솔루션 구성 | 4계층 + test 프로젝트 연결 |
| `backend/Directory.Build.props` | 공통 품질 설정 | net8, nullable, implicit usings, warnings-as-errors |
| `backend/Directory.Packages.props` | 패키지 중앙 관리 | 검증된 NuGet 버전 고정 |
| `backend/.config/dotnet-tools.json` | EF 도구 재현성 | dotnet-ef 8.0.11 고정 |
| `backend/.env.example` | Secret 이름 안내 | 실제 값 없는 환경변수 예시 |
| `backend/compose.yaml` | 선택적 로컬 DB | PostgreSQL 16 및 healthcheck |
| `backend/src/UpscaleLab.Domain/UpscaleLab.Domain.csproj` | Domain 프로젝트 | 독립 entity 계층 |
| `backend/src/UpscaleLab.Domain/Enums/DevicePlatform.cs` | 플랫폼 타입 | Windows/Android enum |
| `backend/src/UpscaleLab.Domain/Enums/ScreenOrientation.cs` | 방향 타입 | Unknown/Portrait/Landscape enum |
| `backend/src/UpscaleLab.Domain/Entities/BaseEntity.cs` | 공통 감사 필드 | Guid, UTC CreatedAt/UpdatedAt |
| `backend/src/UpscaleLab.Domain/Entities/User.cs` | 사용자 모델 | email/username/password hash 및 관계 |
| `backend/src/UpscaleLab.Domain/Entities/Device.cs` | 디바이스 모델 | 화면 크기/비율/방향 |
| `backend/src/UpscaleLab.Domain/Entities/Image.cs` | 원본 이미지 모델 | S3 URL/key와 메타데이터 |
| `backend/src/UpscaleLab.Domain/Entities/OptimizedImage.cs` | 최적화 이미지 모델 | Device별 크기/방향/S3 key |
| `backend/src/UpscaleLab.Domain/Entities/UserSetting.cs` | 사용자 설정 모델 | sync/dynamic orientation/auto upscale |
| `backend/src/UpscaleLab.Domain/Entities/GalleryPost.cs` | 갤러리 게시글 | 이미지/작성자/다운로드 수 |
| `backend/src/UpscaleLab.Domain/Entities/Comment.cs` | 댓글 모델 | 게시글/작성자/내용 |
| `backend/src/UpscaleLab.Domain/Entities/GalleryLike.cs` | 좋아요 모델 | 게시글/사용자 관계 |
| `backend/src/UpscaleLab.Application/UpscaleLab.Application.csproj` | Application 프로젝트 | Domain 참조 |
| `backend/src/UpscaleLab.Application/Common/AppExceptions.cs` | 오류 계약 | not-found/conflict/forbidden 등 |
| `backend/src/UpscaleLab.Application/Auth/AuthModels.cs` | 인증 DTO | register/login/user/token 응답 |
| `backend/src/UpscaleLab.Application/Auth/IAuthService.cs` | 인증 계약 | register/login/me |
| `backend/src/UpscaleLab.Application/Auth/IAuthPrimitives.cs` | 보안 계약 | password hasher/JWT 발급 추상화 |
| `backend/src/UpscaleLab.Application/Devices/DeviceModels.cs` | Device DTO | validation 포함 요청/응답 |
| `backend/src/UpscaleLab.Application/Devices/IDeviceService.cs` | Device 계약 | 계정별 CRUD |
| `backend/src/UpscaleLab.Application/Settings/UserSettingModels.cs` | 설정 DTO | sync/방향/upscale 설정 |
| `backend/src/UpscaleLab.Application/Settings/IUserSettingService.cs` | 설정 계약 | 조회/최신값 수정 |
| `backend/src/UpscaleLab.Application/Images/ImageModels.cs` | 이미지 DTO | upload/optimized/download 응답 |
| `backend/src/UpscaleLab.Application/Images/IImageService.cs` | 이미지 계약 | upload/list/detail/delete/download |
| `backend/src/UpscaleLab.Application/Storage/IStorageService.cs` | 저장소 추상화 | upload/delete/pre-signed URL |
| `backend/src/UpscaleLab.Application/Gallery/GalleryModels.cs` | 갤러리 DTO | post/detail/comment 모델 |
| `backend/src/UpscaleLab.Application/Gallery/IGalleryService.cs` | 갤러리 계약 | CRUD/comment/like/download |
| `backend/src/UpscaleLab.Application/Upscaling/IUpscaleService.cs` | AI 추상화 | prediction create/status 계약 |
| `backend/src/UpscaleLab.Infrastructure/UpscaleLab.Infrastructure.csproj` | Infrastructure 프로젝트 | EF/Npgsql/BCrypt/AWS 패키지 |
| `backend/src/UpscaleLab.Infrastructure/Database/ApplicationDbContext.cs` | DB 접근 | DbSet, 관계, index, UTC timestamp |
| `backend/src/UpscaleLab.Infrastructure/Database/Migrations/20260928095008_InitialCreate.cs` | 신규 DB schema | 8개 table/index/FK 생성·rollback |
| `backend/src/UpscaleLab.Infrastructure/Database/Migrations/20260928095008_InitialCreate.Designer.cs` | migration metadata | InitialCreate target model |
| `backend/src/UpscaleLab.Infrastructure/Database/Migrations/ApplicationDbContextModelSnapshot.cs` | EF model snapshot | 후속 migration 비교 기준 |
| `backend/src/UpscaleLab.Infrastructure/Auth/PasswordHasher.cs` | 비밀번호 보호 | BCrypt work factor 12 |
| `backend/src/UpscaleLab.Infrastructure/Auth/AuthService.cs` | 인증 로직 | 중복 검사, hash/verify, JWT, me |
| `backend/src/UpscaleLab.Infrastructure/Devices/DeviceService.cs` | Device 로직 | UserId 소유권 포함 CRUD |
| `backend/src/UpscaleLab.Infrastructure/Settings/UserSettingService.cs` | 설정 로직 | 계정별 get-or-create/update |
| `backend/src/UpscaleLab.Infrastructure/Images/ImageService.cs` | 이미지 로직 | S3 원본 저장/메타데이터/소유권 |
| `backend/src/UpscaleLab.Infrastructure/Storage/S3StorageOptions.cs` | S3 설정 | region/bucket option |
| `backend/src/UpscaleLab.Infrastructure/Storage/S3StorageService.cs` | S3 구현 | IAM chain upload/delete/pre-sign |
| `backend/src/UpscaleLab.Infrastructure/Gallery/GalleryService.cs` | 갤러리 로직 | 권한, comment, unique like, download |
| `backend/src/UpscaleLab.Infrastructure/Upscaling/ReplicateOptions.cs` | Replicate 설정 | base URL/token/model option |
| `backend/src/UpscaleLab.Infrastructure/Upscaling/ReplicateUpscaleService.cs` | AI API 구현 | prediction create/poll 응답 파싱 |
| `backend/src/UpscaleLab.Api/UpscaleLab.Api.csproj` | API 프로젝트 | JWT/Swagger/EF design 참조, user-secrets ID |
| `backend/src/UpscaleLab.Api/Configuration/JwtOptions.cs` | JWT 설정 모델 | issuer/audience/secret/expiry |
| `backend/src/UpscaleLab.Api/Authentication/JwtTokenService.cs` | JWT 발급 | HS256과 최소 claim |
| `backend/src/UpscaleLab.Api/Authentication/ClaimsPrincipalExtensions.cs` | 인증 사용자 추출 | 필수/선택 UserId parsing |
| `backend/src/UpscaleLab.Api/Middleware/ExceptionHandlingMiddleware.cs` | 안전한 오류 응답 | ProblemDetails와 내부 오류 은닉 |
| `backend/src/UpscaleLab.Api/Hubs/NotificationHub.cs` | 실시간 확장 기반 | 인증 SignalR hub |
| `backend/src/UpscaleLab.Api/Models/UploadImageForm.cs` | multipart binding | 파일과 원본 크기 validation |
| `backend/src/UpscaleLab.Api/Controllers/AuthController.cs` | Auth HTTP API | register/login/me |
| `backend/src/UpscaleLab.Api/Controllers/DevicesController.cs` | Device HTTP API | CRUD |
| `backend/src/UpscaleLab.Api/Controllers/UserSettingsController.cs` | 설정 HTTP API | get/update |
| `backend/src/UpscaleLab.Api/Controllers/ImagesController.cs` | Image HTTP API | 제한된 형식/크기의 upload와 CRUD |
| `backend/src/UpscaleLab.Api/Controllers/GalleryController.cs` | Gallery HTTP API | post/comment/like/download |
| `backend/src/UpscaleLab.Api/Controllers/CommentsController.cs` | Comment HTTP API | 권한 기반 delete |
| `backend/src/UpscaleLab.Api/Program.cs` | 앱 조립 | DI, PostgreSQL, JWT, CORS, Swagger, health, SignalR |
| `backend/src/UpscaleLab.Api/appsettings.json` | 비민감 기본 설정 | issuer/audience/region/API base URL |
| `backend/src/UpscaleLab.Api/appsettings.Development.json` | 개발 설정 분리 | localhost CORS와 개발 logging |
| `backend/src/UpscaleLab.Api/Properties/launchSettings.json` | 로컬 실행 프로필 | Development/5080/Swagger |
| `backend/tests/UpscaleLab.Tests/UpscaleLab.Tests.csproj` | 테스트 프로젝트 | xUnit/InMemory/coverage 패키지 |
| `backend/tests/UpscaleLab.Tests/AuthServiceTests.cs` | 인증 검증 | hash 저장과 잘못된 login 거부 |
| `backend/tests/UpscaleLab.Tests/DeviceServiceTests.cs` | 소유권 검증 | 타 사용자 update 차단/필터링 |
| `backend/tests/UpscaleLab.Tests/ImageServiceTests.cs` | 이미지 검증 | binary 비저장/메타데이터/소유권 |
| `docs/architecture.md` | 설계 문서 | ERD, 처리 흐름, API, 보안 경계 |
| `docs/deployment.md` | 운영 문서 | EC2 조사 결과, AWS 선행조건, 안전 배포 |
| `docs/implementation-report.md` | 작업 인수인계 | 상태/구현/검증/미완료/파일 목록 |
| `deploy/upscale-lab.service.example` | 배포 예시 | 별도 env 파일을 사용하는 hardening된 systemd unit |
