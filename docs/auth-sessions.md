# 자동 로그인과 이메일 인증

신규 JWT는 `sid`로 로그인 세션에 연결되며 서버가 매 요청에서 세션 폐기/만료, 이메일 인증, 비밀번호 fingerprint를 검증합니다. 서버 로그아웃 후 아직 만료되지 않은 신규 액세스 토큰도 거부합니다. 기존 sid 없는 토큰은 기존 API 호환에만 허용하고 새 기기 제어/배경화면 관리 API에는 재로그인이 필요합니다. 설치형 에이전트의 계정+기기 이중 인증은 [연동 규격](agent-api.md)을 따릅니다.

`AddPersistentAuthSessions` 마이그레이션을 검토하고 수동 적용한 뒤 새 API를 배포합니다. 클라이언트만 업데이트하면 서버의 인증 만료·정리 정책은 바뀌지 않습니다. Cognito의 기존 인증 코드는 서비스 자체에서 24시간 유효하므로, API가 가입 시점부터 **15분**을 별도로 제한합니다. 재전송은 최초 가입의 만료 시점을 연장하지 않습니다. 이메일 인증 완료 후에는 만료 시각을 지우며 계정을 자동 삭제하지 않습니다.

API의 정리 작업은 시작할 때 한 번, 이후 30초마다 실행합니다. 15분이 지난 코드는 즉시 거부하고, 미인증 계정은 다음 정리에서 Cognito와 PostgreSQL 양쪽에서 삭제합니다. 기존 미인증 계정도 생성 시각부터 15분으로 계산합니다. Cognito 삭제 실패 시 DB 기록을 유지하고 로그를 남겨 다음 주기에 재시도합니다. 해당 이메일/사용자 이름으로 다시 가입할 때도 먼저 만료된 계정을 정리합니다. PostgreSQL 행 잠금으로 인증·재전송과 정리가 충돌하지 않게 합니다. Cognito에서 이미 확인된 계정은 정리 대상에서 보호합니다.

필수 설정은 `Cognito:UserPoolId`, `Cognito:ClientId`, `Cognito:Region`이며 선택적으로 `Cognito:ClientSecret`을 사용합니다. 삭제 API는 AWS 기본 credential chain(서버 IAM Role/로컬 프로필 등)을 사용합니다. 대상 User Pool ARN에 `cognito-idp:AdminGetUser`, `cognito-idp:AdminDeleteUser` 권한을 부여해야 합니다. 실제 AWS 권한/설정은 이 작업에서 변경하지 않았습니다.

로그인·이메일 인증 응답에 `refreshToken`, `refreshExpiresAt`이 추가됩니다. `POST /api/auth/refresh`는 `{ "refreshToken": "…" }`으로 새 액세스 토큰을 발급하며 `POST /api/auth/logout`은 해당 기기의 갱신 자격을 폐기합니다. 액세스 토큰은 기존 JWT 만료 설정(기본 60분)을 따릅니다. 갱신 자격은 256비트 난수이며 DB에는 SHA-256 해시만 저장합니다. 같은 자격으로 재시도할 수 있어 갱신 응답이 유실되어도 다음 요청에서 복구됩니다. 90일 미사용 또는 최초 발급 후 1년이 지나면 다시 로그인해야 합니다. 비밀번호 재설정 후 이전 갱신 자격도 무효입니다. 로그아웃은 오프라인에서도 기기의 자격을 삭제하며 서버 폐기는 연결이 가능한 경우에 수행합니다.

Android는 자동 로그인을 선택한 경우에만 액세스/갱신 자격을 하나의 암호화된 secure-storage 항목에 저장합니다. 시작 시와 API 사용 중에 갱신하며 동시 요청은 한 번의 갱신을 공유합니다. 네트워크/서버의 일시적인 오류는 자격을 삭제하지 않습니다. 이전 버전에는 갱신 자격이 없었으므로 기존 액세스 토큰이 만료되면 한 번 다시 로그인하고 자동 로그인을 선택해야 합니다.

검증은 백엔드 단위 테스트와 Flutter 인증 테스트로 수행합니다. 운영 DB/AWS 연동 및 Android Studio에서 실제 런처 미리보기 확인은 별도로 수행해야 합니다.

참고: [Cognito 인증 코드 수명](https://docs.aws.amazon.com/cognito/latest/developerguide/quotas.html), [AdminDeleteUser IAM 요구사항](https://docs.aws.amazon.com/cognito-user-identity-pools/latest/APIReference/API_AdminDeleteUser.html).
