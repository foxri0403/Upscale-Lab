# 인증 API 연동 가이드

JSON 필드명은 camelCase입니다. Windows/Android 연결 앱의 기존 로그인·세션 갱신은 그대로 사용합니다. 신규 회원가입은 이메일 인증과 약관·전자서명 동의를 모두 마쳐야 완료됩니다.

## 가입 상태와 계정 생성 시점

1. `POST /api/auth/register`: 이메일·영문 아이디·비밀번호를 받고 Cognito가 인증 메일을 보냅니다. 임시 `pending_registrations`만 저장합니다. 이때 `users`, 기본 설정, 로그인 세션, JWT는 생성하지 않습니다.
2. 웹은 같은 회원가입 화면의 이메일 바로 아래에 6자리 코드 입력란을 표시합니다. 서버가 돌려준 발송·만료 시각과 `serverTime`으로 클라이언트 시계 차이를 보정하여 카운트다운합니다. 새로고침해도 발송 시각을 초기화하지 않습니다. 비밀번호와 코드는 브라우저 저장소에 저장하지 않습니다.
3. `POST /api/auth/verify-email`: 이메일 인증만 완료하고 약관 동의용 15분짜리 일회용 자격을 반환합니다. 회원·JWT·세션은 아직 없습니다.
4. 웹에서 약관 전문, 중요 내용 요약, 사본 다운로드, 별도의 약관/전자서명 확인란과 아이디 서명을 제공합니다. 미리 체크된 항목은 없습니다. 미동의·창 닫기·잘못된 서명으로 가입은 완료되지 않습니다.
5. `POST /api/auth/accept-registration-terms`: 서버가 인증 완료, 자격 유효기간, 약관 버전, 두 동의와 서명을 검증한 뒤 한 트랜잭션에서 회원·기본 설정·세션을 만들고 대기 정보를 제거합니다. 약관 버전·SHA-256 원문 확인값·UTC 동의 시각·서명·이메일 인증 시각은 회원에 기록합니다.

Cognito에는 이메일 확인을 위한 외부 인증 레코드가 먼저 생깁니다. 이것은 애플리케이션 DB의 회원이나 서비스 로그인 권한이 아닙니다. 서비스의 신규 회원 레코드는 마지막 동의 단계에서만 생깁니다.

## 가입 신청

`POST /api/auth/register`

```json
{ "email": "user@example.com", "username": "tester", "password": "Correct-horse1!" }
```

이메일 최대 320자, 아이디는 영문 대·소문자와 숫자 2~50자(웹 2~24자), 비밀번호는 10~128자이며 대문자·숫자·공백이 아닌 특수문자를 각각 포함해야 합니다. 비밀번호는 BCrypt 해시만 임시 저장하며 코드 생성·전송·검증은 Cognito가 담당합니다.

성공 시 `201 Created`:

```json
{
  "email": "user@example.com",
  "username": "tester",
  "requiresEmailVerification": true,
  "verificationCodeSentAt": "2026-11-02T11:00:00Z",
  "verificationCodeExpiresAt": "2026-11-02T11:15:00Z",
  "resendAvailableAt": "2026-11-02T11:01:00Z",
  "serverTime": "2026-11-02T11:00:01Z",
  "cancellationToken": "64자리 임의의 16진수",
  "registration": null
}
```

같은 원래 가입 정보로 다시 요청하면 진행 중인 인증을 이어갑니다. 이미 이메일 인증을 마쳤다면 `requiresEmailVerification: false`와 새 `registration` 자격을 반환하여 약관을 다시 확인할 수 있습니다. 이메일만 알아서는 동의 자격을 재발급받을 수 없습니다. 이전 자격은 무효화됩니다.

## 이메일 인증

`POST /api/auth/verify-email`

```json
{ "email": "user@example.com", "code": "123456" }
```

성공 시 `200 OK`와 약관 동의 자격을 반환합니다. 로그인 JWT가 아닙니다.

```json
{
  "email": "user@example.com",
  "username": "tester",
  "token": "64자리 임의의 16진수",
  "expiresAt": "2026-11-02T11:20:00Z",
  "termsVersion": "2026-11-02.1"
}
```

