# 인증 API 연동 가이드

이 문서는 Windows(WPF)와 Android(Flutter) 클라이언트에서 회원가입, 이메일 인증, 로그인, 아이디 찾기 및 비밀번호 재설정 API를 연동하기 위한 계약을 설명합니다. JSON 필드명은 camelCase입니다.

## 인증 흐름

1. 클라이언트가 `POST /api/auth/register`로 회원가입합니다.
2. 서버가 Amazon Cognito에 사용자를 등록하고 Cognito가 6자리 인증 코드를 이메일로 전송합니다.
3. 클라이언트가 이메일과 코드를 `POST /api/auth/verify-email`로 제출합니다.
4. 인증 성공 응답의 JWT를 저장하고 인증이 필요한 API에 사용합니다.
5. 코드가 오지 않으면 `POST /api/auth/resend-verification`으로 재전송을 요청합니다.
6. 로그인 이메일을 잊은 사용자는 사용자 이름으로 `POST /api/auth/find-id`를 호출합니다.
7. 비밀번호를 잊은 사용자는 `POST /api/auth/forgot-password`로 코드를 요청하고 `POST /api/auth/reset-password`로 새 비밀번호를 설정합니다.

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
  "email": "user@example.com 또는 tester",
  "password": "Correct-horse1!"
}
```

하위 호환성을 위해 요청 필드명은 `email`을 유지하지만 값에는 이메일 또는 사용자 아이디를 입력할 수 있습니다. 이메일 인증 완료 후 `200 OK`와 `AuthResponse`를 반환합니다. 인증 전 로그인은 `403 Forbidden`, 로그인 정보 불일치는 `401 Unauthorized`입니다.

인증이 필요한 후속 API에는 다음 헤더를 사용합니다.

```http
Authorization: Bearer <accessToken>
```

JWT 기본 만료 시간은 60분입니다. Refresh token은 아직 제공하지 않습니다.

## 아이디 찾기

가입 이메일을 제출하면 인증이 완료된 계정의 사용자 아이디 전체를 반환합니다.

`POST /api/auth/find-id`

```json
{
  "email": "user@example.com"
}
```

일치하는 계정이 있으면 `200 OK`를 반환합니다.

```json
{
  "username": "tester"
}
```

인증 완료 계정이 없으면 `username`은 `null`입니다.

## 비밀번호 재설정 코드 요청

`POST /api/auth/forgot-password`

```json
{
  "email": "user@example.com"
}
```

계정 존재 여부가 노출되지 않도록 일치 여부와 관계없이 `202 Accepted`를 반환합니다.

```json
{
  "message": "일치하는 계정이 있다면 등록된 이메일로 재설정 코드를 전송했습니다."
}
```

## 새 비밀번호 설정

`POST /api/auth/reset-password`

```json
{
  "email": "user@example.com",
  "code": "123456",
  "newPassword": "New-correct-horse2!"
}
```

성공 시 `200 OK`를 반환합니다. 서버는 먼저 Cognito에서 코드와 새 비밀번호를 확인한 뒤, 애플리케이션 DB의 기존 해시를 새 BCrypt 해시로 교체합니다. 비밀번호 원문이나 복호화 가능한 값은 저장하지 않습니다. 기존 비밀번호와 같은 값을 입력해도 정상 처리하며 새로운 BCrypt 솔트로 다시 해시합니다. 비밀번호 형식은 회원가입과 동일한 정책을 따릅니다.

```json
{
  "message": "비밀번호가 변경되었습니다."
}
```

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

팀원 AWS 계정의 User Pool을 사용하는 경우 팀원이 아래 항목을 확인해 전달해야 합니다.

- User Pool을 생성한 AWS Region(서울이면 `ap-northeast-2`)
- App client의 Client ID
- App client secret 생성 여부와, 생성했다면 Client Secret
- Self-service sign-up 활성화 여부
- 이메일을 로그인 식별자 및 자동 확인 속성으로 설정했는지 여부
- 비밀번호 정책이 최소 10자, 대문자·숫자·특수문자 필수인지 여부
- 이메일 공급자가 `Send email with Cognito`인지 여부

현재 코드에는 User Pool ID, AWS 계정 로그인 정보, AWS Access Key가 필요하지 않습니다. 팀원은 root 계정이나 IAM 자격 증명을 공유하지 말고 위 Cognito 설정값만 안전한 채널로 전달합니다.

받은 Client ID와, 생성했다면 Client Secret을 user-secrets에 설정합니다. `.env` 파일은 현재 `dotnet run`에서 자동으로 로드되지 않습니다.

```powershell
dotnet user-secrets set --project backend/src/UpscaleLab.Api "Cognito:ClientId" "YOUR_COGNITO_APP_CLIENT_ID"
# App client에 secret을 생성한 경우에만 설정합니다.
dotnet user-secrets set --project backend/src/UpscaleLab.Api "Cognito:ClientSecret" "YOUR_COGNITO_APP_CLIENT_SECRET"
# User Pool이 서울 리전이 아닌 경우에만 설정합니다.
dotnet user-secrets set --project backend/src/UpscaleLab.Api "Cognito:Region" "YOUR_COGNITO_REGION"
```

secret이 없는 public app client라면 `Cognito:ClientSecret`은 생략합니다. 기본 리전은 `ap-northeast-2`이며 `Cognito:Region` 또는 `Cognito__Region`으로 변경할 수 있습니다. 실제 Client ID와 Client Secret은 Git에 커밋하지 않습니다. Cognito 기본 이메일 구성은 AWS 계정당 하루 50건 제한이 있으므로 운영 트래픽이 늘어나면 SES 연결을 검토해야 합니다.

이 통합은 Cognito를 이메일 코드 발송·확인 공급자로 사용하고 기존 BCrypt 로그인과 자체 JWT 발급은 유지합니다. 따라서 WPF/Flutter의 API 요청·응답 계약은 변경되지 않습니다.

스키마 변경을 적용하려면 배포 전 백업을 확인한 후 다음 migration을 실행합니다.

```powershell
dotnet ef database update --project backend/src/UpscaleLab.Infrastructure --startup-project backend/src/UpscaleLab.Api
```

Migration은 기존 계정의 인증 완료 상태를 보존하고 로컬 인증 코드 관련 컬럼을 제거합니다. 이전 Resend 흐름에서 아직 인증되지 않은 계정은 Cognito에 존재하지 않으므로 운영 적용 전에 별도로 정리하거나 마이그레이션해야 합니다.


## 로그인 세션과 만료 계정 정리


신규 JWT는 `sid`로 로그인 세션에 연결되며 서버가 매 요청에서 세션 폐기/만료, 이메일 인증, 비밀번호 fingerprint를 검증합니다. 서버 로그아웃 후 아직 만료되지 않은 신규 액세스 토큰도 거부합니다. 기존 sid 없는 토큰은 기존 API 호환에만 허용하고 새 기기 제어/배경화면 관리 API에는 재로그인이 필요합니다. 설치형 에이전트의 계정+기기 이중 인증은 [연동 규격](agent-api.md)을 따릅니다.

`AddPersistentAuthSessions` 마이그레이션을 검토하고 수동 적용한 뒤 새 API를 배포합니다. 클라이언트만 업데이트하면 서버의 인증 만료·정리 정책은 바뀌지 않습니다. Cognito의 기존 인증 코드는 서비스 자체에서 24시간 유효하므로, API가 가입 시점부터 **15분**을 별도로 제한합니다. 재전송은 최초 가입의 만료 시점을 연장하지 않습니다. 이메일 인증 완료 후에는 만료 시각을 지우며 계정을 자동 삭제하지 않습니다.

API의 정리 작업은 시작할 때 한 번, 이후 30초마다 실행합니다. 15분이 지난 코드는 즉시 거부하고, 미인증 계정은 다음 정리에서 Cognito와 PostgreSQL 양쪽에서 삭제합니다. 기존 미인증 계정도 생성 시각부터 15분으로 계산합니다. Cognito 삭제 실패 시 DB 기록을 유지하고 로그를 남겨 다음 주기에 재시도합니다. 해당 이메일/사용자 이름으로 다시 가입할 때도 먼저 만료된 계정을 정리합니다. PostgreSQL 행 잠금으로 인증·재전송과 정리가 충돌하지 않게 합니다. Cognito에서 이미 확인된 계정은 정리 대상에서 보호합니다.

필수 설정은 `Cognito:UserPoolId`, `Cognito:ClientId`, `Cognito:Region`이며 선택적으로 `Cognito:ClientSecret`을 사용합니다. 삭제 API는 AWS 기본 credential chain(서버 IAM Role/로컬 프로필 등)을 사용합니다. 대상 User Pool ARN에 `cognito-idp:AdminGetUser`, `cognito-idp:AdminDeleteUser` 권한을 부여해야 합니다. 실제 AWS 권한/설정은 이 작업에서 변경하지 않았습니다.

로그인·이메일 인증 응답에 `refreshToken`, `refreshExpiresAt`이 추가됩니다. `POST /api/auth/refresh`는 `{ "refreshToken": "…" }`으로 새 액세스 토큰을 발급하며 `POST /api/auth/logout`은 해당 기기의 갱신 자격을 폐기합니다. 액세스 토큰은 기존 JWT 만료 설정(기본 60분)을 따릅니다. 갱신 자격은 256비트 난수이며 DB에는 SHA-256 해시만 저장합니다. 같은 자격으로 재시도할 수 있어 갱신 응답이 유실되어도 다음 요청에서 복구됩니다. 90일 미사용 또는 최초 발급 후 1년이 지나면 다시 로그인해야 합니다. 비밀번호 재설정 후 이전 갱신 자격도 무효입니다. 로그아웃은 오프라인에서도 기기의 자격을 삭제하며 서버 폐기는 연결이 가능한 경우에 수행합니다.

Windows/Android 연결 앱의 세션 저장과 로그아웃 처리는 [연결 앱](companion-apps.md)을 따른다. 웹은 자동 로그인 선택 여부에 따라 sessionStorage/localStorage를 사용한다. 인증 계약은 백엔드 테스트로 검증하고 실제 AWS 권한과 실기기는 별도로 확인한다.

참고: [Cognito 인증 코드 수명](https://docs.aws.amazon.com/cognito/latest/developerguide/quotas.html), [AdminDeleteUser IAM 요구사항](https://docs.aws.amazon.com/cognito-user-identity-pools/latest/APIReference/API_AdminDeleteUser.html).
