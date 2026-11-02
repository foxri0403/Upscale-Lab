# 로컬 저장소 정리 결과

작업일: 2026-11-02 KST. 최초 정리는 로컬 main 작업 폴더에서만 수행했다. 이후 사용자의 명시적 요청으로 GitHub main을 비교하고 정리 변경과 기존 로컬 웹 수정 2개를 커밋·푸시하는 후속 작업을 진행했다. 운영 배포·DB migration·AWS/RunPod 설정 변경과 새 APK/EXE 생성은 수행하지 않았다.

총 **30,651개 파일, 파일 크기 합계 약 22.54GB**를 삭제했다. 이 중 소스·문서·대화 내보내기는 163개(Git 추적 150개, 미추적 13개), 과거 로컬 산출물은 30,488개다. 크기는 삭제 전 파일 길이 합계이며 실제 디스크 할당량 측정값은 아니다.

- [소스·문서 파일별 역할과 삭제 근거](cleanup/deleted-source-files.md)
- [전체 삭제 파일 CSV: 경로·역할·근거·크기·해시](cleanup/deleted-files.csv)
- [기계 판독 검증 결과](cleanup/validation.json)
- [빌드·테스트 실행 로그](cleanup/verification.txt)

## 삭제·통합 범위

| 구분 | 삭제 파일 수 |
|---|---:|
| 전체 채팅 PDF/Markdown | 2 |
| 문서 통합·이전 작업 기록 | 64 |
| 옛 Android 편집·보관함 구현/리소스 | 12 |
| 옛 Android 구현 전용 테스트 | 7 |
| 옛 독립 모바일 Dart 화면·서비스 | 32 |
| 옛 독립 모바일 테스트 | 17 |
| 이전 로컬 빌드·배포·스크린샷·로그 | 30,488 |
| 완료된 일회성 도구 | 1 |
| 코드 수동 백업 사본 | 20 |
| 미사용 백엔드·로컬 추론 어댑터 | 6 |
| 미사용 magnetic lasso와 테스트 | 2 |

모든 파일명 조사 후 웹 main.tsx의 import/worker 그래프, Android main → runCompanion 진입점, Kotlin manifest/companion/renderer 의존성과 C# DI/호출 지점을 확인했다. 컨트롤러·EF·직렬화 등 프레임워크가 동적으로 사용하는 코드는 단순히 이름 참조가 없다고 삭제하지 않았다. 소스 삭제 전 경로·크기·SHA-256을 기록했고 삭제 후 CSV의 모든 경로가 존재하지 않는지 확인했다.

## 현재 서비스에 필요한 변경

| 파일/영역 | 정리 내용과 유지한 동작 |
|---|---|
| mobile/lib/main.dart | 이미 사용 중인 runCompanion만 남기고 도달하지 않는 legacyMain·옛 독립 앱 화면을 제거 |
| Android MainActivity.kt | 기존 upscale/companion handler와 onResume의 pollSoon 유지; 옛 live_layer 편집·보관함·다운로드 채널 제거 |
| WallpaperPlatform.kt | 삭제한 StaticWallpaperController 안의 현재 사용 중인 OS 인터페이스를 독립 파일로 분리 |
| AndroidManifest.xml | 옛 FileProvider·공유 경로·사진 읽기/쓰기 권한·ML Kit 모델 metadata 제거; 현재 foreground service·boot receiver·WallpaperService 유지 |
| mobile/pubspec.yaml·lock / Gradle | 미사용 http·secure-storage·image-picker·sensors Dart 패키지와 종속 패키지 정리; OS 센서·자격 저장은 Kotlin 구현 유지; 옛 편집용 Exif 의존성 제거 |
| backend Program.cs·설정 | 소비자가 없는 Replicate DI·설정 제거; 현재 RunPod·결제·인증·기기 서비스 유지 |
| SeeThroughOptions.cs | 삭제한 로컬 Python 실행기 전용 속성 제거; 캐릭터 RunPod 설정·manifest 검증 유지 |
| RunPodUpscaleService.cs | 호출/테스트 참조가 전혀 없는 UpscaleToResolutionAsync 타일 합성 메서드 제거; 현재 UpscaleAsync와 S3 전송 경로 유지 |
| web/tests/image-tags.test.mjs | 삭제한 모바일 태그 파일과 비교하던 검사를 현행 API GalleryTagCatalog와 비교하도록 변경; UI 태그 순서는 유지 |
| mobile/test/companion_test.dart | 실제 앱 진입점·상태 조회·같은 계정 로그인·Live 2D 승인 요청·동기화 중지·로그아웃·오류 후 재시도 검사 |
| .gitignore | 이전 코드 백업 변형과 대화 내보내기·정리 임시 파일 재유입 방지 |

