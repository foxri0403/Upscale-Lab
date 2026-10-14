# Upscale Lab Backend Architecture

## 구성 원칙

- 클라이언트는 데이터베이스나 S3에 직접 접근하지 않고 API만 호출합니다.
- 원본 및 최적화 이미지 바이너리는 S3에 저장하고 PostgreSQL에는 메타데이터와 S3 object key만 저장합니다.
- 모든 사용자 소유 리소스 쿼리는 인증된 `UserId`를 조건에 포함합니다.
- 수정 가능한 데이터는 UTC `UpdatedAt`을 사용하여 향후 최신 수정 우선 동기화 정책을 적용할 수 있습니다.
- SignalR의 `/hubs/notifications` 기반만 제공하며 도메인 이벤트 연결은 후속 작업입니다.

## Entity 관계

```mermaid
erDiagram
    USER ||--o{ DEVICE : owns
    USER ||--o{ IMAGE : owns
    USER ||--|| USER_SETTING : has
    IMAGE ||--o{ OPTIMIZED_IMAGE : produces
    DEVICE ||--o{ OPTIMIZED_IMAGE : targets
    USER ||--o{ GALLERY_POST : writes
    USER ||--o{ LIVE_LAYER_PROJECT : owns
    IMAGE ||--o{ LIVE_LAYER_PROJECT : sources
    LIVE_LAYER_PROJECT ||--o{ IMAGE_LAYER : contains
    LIVE_LAYER_PROJECT ||--o{ PROCESSING_JOB : tracks
    LIVE_LAYER_PROJECT ||--o{ GALLERY_POST : publishes
    IMAGE ||--o{ GALLERY_POST : publishes
    GALLERY_POST ||--o{ COMMENT : contains
    USER ||--o{ COMMENT : writes
    GALLERY_POST ||--o{ GALLERY_LIKE : receives
    USER ||--o{ GALLERY_LIKE : creates
```

주요 제약 조건:

- `users.email` unique
- `users.username` unique
- `user_settings.user_id` unique
- `gallery_likes(gallery_post_id, user_id)` unique
- `optimized_images(image_id, device_id, orientation)` unique

## 이미지 처리 흐름

1. 인증된 클라이언트가 `POST /api/images`로 이미지와 크기 메타데이터를 전송합니다.
2. API가 S3에 원본을 저장하고 PostgreSQL에 `Image`를 기록합니다.
3. 새 처리 기능은 `POST /api/projects`로 업로드한 이미지에 `POST /api/projects/{id}/process`를 호출해 시작합니다. 원본 바이트를 LAN GPU 워커에 보내 Real-ESRGAN 업스케일, PartGLEE/See-through 레이어 변환 또는 두 작업을 실행합니다.
4. API가 PNG/manifest ZIP을 검증하고 S3에 결과를 저장합니다. `ProcessingJob`에 옵션과 업스케일 결과 키/크기를 기록하고 레이어는 `ImageLayer`로 저장합니다.
5. 클라이언트는 상태·레이어를 조회하고, jobId에 해당하는 짧은 수명의 업스케일 다운로드 URL을 발급받습니다.

기존 `/api/images`와 장치별 `OptimizedImage` 흐름은 보존합니다. 새 GPU 기능은 프로젝트 단위이며 장치별 화면 맞춤 처리는 하지 않습니다. 기존 Replicate 서비스는 새 처리 경로에서 호출하지 않습니다. [로컬 GPU 가이드](local-gpu.md)에 모델 선택, API 및 두 컴퓨터 설정이 있습니다.

## API

