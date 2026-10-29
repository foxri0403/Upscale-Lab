# 기기 및 웹 연동 API v1

## 인증

Live 2D manifest에는 선택적인 `motionEnabled` boolean을 추가했다. 생략한 기존 manifest는 true로 처리한다. false이면 같은 레이어를 중앙 위치에 합성하되 센서 구독과 지속 프레임 갱신을 중단한다. `POST /api/wallpapers/{id}/snapshot`은 소유자만 호출할 수 있으며 `{ "motionEnabled": false, "strength": 35 }`로 새 불변 결과물을 생성한다. 원래 결과물이나 기존 기기 적용은 수정하지 않는다.

계정과 기기를 함께 검증한다. 에이전트의 모든 `/api/agent/*` 요청은 다음 두 헤더가 필요하다.

```http
Authorization: Bearer <로그인 계정의 accessToken>
X-Device-Token: <연결된 기기의 256비트 키>
```

JWT `sid`의 서버 로그인 세션이 유효하고, 이메일 인증 계정이며, 비밀번호 fingerprint가 일치하고, 계정 ID가 기기 소유자와 같아야 한다. 키만 전송하거나 다른 계정 로그인, 만료/폐기된 세션, 폐기된 기기 키는 거부한다. 키는 DB에 SHA-256 해시만 저장한다. 로그아웃 API가 서버에 도달하면 해당 로그인 세션의 새 API 요청은 즉시 차단된다. 다른 기기에서 별도로 로그인한 동일 계정의 세션은 유지한다.

신규 계정 세션이 없는 기존 JWT는 기존 API와의 호환에만 사용하며, 새 기기 제어/결과물 관리에는 재로그인이 필요하다. 에이전트는 기기 자체에서 계정 로그인/갱신을 수행하고 OS 보안 저장소에 필요한 자격을 저장한다. 로그아웃/계정 전환 시 동기화, 새 적용, Live 2D 실행을 중지하고 계정 자격을 지워야 한다. 이 클라이언트 제어는 [Windows/Android 연결 앱](companion-apps.md)에 구현되어 있다. 서버는 이미 캐시된 파일이나 OS에 설정된 정적 배경화면을 원격으로 강제 회수하지 못한다.

## 웹/계정 API

모든 관리 요청에 계정 Bearer JWT를 사용한다. `AccountSession` 경로는 유효한 `sid`가 필요하다.

| Method | 경로 | 용도 |
|---|---|---|
| GET/POST | `/api/devices` | 기존 기기 목록/등록 API 유지 |
| GET | `/api/devices/updates?after=<cursor>` | 현재 계정의 기기 변경 대기. 첫 응답의 cursor로 다음 변경을 대기 |
| POST | `/api/devices/{id}/agent-token` | 연결 키 발급/회전. 평문 키는 이 응답에서만 제공 |
| DELETE | `/api/devices/{id}/agent-token` | 기기 연결 해제, 대기 명령 대체 처리 |
| GET | `/api/devices/{id}/state` | 화면 목록, 지원 기능, 마지막 통신, 적용 요청/실제 관측 상태 |
| POST | `/api/devices/{id}/commands` | 내 결과물을 연결된 내 화면에 적용 요청 |
| POST | `/api/devices/{id}/wallpapers/clear` | 선택 화면의 배경화면 삭제 요청. 최신 연결 앱 필요 |
| GET/POST | `/api/wallpapers` | 내 결과물 최대 100개/불변 결과물 생성 |
| GET | `/api/wallpapers/shared` | 공개 결과물 최대 100개. 익명 허용 |
| GET | `/api/wallpapers/{id}/manifest` | 소유자 또는 공개 결과물의 다운로드 URL과 렌더링 설정 |
| POST | `/api/wallpapers/{id}/import` | 공개 결과물을 내 S3 prefix로 복사. 내 기기에 적용할 때 사용 |
| PUT | `/api/wallpapers/{id}/sharing` | `{ "isPublic": true }` 또는 false |
| DELETE | `/api/wallpapers/{id}` | archive 및 공유 해제. 기기 복구용 파일 보존 |

생성 요청 예:

```json
{ "projectId": "<uuid>", "title": "내 배경", "mode": "Live2D" }
```

정적 원본은 `mode: Static`을 사용하고, 업스케일 결과는 추가로 완료 작업의 `processingJobId`를 지정한다. Live2D는 현재 프로젝트 레이어와 설정을 고정한다. `processingJobId`로 이전 레이어를 선택하는 기능은 제공하지 않는다.

적용 요청 예:

```json
{ "requestId": "<새 uuid>", "displayId": "<state의 화면 uuid>", "assetId": "<내 결과물 uuid>", "target": "Desktop" }
```

Windows는 `Desktop`, Android는 `Home` 또는 지원하는 경우 `Lock`이다. 두 대상을 적용하려면 각각의 명령을 생성한다. `requestId`는 재전송 시 동일하게 사용한다. 같은 ID에 다른 내용은 409다. 반환된 `Pending`은 OS 적용 완료를 의미하지 않는다.

## 에이전트 API