## 문서 통합

| 현재 문서 | 통합한 내용 |
|---|---|
| [현재 서비스 동작](service-behavior.md) | 보관함·공유 가져오기·다운로드 수·편집 자동 저장·기기 UI·운영 경계 |
| [피사체 선택](subject-selection.md) | 자동 분리·ROI 추가/제거·RGB 채널 계약·직접 알파 마스크 호환·검증 |
| [Live 2D](live-2d.md) | 캐릭터 파츠·레이어 순서 API·웹 리깅·정지 PNG 적용과 미연결 기능 한계 |
| [인증 API](auth-api.md) | 로그인·메일 인증·갱신 자격·세션 폐기·만료 인증 대기 정보 정리 |
| [배포](deployment.md) | 현행 수동 API/웹·DB migration·다운로드 파일 보존·복구 절차 |
| [README](../README.md)·[웹](../web/README.md)·[Android](../mobile/README.md) | 현재 구성과 실행·검증·서명·설치 파일 규칙 |

날짜별 배포 경로·당시 테스트 수·이미 대체된 UI 설명·ZIP 병합 전달 메모는 제거했다. 라이선스·출처와 현재 계약은 해당 문서로 옮겼다. 시작 시 미추적이던 작업 메모도 같은 기준으로 조사·통합했고 그 파일명과 해시는 전체 목록에 남겼다. 문서 내부의 삭제 파일 링크도 현재 통합 문서로 갱신했다.

## 검증 결과

| 검증 | 결과 |
|---|---|
| 백엔드 컴파일 및 전체 xUnit | 249 통과, 실패 0 |
| 웹 단위/계약 테스트 | 99 통과, 실패 0, 원격 환경 테스트 14 건너뜀 |
| 웹 build:cloud | TypeScript + Vite 성공; 원본/산출물 설치 파일 크기·SHA-256 검증 |
| Windows 연결 앱 컴파일·테스트 | 15 통과 |
| Flutter analyze | 문제 0 |
| 현재 Flutter 연결 앱 회귀 | 2 통과 |
| Kotlin compileDebugKotlin·testDebugUnitTest | 빌드 성공, 24 통과 |
| 원래 수정 중이던 웹 파일 2개 | CropEditor.tsx·ImageInformation.tsx의 시작/종료 SHA-256 동일 |
| 삭제 manifest | 모든 기록 경로 삭제 확인 |
| Git diff / Markdown 링크 | 공백 오류 및 남은 로컬 문서 깨진 링크 없음 |

기존 웹 102개 통과에서 현재 99개 통과로 줄어든 3개는 제거한 미사용 magnetic-lasso의 전용 테스트다. 옛 독립 모바일 화면/보관함 테스트도 대상 구현과 함께 제거했으며 현재 연결 앱 테스트로 대체했다. Flutter의 사용하지 않는 직접/전이 패키지 53개가 lockfile에서 빠졌다. Gradle의 기존 도구 버전·폐기 예정 API 경고는 남아 있으며 이번 정리에서 도구 업그레이드를 섞지 않았다.

## 보존한 파일과 이유

