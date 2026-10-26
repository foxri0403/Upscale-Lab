# EC2 / AWS 배포 가이드

## PC 웹 프런트엔드 수동 배포

웹 UI는 `web/`에 추가했습니다. [웹 README](../web/README.md)의 `pnpm build:cloud`와 [프런트엔드 설치 스크립트](../deploy/install-web.sh)를 사용하면 기존 API 서비스와 DB를 유지하면서 Nginx의 같은 origin에 UI를 제공할 수 있습니다. 클라우드 smoke test는 `WEB_TEST_URL`을 지정해 실행합니다. 이 배포는 수동이며 `main` 푸시가 자동 배포를 실행하지 않습니다. 아래의 과거 백엔드/서버 기록은 해당 시점의 기록입니다.

## 웹 기기 관리 변경의 수동 적용

2026-10-26 변경은 기존 .NET 8 API에 기기/화면/적용 명령/결과물 API를 추가합니다. 웹 UI와 새 EXE/APK는 포함하지 않습니다. 공급자 연결과 운영 DB 적용도 자동으로 수행하지 않습니다.

1. `AddPersistentAuthSessions`까지 기존 migration 적용 상태와 백업을 확인합니다.
2. `processing_jobs`에서 `Status IN ('Queued', 'Processing')`인 작업이 프로젝트별 2개 이상인지 확인합니다. 있으면 작업 상태를 검토해 중복을 정리한 뒤 새 unique index를 적용합니다.
3. `AddWebDeviceControl` migration과 idempotent SQL을 리뷰합니다. 신규 테이블 4개 및 device/job 필드 추가입니다. rollback은 신규 결과물/명령 데이터를 삭제하므로 운영 데이터가 생긴 뒤 무조건 Down을 실행하지 않습니다.
4. 정비 시간에 migration을 수동 적용하고 API를 배포합니다. 기존 키가 없는 기기는 새 연동을 시작하기 전 키 발급과 로그인 절차가 필요합니다. 새 기능 사용자는 sid가 포함된 JWT를 위해 재로그인합니다.
5. 실제 PostgreSQL 제약/동시 요청, S3 사본·서명 URL·웹 origin CORS, 로그인/로그아웃 및 실제 에이전트 연동을 검증합니다.

백엔드 폴더에서 SQL 생성만 수행하는 예시입니다. 실제 DB 적용은 별도로 검토합니다.

```powershell
dotnet ef migrations script 20261013105100_AddPersistentAuthSessions 20261026110359_AddWebDeviceControl --idempotent --project src/UpscaleLab.Infrastructure --startup-project src/UpscaleLab.Api --output ../tmp/AddWebDeviceControl.sql
```

API CORS에는 실제 웹 origin을 명시합니다. 브라우저가 S3 이미지를 Canvas/WebGL로 읽어 내보낼 경우 S3 CORS에도 해당 origin의 GET을 허용합니다. bucket public access block은 유지합니다. 사본 생성은 기존 prefix 범위의 GetObject/PutObject 권한을 이용합니다.

클라우드 공급자 연결 전 Upscale/UpscaleAndLayers는 503입니다. 기존 See-through 경로는 설정한 경우 Layers만 처리합니다. 현재 처리 큐는 **API/worker 한 인스턴스** 기준이며 여러 인스턴스를 띄우려면 lease/공유 큐를 먼저 구현해야 합니다. 공급자 계약은 [cloud-processing.md](cloud-processing.md), 기기 프로토콜은 [agent-api.md](agent-api.md)를 따릅니다.

이 절차는 아래의 2026-09-28 서버 점검 기록을 갱신한 실제 운영 상태 보고가 아닙니다. 이번 개발 중 운영 서버/DB/AWS 설정은 변경하지 않았습니다.

## 2026-09-28 점검 결과

- EC2: Ubuntu 26.04.1 LTS, `/home/ubuntu/UpscaleLab`에 `main` 저장소 존재
- Git: 2.53.0, remote는 `git@github.com:foxri0403/Upscale-Lab.git`
- 기존 .NET: SDK/Runtime 10.0.112/10.0.12
- 추가 준비: `/home/ubuntu/.dotnet8`에 ASP.NET Core Runtime 8.0.31을 기존 설치와 병렬로 설치
- 미설치: PostgreSQL client, Docker, nginx, AWS CLI
- 미구성: Upscale Lab systemd service, 실행 프로세스, cron, GitHub Actions, EC2 IAM Role
- RDS/S3: endpoint/bucket/조회 권한이 없어 존재 여부를 확인할 수 없음

EC2 홈 quota 때문에 .NET 8 SDK 전체 설치는 불가능했고, 실제 실행에 필요한 Runtime만 설치했습니다. 빌드는 개발 PC 또는 CI에서 수행하고 framework-dependent publish 결과를 서버에 전달합니다.

## AWS 선행 조건

다음 값을 프로젝트 소유자가 확인해야 합니다.

- RDS PostgreSQL endpoint, port(기본 5432), database, username, password
- RDS security group에서 EC2 security group으로의 5432 inbound 허용
- S3 bucket 이름과 region
- EC2 instance profile/IAM Role 연결 여부
- Replicate API token
- 충분히 긴 JWT signing secret
- Cognito User Pool ID, app client ID와 선택적인 client secret
- 운영 Web origin 목록

