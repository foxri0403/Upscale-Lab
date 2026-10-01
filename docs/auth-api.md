# 인증 API 연동 가이드

이 문서는 Windows(WPF)와 Android(Flutter) 클라이언트에서 회원가입, 이메일 인증 및 로그인 API를 연동하기 위한 계약을 설명합니다. JSON 필드명은 camelCase입니다.

## 인증 흐름

1. 클라이언트가 `POST /api/auth/register`로 회원가입합니다.
2. 서버가 Amazon Cognito에 사용자를 등록하고 Cognito가 6자리 인증 코드를 이메일로 전송합니다.
3. 클라이언트가 이메일과 코드를 `POST /api/auth/verify-email`로 제출합니다.
4. 인증 성공 응답의 JWT를 저장하고 인증이 필요한 API에 사용합니다.
5. 코드가 오지 않으면 `POST /api/auth/resend-verification`으로 재전송을 요청합니다.

이메일 인증 전에는 로그인할 수 없습니다. Cognito 인증 코드는 24시간 동안 유효하며, API의 재전송 요청은 60초 간격으로 제한됩니다. 잘못된 코드 입력 제한과 추가 요청 제한은 Cognito 정책을 따릅니다.

## 회원가입

`POST /api/auth/register`

```json
{
  "email": "user@example.com",
  "username": "tester",
  "password": "Correct-horse1!"
}
```

- `email`: 유효한 이메일 주소, 최대 320자, 대소문자를 구분하지 않음
- `username`: 앞뒤 공백을 제외한 사용자 이름, 2~50자, 중복 불가
- `password`: 10~128자이며 대문자, 숫자, 공백이 아닌 특수문자를 각각 하나 이상 포함

성공 시 `201 Created`를 반환합니다. 이 단계에서는 JWT를 발급하지 않습니다.

```json
{
  "user": {
    "id": "00000000-0000-0000-0000-000000000000",
    "email": "user@example.com",
    "username": "tester",
    "isEmailVerified": false,
    "createdAt": "2026-10-02T11:00:00Z"
  },
  "requiresEmailVerification": true,
  "verificationCodeExpiresAt": "2026-10-03T11:00:00Z"
}
```

비밀번호는 애플리케이션 DB에 BCrypt(work factor 12) 단방향 해시만 저장합니다. Cognito 가입 요청에도 전달되지만 애플리케이션은 비밀번호 원문을 저장하거나 로그에 남기지 않습니다. 인증 코드 생성·전송·검증은 Cognito가 담당하며 애플리케이션 DB에는 인증 코드가 저장되지 않습니다.

## 이메일 인증

`POST /api/auth/verify-email`

```json
{
  "email": "user@example.com",
  "code": "123456"
}
```

성공 시 `200 OK`와 JWT를 반환합니다.

```json
{
  "accessToken": "eyJ...",
  "expiresAt": "2026-10-02T12:00:00Z",
  "user": {
    "id": "00000000-0000-0000-0000-000000000000",
    "email": "user@example.com",
    "username": "tester",
    "isEmailVerified": true,
    "createdAt": "2026-10-02T11:00:00Z"
  }
}
```

잘못되거나 만료된 코드는 동일한 `400` 오류를 반환합니다. Cognito가 요청을 제한한 경우 잠시 후 새 코드를 요청해야 합니다.

## 인증 코드 재전송

`POST /api/auth/resend-verification`

```json
{
  "email": "user@example.com"
}
```

계정 존재 여부가 노출되지 않도록 존재하지 않는 이메일, 이미 인증된 이메일, 재전송 대기 중인 이메일 모두 동일한 `202 Accepted` 응답을 사용합니다.

```json
{
  "message": "인증 메일 요청이 접수되었습니다."
}
```

## 로그인

`POST /api/auth/login`

```json
{
  "email": "user@example.com",
  "password": "Correct-horse1!"
}
```

이메일 인증 완료 후 `200 OK`와 `AuthResponse`를 반환합니다. 인증 전 로그인은 `403 Forbidden`, 로그인 정보 불일치는 `401 Unauthorized`입니다.

인증이 필요한 후속 API에는 다음 헤더를 사용합니다.

```http
Authorization: Bearer <accessToken>
```

JWT 기본 만료 시간은 60분입니다. Refresh token은 아직 제공하지 않습니다.

## 현재 사용자 확인

`GET /api/auth/me`

유효한 Bearer token이 필요하며 `isEmailVerified`를 포함한 사용자 정보를 반환합니다.

## 오류 처리

| 상태 코드 | 발생 조건 | UI 처리 예시 |
|---:|---|---|
| `400` | 입력값 오류, 인증 코드 오류 또는 만료 | 필드 오류 또는 `detail` 표시 |
| `401` | 로그인 정보 불일치 또는 인증 토큰 오류 | 로그인 오류 또는 재로그인 |
| `403` | 이메일 인증 전 로그인 | 인증 코드 입력 화면으로 이동 |
| `409` | 이메일 또는 사용자 이름 중복 | 중복 안내 |
| `503` | Cognito 설정 누락, 요청 제한 또는 이메일 발송 실패 | 잠시 후 재전송 안내 |

서비스 오류는 `application/problem+json`으로 반환됩니다. ASP.NET Core 모델 검증 오류는 필드별 `errors` 객체가 포함된 ValidationProblemDetails 형식이므로 UI에서는 `detail`과 `errors`를 모두 처리해야 합니다.

## 서버 설정

AWS Cognito에서 이메일 로그인을 사용하는 User Pool을 만들고 자체 회원가입과 이메일 자동 인증을 활성화합니다. 비밀번호 정책은 최소 10자, 대문자·숫자·특수문자 필수로 설정해야 합니다. 이메일 공급자는 자체 도메인이 필요 없는 `Send email with Cognito`를 선택합니다.

User Pool에 app client를 만든 뒤 Client ID와, 생성했다면 Client Secret을 user-secrets에 설정합니다.

```powershell
dotnet user-secrets set --project backend/src/UpscaleLab.Api "Cognito:ClientId" "YOUR_COGNITO_APP_CLIENT_ID"
# App client에 secret을 생성한 경우에만 설정합니다.
dotnet user-secrets set --project backend/src/UpscaleLab.Api "Cognito:ClientSecret" "YOUR_COGNITO_APP_CLIENT_SECRET"
```

secret이 없는 public app client라면 `Cognito:ClientSecret`은 생략합니다. 기본 리전은 `ap-northeast-2`이며 `Cognito:Region` 또는 `Cognito__Region`으로 변경할 수 있습니다. 실제 Client ID와 Client Secret은 Git에 커밋하지 않습니다. Cognito 기본 이메일 구성은 AWS 계정당 하루 50건 제한이 있으므로 운영 트래픽이 늘어나면 SES 연결을 검토해야 합니다.

이 통합은 Cognito를 이메일 코드 발송·확인 공급자로 사용하고 기존 BCrypt 로그인과 자체 JWT 발급은 유지합니다. 따라서 WPF/Flutter의 API 요청·응답 계약은 변경되지 않습니다.

스키마 변경을 적용하려면 배포 전 백업을 확인한 후 다음 migration을 실행합니다.

```powershell
dotnet ef database update --project backend/src/UpscaleLab.Infrastructure --startup-project backend/src/UpscaleLab.Api
```

Migration은 기존 계정의 인증 완료 상태를 보존하고 로컬 인증 코드 관련 컬럼을 제거합니다. 이전 Resend 흐름에서 아직 인증되지 않은 계정은 Cognito에 존재하지 않으므로 운영 적용 전에 별도로 정리하거나 마이그레이션해야 합니다.