| Method | Path | 인증 | 용도 |
|---|---|---:|---|
| GET | `/health` | 없음 | 프로세스 상태 확인 |
| POST | `/api/auth/register` | 없음 | 회원가입, BCrypt hash, Cognito 인증 코드 발송 |
| POST | `/api/auth/verify-email` | 없음 | 6자리 이메일 인증 후 JWT 발급 |
| POST | `/api/auth/resend-verification` | 없음 | 이메일 인증 코드 재전송 |
| POST | `/api/auth/login` | 없음 | 로그인 및 JWT 발급 |
| POST | `/api/auth/refresh` | 갱신 자격 | 만료된 액세스 토큰 갱신 |
| POST | `/api/auth/logout` | 갱신 자격 | 해당 기기의 자동 로그인 자격 폐기 |
| GET | `/api/auth/me` | 필요 | 현재 사용자 조회 |
| GET/POST | `/api/devices` | 필요 | 내 디바이스 목록/등록 |
| PUT/DELETE | `/api/devices/{id}` | 필요 | 내 디바이스 수정/삭제 |
| GET/PUT | `/api/settings` | 필요 | 내 동기화/방향/업스케일 설정 |
| GET/POST | `/api/images` | 필요 | 내 이미지 목록/업로드 |
| GET/DELETE | `/api/images/{id}` | 필요 | 내 이미지 조회/삭제 |
| POST | `/api/images/{id}/download-url` | 필요 | 내 원본 pre-signed URL 발급 |
| GET/POST | `/api/gallery` | 조회 공개, 등록 필요 | 갤러리 목록/등록 |
| GET/DELETE | `/api/gallery/{id}` | 조회 공개, 삭제 필요 | 상세/본인 게시글 삭제 |
| GET | `/api/gallery/{id}/preview-url` | 공개 게시글 또는 소유자 | 미리보기용 서명 URL, 다운로드 횟수 유지 |
| PATCH | `/api/gallery/{id}/tag` | 필요 | 본인 게시글 태그 수정 |
| POST | `/api/gallery/{id}/comments` | 필요 | 댓글 등록 |
| DELETE | `/api/comments/{id}` | 필요 | 댓글 작성자/게시글 작성자 삭제 |
| POST/DELETE | `/api/gallery/{id}/likes` | 필요 | 좋아요/취소 |
| POST | `/api/gallery/{id}/download-url` | 없음 | 다운로드 URL 발급 및 카운트 증가 |
| SignalR | `/hubs/notifications` | 필요 | 향후 실시간 알림 기반 |
| GET/POST | `/api/projects` | 필요 | 내 LiveLayer 목록/원본 업로드 |
| GET/DELETE | `/api/projects/{id}` | 필요 | 프로젝트 상세/삭제 |
| POST | `/api/projects/{id}/process` | 필요 | 비동기 업스케일/레이어 처리 시작, 본문 생략 시 See-through |
| POST | `/api/projects/{id}/jobs/{jobId}/download-url` | 필요 | 완료된 본인 업스케일 결과의 서명 URL |
| GET | `/api/projects/{id}/processing-status` | 필요 | 최신 처리 상태 조회 |
| GET | `/api/projects/{id}/layers` | 필요 | 렌더링 레이어 조회 |
| PATCH | `/api/projects/{id}/layers/{layerId}` | 필요 | depth/transform/movement 수정 |

## LiveLayer AI 경계

- API와 영속성은 기존 ASP.NET Core/EF Core 구조를 유지합니다.
- `ProcessingJob`이 queue 상태를 DB에 보존하고 단일 background worker가 처리합니다. 프로세스 재시작 시 `Queued`/`Processing` 작업을 다시 queue에 넣습니다.
- `LocalGpu:Enabled=true`일 때 API는 공유 키로 인증된 GPU HTTP 워커에 multipart 바이트를 전송합니다. S3 URL/DB 자격 증명을 GPU에 전달하지 않습니다.
- 모델별 Python 프로세스에서 공식 See-through CLI, PartGLEE의 Detectron2 모델 API, Real-ESRGAN 라이브러리를 호출합니다. 모델 코드/가중치는 vendoring하지 않습니다.
- PartGLEE 일반 사진 결과의 깊이는 세로 위치 추정, 배경은 OpenCV 보간입니다. 기하학적 3D 재구성은 아닙니다.
- 처리 옵션을 DB에 보존하며 프로젝트별 활성 작업은 unique index로 제한합니다. 단일 API와 단일 GPU 워커 프로세스 구성을 대상으로 합니다.
- 업스케일 단독 작업은 기존 레이어를 유지합니다. 결과 URL은 projectId/jobId/UserId를 모두 확인하며, 처리 중 프로젝트 삭제는 409입니다.
- 성공 결과만 기존 레이어와 원자적으로 교체하며, 실패 시 기존 완료 레이어는 보존합니다.
- Project, Layer, 비공개 GalleryPost 조회는 항상 인증 UserId 또는 공개 여부를 확인합니다.

## 보안 경계

- JWT secret, DB connection string, S3 bucket, Replicate token은 설정 공급자에서만 읽습니다.
- 비밀번호는 10~128자이며 대문자, 숫자, 특수문자를 각각 포함해야 합니다.
- 비밀번호 원문은 DB 또는 응답에 저장하지 않고 BCrypt(work factor 12) 단방향 해시만 저장합니다.
- 이메일 인증 코드 생성·전송·검증은 Cognito가 담당하며 애플리케이션 DB에는 코드를 저장하지 않습니다.
- Cognito 인증 코드는 24시간 유효하며 API는 재전송 60초 제한과 인증 전 로그인 차단을 적용합니다.
- Cognito app client secret은 설정 공급자에서만 읽습니다.
- AWS SDK는 기본 credential chain을 사용하며 EC2 IAM Role을 우선합니다.
- S3 bucket은 public access block을 유지하고 pre-signed URL 방식으로 다운로드합니다.
- CORS 허용 origin은 명시 목록이며 `AllowAnyOrigin`과 credentials를 함께 사용하지 않습니다.
