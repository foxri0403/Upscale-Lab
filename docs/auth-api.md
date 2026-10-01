# 인증 API 연동 가이드

이 문서는 Windows(WPF)와 Android(Flutter) 클라이언트에서 회원가입 및 로그인 API를 연동하기 위한 계약을 설명합니다. 모든 요청과 응답의 JSON 필드명은 기본 ASP.NET Core 직렬화 규칙에 따라 camelCase입니다.

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

성공 시 `201 Created`와 아래 형식의 응답을 반환합니다.

```json
{
  "accessToken": "eyJ...",
  "expiresAt": "2026-10-01T12:00:00Z",
  "user": {
    "id": "00000000-0000-0000-0000-000000000000",
    "email": "user@example.com",
    "username": "tester",
    "createdAt": "2026-10-01T11:00:00Z"
  }
}
```

비밀번호는 BCrypt(work factor 12)로 단방향 해시한 값만 데이터베이스에 저장하며, 원문은 엔터티나 응답에 포함하지 않습니다.

## 로그인

`POST /api/auth/login`

```json
{
  "email": "user@example.com",
  "password": "Correct-horse1!"
}
```

성공 시 `200 OK`와 회원가입 성공 응답과 동일한 `AuthResponse`를 반환합니다. 이메일은 대소문자를 구분하지 않습니다.

인증이 필요한 후속 API에는 다음 헤더를 사용합니다.

```http
Authorization: Bearer <accessToken>
```

현재 JWT 만료 시간 기본값은 60분입니다. 클라이언트는 `expiresAt`을 기준으로 만료 상태를 처리해야 합니다. Refresh token은 아직 제공하지 않습니다.

## 현재 사용자 확인

`GET /api/auth/me`

유효한 Bearer token이 필요하며 성공 시 `200 OK`와 `user` 객체의 필드만 반환합니다.

## 오류 처리

| 상태 코드 | 발생 조건 | UI 처리 예시 |
|---:|---|---|
| `400` | 이메일 형식, 사용자 이름 길이, 비밀번호 정책 등 입력값 오류 | 필드 오류 또는 `detail` 표시 |
| `401` | 로그인 정보 불일치 또는 인증 토큰 오류 | 로그인 오류 표시 또는 재로그인 |
| `409` | 이메일 또는 사용자 이름 중복 | 중복 안내 |

서비스 계층에서 발생한 오류는 `application/problem+json`으로 반환됩니다.

```json
{
  "type": "about:blank",
  "title": "Validation Failed",
  "status": 400,
  "detail": "비밀번호는 10~128자이며 대문자, 숫자, 특수문자를 각각 하나 이상 포함해야 합니다.",
  "instance": "/api/auth/register",
  "traceId": "..."
}
```

ASP.NET Core 모델 검증 단계에서 거절된 요청도 `400` 응답이지만, 필드별 `errors` 객체가 포함된 ValidationProblemDetails 형식입니다. UI에서는 두 형식의 `detail`과 `errors`를 모두 처리해야 합니다.

## 이메일 인증 확장 계획

이메일 인증은 이번 범위에 포함하지 않았습니다. 추후 사용자 인증 상태, 인증 토큰과 만료 시간, 재전송 API를 추가하고 로그인 시 인증 완료 여부를 확인하는 방식으로 확장할 수 있습니다. UI는 현재 응답에 없는 이메일 인증 필드를 가정하지 않아야 합니다.
