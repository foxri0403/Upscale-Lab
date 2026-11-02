# EC2 / AWS 수동 배포

웹·API·연결 앱·RunPod 워커는 각각 검증하고 수동 배포한다. 이 문서는 재사용 절차이며 과거 특정 서버 점검 결과를 현재 상태로 간주하지 않는다. 자동 배포와 CI/CD는 별도 명시 지시 없이 추가하지 않는다.

## 배포 전 확인

현재 실행 서비스·웹 symlink·API SOURCE_REVISION/SOURCE_DIRECTORY와 DB migration history를 확인한다. 기존 API/웹 릴리스와 공급자·결제·메일·S3 설정을 보존하고 실행 파일을 교체 전에 백업한다. 최신 소스와 서버가 별도로 수정된 경우 차이를 먼저 검토한다.

RDS는 대상 DB·소유자·스키마·백업과 보안 그룹을 확인한다. EC2에서만 필요한 DB 포트에 접근하도록 구성하고 API 5080 포트를 인터넷에 직접 노출하지 않는다. S3 public access block을 유지하고 제한된 IAM Role의 GetObject/PutObject/DeleteObject와 서명 URL을 사용한다. Cognito 미인증 계정 정리에는 대상 pool의 AdminGetUser/AdminDeleteUser 권한이 필요하다.

## API

개발 PC에서 .NET 8 SDK로 테스트하고 대상 Linux 실행 환경에 맞춰 publish한다. 프로젝트의 package lock/config를 사용하고 비밀값은 산출물에 넣지 않는다.

```powershell
cd backend
dotnet restore
dotnet test UpscaleLab.sln
dotnet publish src/UpscaleLab.Api -c Release -r linux-x64 --self-contained false -o ../tmp/api-publish
```

서버는 호환되는 ASP.NET Core 8 runtime이 필요하다. [systemd 예시](../deploy/upscale-lab.service.example)의 경로·서비스 계정·EnvironmentFile을 해당 환경에 맞춘다. 새 릴리스 디렉터리에 전달한 뒤 현재 경로를 전환하고 서비스를 재시작한다. GET /health, 로그인/갱신/로그아웃, 소유권, 공개 가져오기, 작업 상태, 기기 API를 확인한다.

운영 secret 파일은 제한된 권한으로 Git 밖에 두고 [설정 예시](../backend/.env.example)의 변수명을 사용한다. DB/JWT/S3/Cognito와 사용 중인 RunPod·결제 설정을 모두 유지한다. 공급자 키를 브라우저 config.js나 VITE_ 변수로 넘기지 않는다.

## DB migration

기존 migration과 ModelSnapshot은 DB 이력과 신규 DB 생성에 필요하므로 정리 대상이 아니다. migration 이름에 LocalGpu 또는 과거 기능이 있어도 삭제하지 않는다.

1. 대상 DB·소유자·기존 schema와 __EFMigrationsHistory를 확인한다.
2. 백업/스냅샷과 롤백 계획을 확인한다.
3. dotnet ef migrations script --idempotent 결과를 리뷰한다.
4. 필요한 승인과 정비 시간에만 수동 적용한다.
5. 제약·동시 요청·기존 데이터와 새 API를 확인한다.

배경 마스크는 AddSubjectMaskRefinement, 기기·화면·명령·결과물은 AddWebDeviceControl/AddCompanionInventory 등 현재 migration 체인을 따른다. 레이어 순서 편집과 선택 범위 보완은 기존 컬럼을 사용한다. 이미 운영 데이터가 생긴 테이블을 무조건 Down하거나 과거 SQL을 재실행하지 않는다.

## 웹

[웹 가이드](../web/README.md)의 build:cloud로 TypeScript·Vite·설치 파일을 검증한다. public/downloads의 manifest와 최신 EXE/APK를 새 릴리스에도 포함한다. 설치 파일의 크기·SHA-256을 빌드 전후 비교하고 다운로드가 404가 아닌지 확인한다.

[nginx 예시](../deploy/nginx-web.conf.example), [수동 설치 스크립트](../deploy/install-web.sh)를 사용한다. 웹과 /api·/hubs·/health 라우팅, 캐시 정책과 runtime config를 검토한다. CloudFront/HTTPS를 쓰는 환경은 origin과 cache invalidation을 수동 관리하고 인증서·CORS·S3 Canvas 접근을 확인한다. 자이로 권한은 HTTPS와 지원 브라우저에서 확인한다.

새 웹 경로를 전환한 뒤 기존 세션, 공유 링크/가져오기, 원본 비율, 자르기 저장, 분리·업스케일, 설치 파일 다운로드를 확인한다. 실패하면 보존한 웹/API 릴리스로 경로를 되돌리고 DB 호환 여부를 먼저 검토한다.

## 워커와 연결 앱

RunPod 모델·이미지·입출력·hash·완료 marker는 [클라우드 계약](cloud-processing.md)과 [분리 워커](../gpu-worker-runpod/layers/README.md)를 따른다. API/처리 worker 큐는 현재 단일 인스턴스 기준이다. 여러 worker 확장은 별도 lease/공유 큐 설계가 필요하다.

Windows/Android 새 배포 파일은 [연결 앱](companion-apps.md)과 [AGENTS.md](../AGENTS.md)의 버전·공유 서명 규칙을 따른다. 현재 소스 정리에서는 APK/EXE를 새로 만들거나 운영 리소스를 변경하지 않는다.
