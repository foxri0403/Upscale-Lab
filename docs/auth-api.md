# 인증 API 연동 가이드

이 문서는 Windows(WPF)와 Android(Flutter) 클라이언트에서 회원가입, 이메일 인증 및 로그인 API를 연동하기 위한 계약을 설명합니다. JSON 필드명은 camelCase입니다.

## 인증 흐름

1. 클라이언트가 `POST /api/auth/register`로 회원가입합니다.
2. 서버가 6자리 인증 코드를 Resend를 통해 이메일로 전송합니다.
3. 클라이언트가 이메일과 코드를 `POST /api/auth/verify-email`로 제출합니다.
4. 인증 성공 응답의 JWT를 저장하고 인증이 필요한 API에 사용합니다.
5. 코드가 오지 않으면 `POST /api/auth/resend-verification`으로 재전송을 요청합니다.

이메일 인증 전에는 로그인할 수 없습니다. 인증 코드는 10분 동안 유효하고 최대 5번까지 입력할 수 있으며, 재전송은 60초 간격으로 제한됩니다.

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
  "verificationCodeExpiresAt": "2026-10-02T11:10:00Z"
}
```

비밀번호는 BCrypt(work factor 12) 단방향 해시만 저장합니다. 이메일 인증 코드 원문도 저장하지 않고 서버 비밀키를 사용한 HMAC-SHA256 결과만 저장합니다.

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

잘못되거나 만료된 코드는 동일한 `400` 오류를 반환합니다. 5번 실패하면 해당 코드는 폐기되므로 새 코드를 요청해야 합니다.

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
| `503` | Resend 설정 누락 또는 이메일 발송 실패 | 잠시 후 재전송 안내 |

서비스 오류는 `application/problem+json`으로 반환됩니다. ASP.NET Core 모델 검증 오류는 필드별 `errors` 객체가 포함된 ValidationProblemDetails 형식이므로 UI에서는 `detail`과 `errors`를 모두 처리해야 합니다.

## 서버 설정

Resend에서 발신 도메인을 인증하고 Sending access 권한의 API key를 만든 뒤 아래 값을 user-secrets 또는 운영 환경변수로 설정합니다.

```powershell
dotnet user-secrets set --project backend/src/UpscaleLab.Api "EmailVerification:ApiKey" "YOUR_RESEND_API_KEY"
dotnet user-secrets set --project backend/src/UpscaleLab.Api "EmailVerification:FromAddress" "no-reply@YOUR_VERIFIED_DOMAIN"
dotnet user-secrets set --project backend/src/UpscaleLab.Api "EmailVerification:CodeHashKey" "YOUR_RANDOM_SECRET_AT_LEAST_32_CHARACTERS"
```

운영 환경변수 이름은 각각 `EmailVerification__ApiKey`, `EmailVerification__FromAddress`, `EmailVerification__CodeHashKey`입니다. 실제 값은 Git에 커밋하지 않습니다.

스키마 변경을 적용하려면 배포 전 백업을 확인한 후 다음 migration을 실행합니다.

```powershell
dotnet ef database update --project backend/src/UpscaleLab.Infrastructure --startup-project backend/src/UpscaleLab.Api
```

Migration은 기존 계정을 인증 완료 상태로 보존하며, 이후 생성되는 신규 계정부터 이메일 인증을 요구합니다.