- Windows/Android 연결 앱: 현재 웹의 기기 보고·정적/Live 2D 배경화면 적용·삭제에 필요하다.
- web/public/downloads의 manifest·Windows EXE·Android APK와 최신 artifacts 1.2.2 설치 파일: 실제 다운로드와 다음 cloud 빌드에 필요하며 해시·크기를 검증했다.
- 현재 RunPod 처리기·GPU worker/Dockerfile·모델·캐릭터 manifest reader·관련 테스트: 현재 클라우드 처리 또는 수동 워커 빌드에 사용한다.
- DB migrations·ModelSnapshot·기존 엔티티·호환 API: 기존 DB 업그레이드와 신규 DB 생성·클라이언트 호환에 필요하다.
- SeparationLab·SlimSAM worker·transformers·브라우저 테스트 진입 파일: 현재 개발용 검증 경로다.
- PhotoCraft LICENSE·배포 고지·샘플 이미지 출처: 실행/개발 자산과 재배포 고지에 필요하다.
- .git·SDK·NuGet/node_modules/Gradle·현재 빌드 캐시·로컬 설정/서명 파일: 소스 이력·개발 환경·비밀값을 보존한다.

다음 경로는 일괄 삭제에서 제외했다. 등록된 Git 작업 트리는 별도 작업 상태와 .git 연결을 보존하고, running.json이 있는 실험실 폴더는 로컬/원격 종료·정리 정보가 필요할 수 있어 유지했다. 이번 요청은 로컬 범위이므로 원격 실행 상태를 조회하거나 종료하지 않았다. SDK 도구·캐시와 최신 설치 산출물도 유지했다.

- `artifacts/live2d-merge-20261101`
- `artifacts/selection-billing-20261030`
- `artifacts/windows`
- `artifacts/Upscale_Lab-1.2.2-.apk`
- `tmp/tools`
- `tmp/uv-cache`
- `artifacts/separation-lab-20261030-115437-1fc46a9a`
- `artifacts/separation-lab-20261030-115645-10f60cad`

## 정상 작동 판단과 한계

**현재 로컬 소스는 컴파일·웹 cloud 빌드·회귀 테스트 기준으로 정상이며, 확인된 미사용 파일 제거로 현행 서비스 경로가 끊긴 흔적은 없다.** 운영 서버는 변경하지 않았으므로 이번 정리로 배포 서비스가 바뀌지 않는다. 시작 시 수정 중이던 웹 두 파일도 그대로 보존했다.

운영/API·AWS·실제 GPU 추론·결제 승인·Windows/Android OS 적용은 이번 작업에서 실행하지 않았다. 웹의 원격 테스트 14개도 환경을 지정하지 않아 건너뛰었다. 따라서 실제 클라우드 모델 품질·설정과 실기기 동작까지 새로 검증했다는 의미는 아니다. 저장소 기본 업스케일 worker와 실제 endpoint 이미지의 해상도/S3 계약도 배포 시 별도로 맞춰야 하며, 이 정리에서 endpoint나 worker 이미지를 바꾸지 않았다.

구형 독립 모바일 편집기를 legacyMain으로 별도 실행하는 비공식 개발 경로와 옛 로컬 갤러리 가져오기 기능은 제거했다. 현재 배포 진입점은 원래부터 연결 앱이었다. 기기에 이미 저장된 사용자 이미지 데이터는 삭제하지 않았다. 새 APK/EXE를 만들지 않았으므로 현재 배포 파일의 버전·서명·내용도 유지된다.

Git 추적 파일은 추후 Git 이력으로 복원할 수 있다. Git에 없던 과거 산출물·대화 내보내기·미추적 문서는 실제로 삭제했으며 별도 백업을 만들지 않았다. 삭제 목록은 복원용 사본이 아니다.

## GitHub main 비교

후속 동기화 전 `git fetch origin main`과 GitHub compare API로 확인한 원격 main은 `1d7ef0f064d0b5464950e0442636c76715facde0`였고 로컬 HEAD와 일치했다. ahead/behind가 모두 0이므로 추가 병합 없이 현재 변경을 반영한다. 기존 로컬 `CropEditor.tsx`의 확대 미리보기 이동과 `ImageInformation.tsx`의 표시 문구 수정도 포함한다. 자세한 비교 근거는 [동기화 기록](cleanup/github-sync.json)을 따른다. GitHub의 자동 배포 workflow는 없으며 이번 작업에서 배포를 실행하거나 추가하지 않는다.
