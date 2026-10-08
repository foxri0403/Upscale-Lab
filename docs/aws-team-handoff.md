# AWS 팀 배포 핸드오프

## 목표

Upscale Lab ASP.NET Core API를 AWS에서 상시 실행하고 Android 테스트 APK가 HTTPS API에 연결되도록 구성한다.

```text
Android APK -> HTTPS -> EC2 ASP.NET Core API -> RDS PostgreSQL
                                      |-----> private S3 bucket
                                      |-----> Cognito app client
                                      `-----> See-through GPU worker
```

## 2026-10-08 현재 상태

- EC2: `13.124.225.10`, Ubuntu, SSH user `ubuntu`
- EC2에 .NET 8 Runtime: `/home/ubuntu/.dotnet8/dotnet`
- 최신 backend Release bundle 업로드 완료:
  `/home/ubuntu/UpscaleLab/backend/deploy-staging-20261008/publish`
- systemd unit 스테이징 완료:
  `/home/ubuntu/UpscaleLab/backend/deploy-staging-20261008/upscale-lab.service`
- `upscale-lab` service는 아직 설치되지 않음
- EC2 IAM Role 없음
- `/etc/upscale-lab/upscale-lab.env` 없음
- nginx/Caddy/ALB 및 HTTPS 없음
- 로컬 `backend/.env`는 개발용 localhost DB이며 S3 bucket과 Replicate token이 설정되지 않음
- See-through AI는 비활성화 상태

## AWS 팀원이 확정할 값

실제 값은 Git, 메신저, 문서에 기록하지 말고 Secrets Manager 또는 EC2의 root 소유 `600` 권한 env 파일에만 저장한다.

- RDS PostgreSQL endpoint, database, username, password
- S3 bucket name and region (`ap-northeast-2` 권장)
- Cognito app client ID and optional client secret
- JWT signing secret (32 bytes 이상)
- 운영 API domain, e.g. `https://api.example.com`
- See-through AI를 돌릴 GPU host/worker 구성
- Replicate를 사용하는 기능을 활성화할 경우 API token

## IAM Role

EC2 instance profile에 전용 IAM Role을 연결한다. 액세스 키를 EC2 파일에 저장하지 않는다.

S3 권한은 사용할 bucket/prefix에 대해 다음으로 제한한다.

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"],
      "Resource": "arn:aws:s3:::YOUR_BUCKET/users/*"
    }
  ]
}
```

S3 Block Public Access는 유지한다. 앱은 API가 발급한 짧은 수명의 pre-signed URL을 사용한다.

## RDS 및 Security Group

- PostgreSQL 14 이상
- RDS inbound `5432`는 EC2 security group에서만 허용
- EC2 public inbound은 `22`(관리자 IP), `80`, `443`만 허용
- API internal port `5080`은 인터넷에 직접 공개하지 않음
- migration 적용 전 snapshot/backup과 EF migration history 확인
- 서버에 SDK가 없으므로 migration SQL은 개발 PC/CI에서 `dotnet ef migrations script --idempotent`로 생성하거나 migration bundle을 전달

## 운영 환경 파일

`/etc/upscale-lab/upscale-lab.env`:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5080
ConnectionStrings__DefaultConnection=Host=RDS_ENDPOINT;Port=5432;Database=DB_NAME;Username=DB_USER;Password=DB_PASSWORD;SSL Mode=Require;Trust Server Certificate=true
Jwt__Secret=RANDOM_SECRET_AT_LEAST_32_BYTES
AWS__Region=ap-northeast-2
AWS__S3BucketName=PRIVATE_BUCKET_NAME
Cognito__Region=ap-northeast-2
Cognito__ClientId=COGNITO_APP_CLIENT_ID
# Cognito__ClientSecret=OPTIONAL_CLIENT_SECRET
Replicate__ApiToken=OPTIONAL_REPLICATE_TOKEN
SeeThrough__Enabled=false
SeeThrough__RepositoryPath=/opt/see-through
SeeThrough__PythonExecutable=/opt/see-through/.venv/bin/python
SeeThrough__AdapterScriptPath=/home/ubuntu/UpscaleLab/backend/ai-worker/see_through_adapter.py
SeeThrough__TimeoutMinutes=30
```

```bash
sudo install -d -m 700 -o root -g root /etc/upscale-lab
sudo chown root:root /etc/upscale-lab/upscale-lab.env
sudo chmod 600 /etc/upscale-lab/upscale-lab.env
```

## Backend 활성화

스테이징 번들을 고정 배포 경로로 전환한 후 서비스를 설치한다.

```bash
sudo install -d -m 755 -o ubuntu -g ubuntu /home/ubuntu/UpscaleLab/backend/publish
sudo cp -a /home/ubuntu/UpscaleLab/backend/deploy-staging-20261008/publish/. /home/ubuntu/UpscaleLab/backend/publish/
sudo cp /home/ubuntu/UpscaleLab/backend/deploy-staging-20261008/upscale-lab.service /etc/systemd/system/upscale-lab.service
sudo systemctl daemon-reload
sudo systemctl enable --now upscale-lab
sudo systemctl status upscale-lab --no-pager
curl --fail http://127.0.0.1:5080/health
```

## HTTPS

nginx+Let's Encrypt 또는 AWS Application Load Balancer+ACM으로 TLS를 종료한다. `https://API_DOMAIN/health`가 `Healthy`를 반환하기 전에 APK 테스를 시작하지 않는다.

## 모바일 테스트

1. APK 설치
2. 로그인 화면의 **서버 주소 설정** 선택
3. `https://API_DOMAIN` 입력 (`/api` 제외)
4. 회원가입/이메일 인증/로그인
5. 이미지 업로드, S3 object 생성, 원본 다운로드 확인
6. 정적 배경화면 적용 확인
7. GPU worker가 준비된 후 라이브 레이어 처리/적용 확인

See-through가 비활성화된 상태에서는 원본 업로드·다운로드·정적 배경화면은 테스할 수 있지만 라이브 레이어 생성은 실패한다.
