# EC2 / AWS 배포 가이드

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
- Resend API key, 인증된 발신 주소, 이메일 코드 hash key
- 운영 Web origin 목록

권장 S3 권한은 대상 bucket/prefix에 대한 `s3:PutObject`, `s3:GetObject`, `s3:DeleteObject`로 제한합니다. bucket 삭제, policy 전체 변경, public 공개는 필요하지 않습니다.

Secrets Manager를 사용할 경우 DB connection, JWT secret, Replicate token, Resend API key, 이메일 코드 hash key를 별도 secret으로 만들고 EC2 Role에 필요한 `secretsmanager:GetSecretValue`만 부여합니다. 현재 EC2에는 IAM Role이 없으므로 비용과 권한 범위를 합의하기 전에는 구성하지 않았습니다.

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
EmailVerification__ApiKey=YOUR_RESEND_API_KEY
EmailVerification__FromAddress=no-reply@YOUR_VERIFIED_DOMAIN
EmailVerification__CodeHashKey=YOUR_RANDOM_SECRET_AT_LEAST_32_CHARACTERS
AWS__Region=ap-northeast-2
AWS__S3BucketName=YOUR_S3_BUCKET
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