| Method | 경로 | 용도 |
|---|---|---|
| GET | `/api/agent/state` | 현재 연결의 기기/화면 상태와 inventoryRevision |
| GET | `/api/agent/updates?after=<cursor>` | 이 기기의 새 명령·연결 해제 알림 대기. 계정 JWT와 기기 키 모두 필요 |
| POST | `/api/agent/heartbeat` | 화면/기기 전체 스냅샷 보고 |
| GET | `/api/agent/commands` | 재시도 가능한 최신 대기/다운로드/사용자 확인 명령 |
| GET | `/api/agent/commands/{id}` | 최신 sequence·유효기간·연결 상태를 검증한 명령. 삭제 직전 재확인 |
| GET | `/api/agent/commands/{id}/manifest` | 해당 명령의 파일 URL 갱신. 현재 적용 완료 명령의 복구도 허용 |
| POST | `/api/agent/commands/{id}/report` | 명령 처리 결과 보고 |

변경 대기는 최대 20초 뒤 `{ "cursor": "<uuid>" }`를 반환한다. cursor가 바뀌면 즉시 state/commands를 다시 읽는다. 동일 cursor는 변경 없음이며 다시 대기한다. 최초 연결·재연결·서버 재시작은 cursor를 재설정해 최신 상태를 따라잡는다. cursor 확인과 구독은 원자적으로 처리하며 알림은 계정/기기별로 분리한다. heartbeat·상태 보고는 웹만 깨워 연결 앱의 자기 알림 반복을 막는다. 데이터 변경은 기존 서비스 계층에서 수행하고 알림에는 자격·이미지·상태 데이터를 담지 않는다. DB migration은 추가하지 않는다.

Heartbeat 예:

```json
{
  "revision": 1,
  "osVersion": "Windows 11",
  "agentVersion": "1.0.0",
  "supportsLive2D": true,
  "supportsLockScreen": false,
  "displays": [{
    "nativeId": "stable-monitor-id",
    "name": "Monitor 1",
    "width": 2560,
    "height": 1440,
    "scale": 1.25,
    "rotation": 0,
    "offsetX": 0,
    "offsetY": 0,
    "isPrimary": true,
    "isConnected": true,
    "wallpapers": [{ "target": "Desktop", "observation": "Unavailable", "assetId": null }]
  }]
}
```

화면은 최대 16개이며 `nativeId`는 같은 설치 내에서 동일 화면을 식별하는 안정적인 값이어야 한다. 제조사·모델·CPU 구조는 수집하거나 state에 포함하지 않는다. 구형 연결 앱이 전송하는 `manufacturer`, `model`, `architecture`는 무시하며 기존 저장값은 다음 유효 heartbeat에서 비운다. 각 display의 `refreshRateHz`는 null 또는 1~1000의 유한한 실수다(59.94 등 보존). `canApplyWallpaper`는 기본 true이며 Android 외부 화면처럼 조회만 지원하는 화면은 false로 보낸다. false인 화면의 적용 요청은 409로 거부한다. 화면 필드들은 기기 state에도 포함된다. 주사율을 알 수 없을 때 60으로 추정하지 않고 null을 보낸다.

`revision`은 서버의 inventoryRevision보다 커야 한다. 에이전트 재시작 시 state를 읽고 다음 값을 사용하며 응답 유실/409 시 재조회한다. 키 회전 시 revision은 0으로 초기화된다. 누락된 화면은 disconnected로 남긴다. 해상도는 픽셀이며 물리 인치로 해석하지 않는다. Android Home/Lock은 화면 개수가 아니다.

관측 상태는 `Unknown`, `Managed`, `External`, `Unavailable`, `Cleared`이다. `Managed`일 때만 내 결과물 `assetId`가 필요하다. `Cleared`는 OS 배경화면 초기화를 확인한 상태이며 assetId가 없다. 외부 배경화면의 원본을 읽을 수 없으면 `Unavailable`을 보고한다. 보고하지 않은 대상의 이전 관측값은 마지막 관측 시각과 함께 남으므로 웹은 최신 확정값으로 표시하지 않아야 한다.

배경화면 삭제는 `{ "requestId": "<새 uuid>", "displayId": "<화면 uuid>", "target": "Desktop" }`를 `POST /api/devices/{id}/wallpapers/clear`로 전송한다. Windows는 Desktop, Android는 Home/Lock을 각각 요청한다. `action: Clear`, `assetId: null`인 명령을 반환하며 이미지 manifest는 409로 거부한다. 삭제 대기 중 state의 `pendingRemoval`은 true이고 기존 실제 관측 이미지가 유지된다. 기기가 OS 삭제 성공 후 Applied를 보고해야 관측 이미지가 비워지고 Cleared로 전환된다. 실패하면 기존 관측 이미지를 유지한다. 이미지 라이브러리나 다른 화면은 삭제하지 않는다.

