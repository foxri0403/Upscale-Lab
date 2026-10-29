# 기기 배경화면 움직임 및 커서 알림 수정

2026-10-28 UTC 수동 배포 완료. 운영 주소: https://d3bjir1z2hduu0.cloudfront.net/

API와 웹 릴리스는 `motion-20261028-105741`, 번들은 `/assets/index-BBppVKZ0.js`이다. 이전 릴리스 `devices-20261028-093442`를 보존했다. DB migration, GPU endpoint, 인증 자격, CI/CD 설정은 변경하지 않았다.

## 수정 범위

- 기존 Windows 구현은 raised desktop의 시스템 WorkerW 안에 동적 창을 넣었다. 새 구현은 불투명 layered child를 Progman의 아이콘 아래·정적 배경 위에 배치한다. 창 스타일 변경 후 SetWindowPos로 위치와 순서를 적용하며, 상태 확인 시 테마 변경으로 달라진 순서를 복원한다. 마우스 X 이동은 웹과 같은 방향으로 수정했다. WPF 디스플레이 프레임 콜백을 유지한다.
- 자르기·필터·배율을 사용하는 분리 이미지가 정적 이미지 한 장으로 합쳐지던 경로를 수정했다. 서버가 레이어별 픽셀을 처리하고 피사체 알파 채널을 보존한다. 최종 캔버스와 공유 강도로 이동량을 계산하며 원본 프로젝트 레이어를 수정하지 않는다. 이미 처리한 편집 메타데이터는 초기화해 다음 미리보기에서 중복 적용하지 않는다.
- manifest의 mouseEnabled/gyroEnabled를 분리해 Windows는 마우스, Android는 자이로 선택을 따른다. 오래된 manifest는 기존 전체 스위치와 호환되며 저장된 개별 입력 메타데이터가 있으면 응답에서 보완한다. 두 연결 앱은 이미지 캐시를 재사용해도 입력 설정은 갱신한다.
- Android는 fused rotation vector를 우선 사용하고 없으면 원시 자이로를 적분하는 대체 경로를 제공한다. 표시 회전·센서 재개 시 중립을 재설정하고, 화면 가시성과 계정 lease에 따라 구독을 관리한다. 캐시 설정이 바뀌면 실행 중 배경화면 엔진에도 갱신을 알린다. Choreographer 기반 프레임 보간은 유지한다.
- PC 브라우저에서도 자체 센서 없이 휴대폰의 자이로 옵션을 저장할 수 있다. 휴대폰 브라우저의 미리보기는 해당 브라우저의 센서 권한을 사용한다.
- 하단 고정 알림을 커서 옆의 작은 말풍선으로 변경했다. X 버튼을 제거하고 2초간 opacity를 낮춘 뒤 제거한다. 바깥 pointerdown 시 즉시 닫히며 화면 가장자리에서는 위치를 보정한다. 관련 타이머·이벤트는 컴포넌트가 해제될 때 정리한다.

Windows 연결 방식의 근거: [Microsoft SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent), [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos), [Lively 유지보수자의 raised desktop/WPF 호환성 논의](https://github.com/lively-community/lively/issues/2074). 센서 경로는 [Android position sensors](https://developer.android.com/develop/sensors-and-location/sensors/sensors_position)를 참고했다.

## 앱 배포

Windows 1.2.1: `UpscaleLab-Connect-1.2.1-win-x64.exe`, 83,378,018 bytes, SHA-256 `8f93b41bf275b6b70968e969be7d2c7a400be42d2d3582609120ce3850ff7607`.

Android 1.2.1+35: `Upscale_Lab-1.2.1-.apk`, 48,980,954 bytes, SHA-256 `29be3276e4a1ed177b20e61035dee7a0f424aca3338f4b9da13de3f2180f6a1e`.

APK는 apksigner 검증을 통과했고 이전 정식 버전과 같은 팀 인증서(SHA-256 `d2e2dc332f34de8ea72a0d1b4aa2c428306ba7ae4529f26ed6dd504018572b93`)를 사용한다. Windows는 기존과 같이 Authenticode 미서명이다. 새 앱은 웹 다운로드 메뉴에도 게시했다. Windows는 트레이에서 이전 연결 앱을 종료한 후 새 EXE를 실행하고 배경화면을 다시 적용한다. 자동 실행을 사용하면 새 파일 경로로 다시 설정한다. Android는 APK 업데이트 후 자이로를 선택해 적용한다. 최초 OS 라이브 배경화면 승인도 필요하다. 내부 wire mode Live2D는 기존 레이어 규격이며 별도 제품 Live 2D 도구를 활성화하지 않는다.

## 검증과 한계

- 백엔드 190개, Windows 15개, Android 앱 `:app:testReleaseUnitTest` 66개, 운영 HTTPS 웹 테스트 54개 통과.
- Windows는 STA/WinForms/WPF 및 실제 데스크톱 자식 창을 사용한 숨겨진 렌더링 테스트에서 연속 프레임과 오른쪽 마우스 입력에 대한 오른쪽 이동을 확인했다. 사용자 OS 배경화면은 변경하지 않았다.
- 브라우저에서 데스크톱의 자이로 설정 저장, 자르기·필터를 유지한 레이어 적용 요청, 커서 말풍선 위치·X 부재·2초 페이드·바깥 클릭 닫기를 확인했다. 운영 번들에서도 API를 대체한 UI 검증을 통과했다.
- 모바일 브라우저 센서 이벤트와 iOS 권한 API를 각각 모의해 강도 공유, 가로 방향 변환, 저장·초기화·가시성 변경 동작을 확인했다. Android 자이로 계산·입력 선택·캐시 갱신은 앱 테스트로 확인했다.
- 실제 운영 HTTPS API는 임시 계정과 작은 S3 PNG 두 장으로 필터·자르기 후 레이어/알파/이동량 유지, Windows와 Android 명령의 manifest 전달, 자이로 전용 스냅샷을 검증했다. GPU 추론은 실행하지 않았다. 테스트 계정·기기·이미지·S3 파일은 정리했다.
- Android 실기기/에뮬레이터는 연결되어 있지 않았다. 실제 센서 감도, 제조사별 홈/잠금 화면, 사용자 PC에서의 최종 바탕화면 표시 및 실제 FPS는 앱 설치 후 확인해야 한다. 하드웨어에서 60fps를 측정했다고 주장하지 않는다.
- 첫 병렬 테스트 실행에서 기존 DeviceChangeFeedTests의 1초 대기 제한이 한 번 실패했다. 부하가 없는 재실행과 최종 전체 실행은 모두 190개 통과했다. 테스트의 제한 시간을 변경하지 않았다.

배포 검증 로그는 `/home/ubuntu/deploy-backups/motion-20261028-105741/`에 비공개로 보존한다. 소스 검토본과 이전 symlink를 보존했으며, 새 업로드·검증 staging은 정리했다. API는 이전 devices 릴리스와 스키마가 동일하므로 배포 스크립트는 실패 시 이전 API/웹 경로로 복구하도록 구성했다. 자동 배포를 추가하지 않았다. 로컬 로그·도구·캡처는 무시되는 `artifacts/wallpaper-motion/`에 있다.
