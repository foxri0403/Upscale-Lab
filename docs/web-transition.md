# 웹 중심 전환

현재 제품은 C# ASP.NET Core API·PostgreSQL·S3와 React/TypeScript 웹을 사용한다. Windows/Android 연결 앱은 기존 C#/Kotlin 배경화면 엔진을 재사용한다. 이미지 제작·편집·보관함·공유는 웹에서 수행하고 설치형 앱은 계정 로그인·기기 보고·OS 적용을 담당한다.

## 역할

| 구성 | 역할 |
|---|---|
| 웹 | 업로드, 편집, 작업 상태, Live 2D 미리보기, 공유/다운로드, 계정별 기기 관리 |
| 기존 .NET API | 소유권 검증, 로그인, 작업 관리, 결과물 고정, 기기 명령 및 상태 보고 |
| 클라우드 AI | 팀원이 모델 및 공급자 연결 구현. [처리 계약](cloud-processing.md)의 `IProjectImageProcessor`로 연결 |
| Windows 에이전트 | C# 재사용, 사용자 세션에서 화면 수집/정적 적용/Live 2D 렌더링 |
| Android 에이전트 | Kotlin `WallpaperService`, 센서, 영구 캐시 재사용. 계정 로그인과 기기 키를 함께 사용 |

신규 서버 API는 [에이전트 연동 규격](agent-api.md)을 따른다. 기존 `/api/devices`, `/api/projects`, `/api/gallery` 경로는 유지한다. 새 `/api/wallpapers`는 정적/Live 2D 결과물을 공유하고 기기에 전달하는 공통 경로다. 기존 이미지 갤러리의 다운로드 카운트 정책은 변경하지 않는다.

## 데이터와 적용 흐름

1. 프로젝트 원본을 S3에 업로드한다. 클라우드 작업은 요청 종류/배율과 처리 결과를 `processing_jobs`에 기록한다.
2. 완료한 원본/업스케일 이미지 또는 레이어를 `/api/wallpapers`에서 독립 결과물로 고정한다. 결과물 ID가 버전 ID이며, 수정할 때는 새 ID를 만든다.
3. S3 `users/{userId}/wallpapers/{assetId}/`에 파일을 복사하고 DB에는 manifest와 object key만 보관한다. 프로젝트 삭제·레이어 재처리와 결과물 수명은 분리된다.
4. 화면별 적용 명령을 DB에 저장한다. 같은 대상의 대기 명령은 최신 요청으로 대체하며, 명령 ID/sequence는 재시도와 오래된 완료 보고를 구분한다.
5. 에이전트가 파일을 캐시하고 OS 적용을 확인한 뒤 `Applied`를 보고해야 실제 적용 상태가 바뀐다. Android 최초 Live 2D 활성화는 `NeedsUserAction`을 사용할 수 있다.

`device_displays`는 물리/논리 화면을 나타내고 Android의 Home/Lock은 적용 대상으로 구분한다. 목록에서 사라진 화면은 연결 해제로 표시하고 화면 ID와 설정을 보존한다. 마지막 응답 2분 이내라는 `recentlySeen`은 최근 통신 여부이며 전원/네트워크 상태의 확정값이 아니다.

결과물 삭제는 보관함에서 숨기고 공유를 해제하는 archive다. 적용 중인 기기가 재시작해도 복구할 수 있게 파일은 유지한다. 영구 스토리지 정리는 활성 적용/명령 참조와 보존 기간을 고려하는 별도 작업으로 둔다. 공유 해제 후 이미 내려받거나 개인 보관함에 가져온 복사본은 회수하지 않는다.

## 후속 클라이언트 작업

- 웹 `web/` 프런트엔드는 API/OpenAPI에서 TypeScript 타입을 맞추고 모바일 내부 `MethodChannel`을 호출하지 않는다. Canvas/WebGL 미리보기는 공통 manifest를 읽는다.
- EXE/APK는 로그인, 기기 연결, OS API 수집, 다운로드/검증/캐시, 명령 보고를 구현한다. 다운로드 파일의 형식/크기를 검사하고 만료 URL은 재발급한다.
- 배경 분리는 Android와 웹 모두 RunPod의 BiRefNet_HR-matting + LaMa를 사용한다. [분리 워커](../gpu-worker-runpod/layers/README.md)는 별도 endpoint로 수동 배포한다. 기기 내 ML Kit 추론과 See-through 운영 연결은 사용하지 않는다. Android는 gyro 기반 회전 센서, Windows는 마우스, 웹은 포인터/권한을 허용한 기울기 센서로 동작하며 ON/OFF를 결과물에 보존한다.
- 기존 모바일의 비공개 로컬 보관함 데이터는 이번 저장소 정리로 삭제하지 않는다. 현재 연결 앱에는 해당 보관함을 열거나 가져오는 UI가 없다.
- Android의 임의 외부 배경화면 원본 읽기, 최초 Live 2D 활성화, 제조사별 Home/Lock 지원, 절전 중 즉시 수신은 보장하지 않는다. 확인 불가 상태를 UI에 표시한다.
- Windows Live 2D의 바탕화면 호스팅과 다중 모니터 복원은 실제 기기에서 검증한다. 정적 이미지 적용과 동적 렌더러의 수명을 분리한다.

## 배포와 정리

`AddWebDeviceControl` 마이그레이션을 리뷰 후 수동 적용한다. 새 테이블 4개와 기존 device/job 필드만 추가하고 기존 이미지/계정 테이블을 삭제하지 않는다. API 배포와 DB 변경은 자동 실행하지 않는다. [배포 문서](deployment.md)를 따른다.

실행 경로가 없는 독립 모바일 편집/보관함·일회성 이전 코드, 로컬 GPU 어댑터와 미사용 Replicate 클라이언트를 제거했다. 현재 연결 앱의 캐시·배경화면 렌더러·센서·계정/기기 인증은 유지한다. 클라우드 설정과 비밀은 브라우저에 전달하지 않는다. [정리 보고서](repository-cleanup.md)에 파일별 근거와 검증 범위를 기록한다.

.NET 8에서 기존 코드 호환성을 검증한다. 장기 운영은 의존성 검증을 거쳐 .NET 10 LTS로 옮기는 별도 변경을 계획한다.