잘못되거나 만료된 코드는 `400`입니다. 자격 원문은 서버 DB에 보관하지 않고 SHA-256 해시만 저장합니다. 웹에서는 동의 자격을 메모리에만 두므로 이 단계에서 새로고침한 경우 가입 화면에 원래 정보를 다시 입력하여 이어갑니다.

## 약관 동의 및 회원가입 완료

`POST /api/auth/accept-registration-terms`

```json
{
  "email": "user@example.com",
  "token": "64자리 임의의 16진수",
  "termsVersion": "2026-11-02.1",
  "acceptTerms": true,
  "confirmSignature": true,
  "signature": "tester"
}
```

성공 시 기존 로그인과 같은 `AuthResponse`를 반환합니다. 동의 누락·다른 서명·구 버전은 `400`, 미인증·만료·다른 이메일·잘못된 자격·재사용은 `401`이며 회원과 세션을 생성하지 않습니다. 서명은 가입한 아이디와 정확히 일치해야 합니다.

## 인증 코드 재전송

`POST /api/auth/resend-verification`

```json
{ "email": "user@example.com" }
```

항상 일반적인 메시지와 `202 Accepted`를 사용합니다. 유효한 인증 대기에 대해서만 `verificationCodeSentAt`, `verificationCodeExpiresAt`, `resendAvailableAt`, `serverTime`을 반환합니다. 존재하지 않거나 이미 이메일 인증을 끝낸 이메일은 시간 필드가 null입니다. 60초 재전송 대기 중에는 실제 메일을 보내거나 기존 만료 시각을 연장하지 않습니다. 실제 재전송 성공 시에만 새 발송 시각부터 15분을 계산합니다. 이미 만료된 대기는 가입 신청을 다시 해야 합니다.
## 가입 대기 취소 및 정보 다시 입력

`POST /api/auth/cancel-registration`

```json
{ "email": "user@example.com", "token": "가입 신청 응답의 cancellationToken" }
```

가입 신청 응답은 취소 전용 256비트 난수 `cancellationToken`을 반환하며 DB에는 SHA-256 해시만 저장합니다. 웹은 이메일 인증 전 진행 정보를 sessionStorage에 저장할 때 이 취소 자격도 함께 보관합니다. 로그인이나 약관 서명 자격으로는 사용할 수 없습니다. 이전 버전에서 열어 둔 화면처럼 취소 자격이 없으면 `{ "email": "…", "password": "가입 시 입력한 비밀번호" }`로 본인의 대기임을 확인할 수 있습니다. 이메일만으로 대기를 삭제할 수 없습니다.

이메일 잠금 아래 회원이 없는지와 취소 자격 또는 원래 비밀번호를 검증하고, Cognito의 가입 대기 레코드를 제거한 뒤 임시 DB 기록을 삭제합니다. 인증 전/후 모두 취소할 수 있으며 완료된 회원과 그 외부 인증은 삭제하지 않습니다. 이미 대기가 없으면 멱등적으로 `204 No Content`입니다. 외부 삭제 실패 시 임시 기록과 브라우저 입력 상태를 유지합니다.

웹의 ‘가입 정보 다시 입력하기’는 이 API 성공 후에만 코드를 비우고 입력란을 다시 엽니다. 따라서 같은 이메일로 아이디나 비밀번호를 바꾸어 다시 신청할 수 있습니다. 배포 전 `AddRegistrationCancellationCredential` 마이그레이션을 적용해야 하며 기존 대기는 nullable 취소 확인값과 비밀번호 확인 경로로 호환합니다.

## 로그인

`POST /api/auth/login`

```json
{
  "email": "user@example.com 또는 tester",
  "password": "Correct-horse1!"
}
```

하위 호환성을 위해 요청 필드명은 `email`을 유지하지만 값에는 이메일 또는 사용자 아이디를 입력할 수 있습니다. 이메일 인증과 약관 동의로 가입을 완료한 뒤 `200 OK`와 `AuthResponse`를 반환합니다. 인증 전 로그인은 `403 Forbidden`, 로그인 정보 불일치는 `401 Unauthorized`입니다.

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


## 로그인 세션과 인증 대기 정보 정리

신규 JWT는 `sid`로 로그인 세션에 연결되며 세션 폐기/만료, 이메일 인증, 비밀번호 fingerprint를 검증합니다. 기존 회원의 로그인·갱신·로그아웃과 기기 인증 정책은 유지합니다. 세션 갱신은 90일 미사용 또는 최초 발급 후 1년이 지나면 만료됩니다. [연결 앱의 계정+기기 인증](agent-api.md)을 따릅니다.