연결 앱 1.2.0부터 heartbeat에 `supportsWallpaperRemoval: true`를 보내며 state에도 해당 지원 여부가 포함된다. 생략한 기존 앱은 false로 처리하고 삭제 요청을 거부하며 삭제 명령을 전달하지 않는다. 키 회전·폐기 시 지원 여부를 초기화하므로 새 heartbeat가 필요하다. 명령의 `action`은 Apply/Clear이며 생략한 기존 응답은 Apply로 처리한다. Clear 실행 전에는 state의 대상 sequence·desiredAssetId(null) 및 `GET /api/agent/commands/{id}`를 재검증한다. 두 계정/기기 자격은 삭제에도 동일하게 필요하다.

명령 상태 보고 예:

```json
{ "status": "NeedsUserAction", "error": "기기에서 Live 2D 최초 활성화가 필요합니다." }
```

기기는 `Downloading`, `NeedsUserAction`, `Applied`, `Failed`만 보고한다. 서버는 `Pending`, `Superseded`, `Expired`도 사용한다. 명령은 24시간 유효하며 같은 화면/대상의 이전 미완료 명령은 대체된다. 이전 sequence의 완료는 409다. 동일한 Applied/Failed 보고는 안전하게 재전송할 수 있다. 409면 state/commands를 다시 읽는다. 401이면 갱신 또는 재로그인하고, 복구되지 않으면 작업을 중지한다.

## Manifest와 다운로드

응답은 camelCase, enum은 문자열이다. `schemaVersion: 1`, `assetId`, `mode`, `width`, `height`, `files`, `urlsExpireAt`를 제공한다. 각 file의 `source`는 15분짜리 서명 URL이며 저장된 DB manifest에는 S3 object key만 있다. URL을 영구 저장하지 말고 bytes를 로컬에 캐시한다. 공유 해제/로그아웃으로 이미 발급한 S3 URL의 남은 유효기간을 취소하지는 못한다.

1.2.1부터 manifest에 선택적인 `mouseEnabled`, `gyroEnabled`가 포함된다. `motionEnabled`는 전체 스위치이고 Windows는 mouseEnabled, Android는 gyroEnabled를 사용한다. 두 필드가 모두 없거나 null인 기존 manifest는 기존 motionEnabled 동작을 따른다. 예전 저장 결과물에 개별 입력 메타데이터가 있으면 응답에서 해당 설정을 보완하며 원본 JSON은 변경하지 않는다. 캐시된 픽셀을 재사용하더라도 새 manifest의 입력 설정은 갱신한다.

배경 분리 결과의 Create/Snapshot 요청은 선택적인 `render: { strength, filter, crop, scale, foreground }`를 받는다. 서버가 각 레이어를 독립적으로 자르고 필터·배율을 반영해 알파 채널을 보존한다. 강도는 최종 캔버스의 3.5%에 0~100 비율을 곱한다. 원본 프로젝트의 공용 레이어 이동량을 수정하지 않는다. 결과물의 편집 메타데이터는 이미 적용한 필터·자르기·배율을 초기화해 중복 적용을 방지한다. wire mode의 Live2D는 기존 레이어 운송 규격 이름이며 별도 제품 Live 2D 기능을 활성화하지 않는다.

파일은 `order` 오름차순으로 합성한다. `role`은 정적 이미지의 `image` 또는 기존 LayerType(`Background`, `Body`, `Hair` 등)이다. PNG 레이어는 공통 width/height 캔버스를 사용한다. `x/y`는 원본 캔버스 중심 기준 이동량(px), rotation은 시계 방향 도, scale은 배율이다. 먼저 cover 배율 `s = max(표시너비/width, 표시높이/height)`를 구하고 x/y와 movementX/Y에 s를 곱해 기기 픽셀로 변환한다. 시차 입력은 자이로 ±30도 또는 미리보기 영역 중심으로부터의 마우스/터치를 [-1, 1]로 제한한다. Background는 시차를 적용하지 않고 Windows 1.2.1의 마우스는 X에 입력X*movementX*s를 더하고 Android 센서는 X에 -입력X*movementX*s를 더한다. 두 플랫폼의 Y에는 입력Y*movementY*s를 더한다. depth는 편집 메타데이터이며 별도로 이동량에 다시 곱하지 않는다. 기존 Android의 3.5% 고정 이동 규칙과 로컬 manifest는 그대로 남으므로 후속 에이전트 어댑터에서 이 공통 규격을 읽도록 연결해야 한다.

일반 다운로드는 manifest의 파일 URL을 이용한다. Live 2D 내보내기는 클라이언트가 manifest와 PNG를 묶고 로컬 파일명으로 URL을 치환한다. 서버 ZIP 생성 API는 이번 범위에 없다.

## 실시간 알림

`/hubs/notifications`에서 `DeviceChanged { deviceId }`를 받으면 state를 재조회한다. 알림은 활성 로그인 세션 그룹으로만 전송하고 JWT 만료 시 연결을 닫는다. 알림 유실은 허용하며 실제 명령은 DB에서 복구한다. 에이전트는 앱 실행 중 polling, Android 백그라운드에서는 후속 FCM/WorkManager 연동을 사용한다. 과도한 polling을 피하고 네트워크 오류에는 지수 backoff를 적용한다.