권장 S3 권한은 대상 bucket/prefix에 대한 `s3:PutObject`, `s3:GetObject`, `s3:DeleteObject`로 제한합니다. bucket 삭제, policy 전체 변경, public 공개는 필요하지 않습니다.

Secrets Manager를 사용할 경우 DB connection, JWT secret, Replicate token, Cognito app client secret을 별도 secret으로 만들고 EC2 Role에 필요한 `secretsmanager:GetSecretValue`만 부여합니다. Cognito의 `SignUp`, `ConfirmSignUp`, `ResendConfirmationCode`는 공개 사용자 작업입니다. 만료된 미인증 계정 정리는 IAM 인증을 사용하는 `AdminGetUser`, `AdminDeleteUser`가 필요하므로, 대상 User Pool ARN으로 제한한 `cognito-idp:AdminGetUser`, `cognito-idp:AdminDeleteUser` 권한을 서버 Role 또는 로컬 AWS credential provider에 설정해야 합니다. 이 코드 변경은 IAM Role이나 운영 서버 설정을 자동으로 변경하지 않습니다.

## Publish

개발 PC에서:

```powershell
cd Upscale-Lab\backend
dotnet restore
dotnet test UpscaleLab.sln
dotnet publish src/UpscaleLab.Api -c Release -o publish --no-self-contained
scp -r publish ubuntu@13.124.225.10:/home/ubuntu/UpscaleLab/backend/
```

서버에서 배포 디렉터리와 runtime을 확인합니다.

```bash
/home/ubuntu/.dotnet8/dotnet --info
ls -la /home/ubuntu/UpscaleLab/backend/publish
```

## 운영 Secret 파일

`/etc/upscale-lab/upscale-lab.env`는 repository 밖에 만들고 root 소유, mode 600으로 제한합니다. 실제 값은 shell history에 남기지 않는 안전한 방법으로 입력합니다.

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5080
ConnectionStrings__DefaultConnection=Host=YOUR_DB_ENDPOINT;Port=5432;Database=YOUR_DB_NAME;Username=YOUR_DB_USER;Password=YOUR_DB_PASSWORD;SSL Mode=Require;Trust Server Certificate=true
Jwt__Secret=YOUR_RANDOM_SECRET_AT_LEAST_32_CHARACTERS
AWS__Region=ap-northeast-2
AWS__S3BucketName=YOUR_S3_BUCKET
Cognito__Region=ap-northeast-2
Cognito__ClientId=YOUR_COGNITO_APP_CLIENT_ID
Cognito__UserPoolId=YOUR_COGNITO_USER_POOL_ID
# Cognito__ClientSecret=YOUR_COGNITO_APP_CLIENT_SECRET
Replicate__ApiToken=YOUR_REPLICATE_TOKEN
SeeThrough__Enabled=false
SeeThrough__RepositoryPath=/opt/see-through
SeeThrough__PythonExecutable=/opt/see-through/.venv/bin/python
SeeThrough__AdapterScriptPath=/home/ubuntu/UpscaleLab/backend/ai-worker/see_through_adapter.py
SeeThrough__TimeoutMinutes=30
Cors__AllowedOrigins__0=https://YOUR_WEB_DOMAIN
```

현재 문서화된 EC2에는 GPU/See-through 모델 환경이 확인되지 않았으므로 기본값은 비활성화입니다. AI를 활성화하기 전에 별도 GPU host에서 공식 See-through 단일 이미지 명령을 검증하고, 모델 다운로드 용량·CUDA 호환성·약 12–16 GB VRAM 요구량을 확인하세요. API와 AI worker를 분리 배치할 경우 현재 프로세스 실행 adapter를 queue 기반 원격 worker로 교체해야 합니다.

## systemd

`deploy/upscale-lab.service.example`을 검토한 후 `/etc/systemd/system/upscale-lab.service`로 복사합니다. 처음 활성화하기 전 RDS migration 상태, 환경변수, health check를 확인해야 합니다.

```bash
sudo cp /home/ubuntu/UpscaleLab/deploy/upscale-lab.service.example /etc/systemd/system/upscale-lab.service
sudo systemctl daemon-reload
sudo systemctl enable --now upscale-lab
sudo systemctl status upscale-lab --no-pager
curl --fail http://127.0.0.1:5080/health
```

현재 서버에는 nginx가 없습니다. 도메인과 TLS 인증서가 정해진 뒤 nginx 또는 AWS Application Load Balancer를 선택해 HTTPS를 종료해야 합니다. 5080 포트를 인터넷에 직접 공개하지 않는 구성을 권장합니다.

## Migration 안전 절차

운영 RDS 정보가 제공된 뒤에도 바로 `database update`를 실행하지 않습니다.

1. DB 이름과 소유자 확인
2. 기존 schema/table 및 EF migration history 확인
3. 백업/스냅샷 확인
4. `dotnet ef migrations script --idempotent` 결과 리뷰
5. 승인 후 maintenance window에 적용

## GitHub Actions

현재 `.github/workflows`가 없고 GitHub와 EC2 사이 자동 배포 연결도 없습니다. 이후 CI/CD를 추가할 때는 최소한 build/test/publish artifact 단계를 먼저 만들고, 배포는 GitHub Environment 승인과 OIDC 또는 제한된 deploy key를 사용해야 합니다. `main` 직접 push만으로 운영 배포가 실행되도록 구성하지 않습니다.