정리 작업은 시작 시 한 번, 이후 30초마다 실행하며 `users`를 삭제하지 않습니다. 이메일 미인증 대기는 마지막 실제 발송부터 15분, 이메일 인증 후 미동의 대기는 인증부터 24시간이 지나면 정리합니다. 정리 시 같은 이메일의 회원이 있으면 그 외부 인증 레코드는 삭제하지 않습니다. 회원이 없는 만료 대기의 Cognito 레코드만 정리하며 외부 정리 실패 시 임시 기록을 남겨 재시도합니다. PostgreSQL 이메일 단위 advisory lock으로 신규 가입·인증·동의·정리를 직렬화하고 아이디 예약도 잠급니다.

필수 설정은 기존 `Cognito:UserPoolId`, `Cognito:ClientId`, `Cognito:Region`이며 필요 시 `Cognito:ClientSecret`을 사용합니다. 외부 임시 레코드 정리는 서버 IAM Role/로컬 AWS 프로필의 대상 pool `AdminGetUser`·`AdminDeleteUser` 권한을 사용합니다. 새 IAM 권한이나 자동 배포를 추가하지 않습니다.

## 수동 전환 및 로컬 검증

- `AddPendingRegistrationConsent` 마이그레이션을 검토하고 수동 적용한 뒤 API와 웹을 함께 업데이트합니다. 이 변경으로 실제 DB나 운영 서버를 자동 업데이트하지 않습니다.
- 이전 버전의 미인증 `users`가 남아 있으면 전환 전에 기존 정리 정책의 15분 창이 끝나도록 가입을 잠시 멈추고 기존 정리 작업의 완료를 확인합니다. 새 마이그레이션은 기존 회원을 삭제하거나 동의한 것으로 소급 표시하지 않습니다.
- 생성된 EF 스냅샷은 이미 이전 마이그레이션에 포함된 결제/구독 모델과의 기존 차이도 정합화합니다. 이번 `Up/Down`에는 인증 대기 테이블과 회원의 약관 기록 열만 포함하며 결제/구독 테이블을 다시 생성하지 않습니다.
- 백엔드 폴더에서 기존 보안 설정을 사용해 `dotnet ef database update --project src/UpscaleLab.Infrastructure --startup-project src/UpscaleLab.Api`와 README의 API 시작 절차를 실행합니다. 메일 발송은 실제 Cognito 설정이 있어야 합니다.
- UI만 확인할 때는 웹에서 `pnpm dev:production-ui` 후 `/signup`을 엽니다. 실제 발송 없이 전 과정을 확인하는 개발 전용 `/tests/signup-flow.html`은 [웹 가이드](../web/README.md)를 따릅니다.

## 약관 원문과 법적 범위

정식 원문은 [버전이 고정된 JSON](../web/src/legal/image-copyright-terms.json)입니다. 웹과 API가 같은 파일을 번들/리소스로 사용하며 API는 줄바꿈을 LF로 정규화한 원문의 SHA-256을 기록합니다. 이미 동의된 버전의 내용은 덮어쓰지 말고 새 버전과 필요한 재동의 절차로 변경해야 합니다.

권리 귀속과 이용허락은 [저작권법 제46조](https://www.law.go.kr/법령/저작권법/제46조), 명확한 중요 내용 표시와 설명은 [약관법 제3조](https://www.law.go.kr/lsLinkCommonInfo.do?lsJoLnkSeq=1029708921), 면책 범위는 [약관법 제7조](https://www.law.go.kr/법령/약관의규제에관한법률/제7조), 전자적 동의 방식은 [전자서명법 제3조](https://www.law.go.kr/LSW/lsInfoP.do?efYd=20221020&lsiSeq=236201)를 참고했습니다. 제작팀 자신의 위법행위·고의·중대한 과실 및 법률상 배제 불가능한 책임, 제3자의 권리, 이용자의 법정 구제 권리를 일괄 면제하지 않습니다. 실제 효력은 운영 주체와 구체적 사실관계에 따라 판단되므로 법률 전문가 검토 없이 효력이 보장된다고 설명하면 안 됩니다. 개인정보 처리에 필요한 고지·동의는 별도로 제공해야 합니다.
