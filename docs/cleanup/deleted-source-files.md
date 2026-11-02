# 삭제한 소스·문서 파일별 목록

2026-11-02 KST. 이전부터 존재하던 파일 중 소스·문서·대화 내보내기 163개다. 그 밖의 로컬 산출물은 [전체 CSV](deleted-files.csv)에 파일마다 기록했다. SHA-256은 소스/문서의 삭제 직전 값이며 산출물은 경로·크기를 기록했다.

## 옛 독립 모바일 Dart 화면·서비스 (32)

| 파일 | 이전 역할 | 삭제 근거 / 통합 위치 |
|---|---|---|
| `mobile/lib/gallery_preview_page.dart` | 옛 앱의 보관함 이미지 확대·공유·배경화면 적용 화면 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/image_editor_page.dart` | 옛 앱의 자르기·필터 이미지 편집 화면 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/image_registration_page.dart` | 옛 앱의 사진 선택·기능 설정·보관함 등록 화면 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/live_2d_page.dart` | 옛 앱의 클라우드 분리·Live 2D 미리보기와 적용 화면 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/models/gallery.dart` | 옛 모바일 갤러리 게시물·응답 데이터 모델 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/models/gallery_tag_codec.dart` | 옛 단일 태그 서버용 다중 태그 설명 metadata 인코딩 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/models/gallery_tags.dart` | 옛 모바일 태그 목록과 정규화 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/models/image_editor.dart` | 옛 모바일 자르기·필터 편집 상태 모델 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/models/image_ratio.dart` | 옛 모바일 자동/수동 비율 선택 모델 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/models/library_order.dart` | 옛 모바일 보관함 정렬·드래그 순서 모델 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/models/live_layer_models.dart` | 옛 Flutter 프로젝트·레이어·움직임 응답 모델 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/models/local_image.dart` | 옛 계정별 기기 보관함 이미지 metadata 모델 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/models/wallpaper_save_selection.dart` | 옛 모바일 저장/배경화면 대상 선택 모델 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/services/api_client.dart` | 옛 Flutter 인증·프로젝트·갤러리 HTTP 클라이언트 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/services/auth_session_store.dart` | 옛 Flutter 자동 로그인 secure-storage 어댑터 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/services/image_editor_service.dart` | 옛 Flutter 편집 작업용 네이티브 호출 어댑터 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/services/live_2d_service.dart` | 옛 Flutter 분리 결과·임시 레이어·적용 네이티브 호출 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/services/local_image_store.dart` | 옛 Flutter 계정별 기기 보관함 호출 어댑터 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/services/wallpaper_service.dart` | 옛 Flutter 정적/라이브 배경화면 적용 호출 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/theme/app_theme.dart` | 옛 독립 앱 라이트/다크 테마와 시스템 UI 설정 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/unified_home.dart` | 옛 앱의 보관함·탐색·기기·정렬 통합 홈 화면 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/widgets/app_components.dart` | 옛 독립 모바일 UI 공통 폼·스크롤 구성 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/widgets/device_display_builder.dart` | 옛 Flutter 기기/화면 표시 위젯 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/widgets/gallery_tag_picker.dart` | 옛 Flutter 다중 태그 선택 UI | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/widgets/image_edit_dialog.dart` | 옛 Flutter 이미지 이름·태그 수정 대화상자 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/widgets/image_ratio_selector.dart` | 옛 Flutter 이미지 비율 선택 UI | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/widgets/live_2d_preview.dart` | 옛 Flutter 고정 배경·전경 자이로 미리보기 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/widgets/local_image_card.dart` | 옛 로컬 보관함 이미지 카드·메뉴 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/widgets/parallax_preview.dart` | 옛 Flutter 서버 레이어 시차 미리보기 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/widgets/registration_image_canvas.dart` | 옛 모바일 사진 등록·편집 캔버스 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/widgets/wallpaper_apply_dialog.dart` | 옛 모바일 홈·잠금 배경화면 대상 선택창 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |
| `mobile/lib/widgets/wallpaper_remove_dialog.dart` | 옛 모바일 배경화면 삭제 확인창 | 현재 main → runCompanion 진입 경로에서 참조하지 않음 → `web/src; mobile/lib/companion.dart` |

## 옛 독립 모바일 테스트 (17)

| 파일 | 이전 역할 | 삭제 근거 / 통합 위치 |
|---|---|---|
| `mobile/test/account_scope_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/app_ui_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/auth_session_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/dark_mode_theme_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/gallery_api_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/gallery_model_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/gallery_sync_widget_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/image_editor_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/image_ratio_selector_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/image_ratio_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/library_order_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/live_2d_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/local_delete_preserves_shared_post_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/local_image_model_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/server_configuration_dialog_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/wallpaper_apply_dialog_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |
| `mobile/test/wallpaper_service_test.dart` | 폐기하는 독립 모바일 UI/API의 회귀 테스트 | 검증 대상 화면과 서비스가 현재 앱에서 실행되지 않음 → `mobile/test/companion_test.dart` |

## 옛 Android 편집·보관함 구현/리소스 (12)

| 파일 | 이전 역할 | 삭제 근거 / 통합 위치 |
|---|---|---|
| `mobile/android/app/src/main/kotlin/com/livelayer/app/GalleryImageMetadata.kt` | 옛 로컬 갤러리 이미지의 계정·이름·태그 metadata 처리 | 옛 MethodChannel 제거 후 현재 companion/WallpaperService에서 사용하지 않음 → `CompanionRuntime.kt; CompanionCache.kt` |
| `mobile/android/app/src/main/kotlin/com/livelayer/app/ImageOwnerMetadata.kt` | 옛 외부 이미지 파일의 계정 소유권 metadata 읽기/쓰기 | 옛 MethodChannel 제거 후 현재 companion/WallpaperService에서 사용하지 않음 → `CompanionRuntime.kt; CompanionCache.kt` |
| `mobile/android/app/src/main/kotlin/com/livelayer/app/LayerPixels.kt` | 옛 Live 2D 편집의 레이어 픽셀·알파 처리 | 옛 MethodChannel 제거 후 현재 companion/WallpaperService에서 사용하지 않음 → `CompanionRuntime.kt; CompanionCache.kt` |
| `mobile/android/app/src/main/kotlin/com/livelayer/app/LegacyGalleryImporter.kt` | 옛 외부 갤러리 이미지를 비공개 보관함으로 가져오기 | 옛 MethodChannel 제거 후 현재 companion/WallpaperService에서 사용하지 않음 → `CompanionRuntime.kt; CompanionCache.kt` |
| `mobile/android/app/src/main/kotlin/com/livelayer/app/LegacyImageMigration.kt` | 옛 로컬 이미지 저장 구조 이전 | 옛 MethodChannel 제거 후 현재 companion/WallpaperService에서 사용하지 않음 → `CompanionRuntime.kt; CompanionCache.kt` |
| `mobile/android/app/src/main/kotlin/com/livelayer/app/Live2DStore.kt` | 옛 앱의 Live 2D 임시·저장 레이어 편집/복사/활성화 저장소 | 옛 MethodChannel 제거 후 현재 companion/WallpaperService에서 사용하지 않음 → `CompanionRuntime.kt; CompanionCache.kt` |
| `mobile/android/app/src/main/kotlin/com/livelayer/app/LocalImageOwnership.kt` | 옛 로컬 보관함 파일 소유권 판단 | 옛 MethodChannel 제거 후 현재 companion/WallpaperService에서 사용하지 않음 → `CompanionRuntime.kt; CompanionCache.kt` |
| `mobile/android/app/src/main/kotlin/com/livelayer/app/PrivateImageStore.kt` | 옛 계정별 앱 전용 이미지 CRUD·편집·공유 상태 저장소 | 옛 MethodChannel 제거 후 현재 companion/WallpaperService에서 사용하지 않음 → `CompanionRuntime.kt; CompanionCache.kt` |
| `mobile/android/app/src/main/kotlin/com/livelayer/app/StaticWallpaperController.kt` | 옛 직접 정적 배경화면 적용과 원본 캐시 관리; 현행 OS 인터페이스는 분리 보존 | 옛 MethodChannel 제거 후 현재 companion/WallpaperService에서 사용하지 않음 → `WallpaperPlatform.kt; CompanionRuntime.kt` |
| `mobile/android/app/src/main/kotlin/com/livelayer/app/WallpaperImageProcessor.kt` | 옛 모바일 Exif 방향·자르기·필터 이미지 처리 | 옛 MethodChannel 제거 후 현재 companion/WallpaperService에서 사용하지 않음 → `CompanionRuntime.kt; CompanionCache.kt` |
| `mobile/android/app/src/main/kotlin/com/livelayer/app/WallpaperRatio.kt` | 옛 모바일 배경화면 비율과 자르기 좌표 계산 | 옛 MethodChannel 제거 후 현재 companion/WallpaperService에서 사용하지 않음 → `CompanionRuntime.kt; CompanionCache.kt` |
| `mobile/android/app/src/main/res/xml/wallpaper_image_paths.xml` | 옛 외부 이미지 공유 FileProvider 경로 | 연결 앱은 companion 캐시를 사용하며 이 provider를 호출하지 않음 |

## 옛 Android 구현 전용 테스트 (7)

| 파일 | 이전 역할 | 삭제 근거 / 통합 위치 |
|---|---|---|
| `mobile/android/app/src/test/kotlin/com/livelayer/app/GalleryImageMetadataTest.kt` | 옛 네이티브 편집·보관함 구현 테스트 | 삭제한 구현에만 의존 → `CompanionWallpaperRemovalTest.kt; 유지한 렌더러/센서 테스트` |
| `mobile/android/app/src/test/kotlin/com/livelayer/app/ImageOwnerMetadataTest.kt` | 옛 네이티브 편집·보관함 구현 테스트 | 삭제한 구현에만 의존 → `CompanionWallpaperRemovalTest.kt; 유지한 렌더러/센서 테스트` |
| `mobile/android/app/src/test/kotlin/com/livelayer/app/Live2DTest.kt` | 옛 네이티브 편집·보관함 구현 테스트 | 삭제한 구현에만 의존 → `CompanionWallpaperRemovalTest.kt; 유지한 렌더러/센서 테스트` |
| `mobile/android/app/src/test/kotlin/com/livelayer/app/LocalImageOwnershipTest.kt` | 옛 네이티브 편집·보관함 구현 테스트 | 삭제한 구현에만 의존 → `CompanionWallpaperRemovalTest.kt; 유지한 렌더러/센서 테스트` |
| `mobile/android/app/src/test/kotlin/com/livelayer/app/PrivateImageStoreTest.kt` | 옛 네이티브 편집·보관함 구현 테스트 | 삭제한 구현에만 의존 → `CompanionWallpaperRemovalTest.kt; 유지한 렌더러/센서 테스트` |
| `mobile/android/app/src/test/kotlin/com/livelayer/app/StaticWallpaperControllerTest.kt` | 옛 네이티브 편집·보관함 구현 테스트 | 삭제한 구현에만 의존 → `CompanionWallpaperRemovalTest.kt; 유지한 렌더러/센서 테스트` |
| `mobile/android/app/src/test/kotlin/com/livelayer/app/WallpaperRatioTest.kt` | 옛 네이티브 편집·보관함 구현 테스트 | 삭제한 구현에만 의존 → `CompanionWallpaperRemovalTest.kt; 유지한 렌더러/센서 테스트` |

## 코드 수동 백업 사본 (20)

| 파일 | 이전 역할 | 삭제 근거 / 통합 위치 |
|---|---|---|
| `backend/src/UpscaleLab.Api/BackgroundServices/PaymentRecoveryWorker.cs.bak-before-subscription-filter` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Api/BackgroundServices/SubscriptionExpirationWorker.cs.bak-before-strategy-fix` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Api/BackgroundServices/SubscriptionPaymentRecoveryWorker.cs.bak-before-failed-payment-handling` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Api/BackgroundServices/SubscriptionPaymentRecoveryWorker.cs.bak-before-renewal-filter` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Api/BackgroundServices/SubscriptionPaymentRecoveryWorker.cs.bak-before-unified-recovery` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Payments/PaymentFulfillmentService.cs.bak-before-execution-strategy` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Payments/PaymentFulfillmentService.cs.bak-before-subscription-order-guard` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Payments/SubscriptionFulfillmentService.cs.bak-before-approved-at` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Payments/SubscriptionFulfillmentService.cs.bak-before-execution-strategy` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Payments/SubscriptionRenewalChargeClaimService.cs.bak-before-execution-strategy` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Payments/SubscriptionRenewalFulfillmentService.cs.bak-before-execution-strategy` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Payments/SubscriptionRenewalOrderService.cs.bak-before-execution-strategy` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Payments/TossBillingClient.cs.bak-before-approved-at` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Payments/TossBillingClient.cs.bak-before-order-query` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Payments/TossPaymentsClient.cs.bak-before-idempotency` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Tokens/TokenService.cs.bak-before-paid-renewal` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Tokens/TokenService.cs.bak-before-settlement` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Tokens/TokenService.cs.bak-before-split` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Tokens/TokenService.cs.bak-before-split-v2` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |
| `backend/src/UpscaleLab.Infrastructure/Tokens/TokenService.cs.before-token-interface` | 결제·토큰·구독 코드 변경 전 사본 | 프로젝트가 컴파일하지 않는 과거 수동 백업; 현재 .cs와 Git 이력 사용 |

## 미사용 백엔드·로컬 추론 어댑터 (6)

| 파일 | 이전 역할 | 삭제 근거 / 통합 위치 |
|---|---|---|
| `backend/src/UpscaleLab.Application/Upscaling/IUpscaleService.cs` | 사용하지 않는 Replicate 예측 생성/조회 계약과 DTO | DI 등록 이외에 소비자가 없고 현재 업스케일은 RunPodProjectImageProcessor 사용 → `RunPodProjectImageProcessor.cs` |
| `backend/src/UpscaleLab.Infrastructure/Upscaling/ReplicateOptions.cs` | 사용하지 않는 Replicate API/model 자격 설정 | DI 등록 이외에 소비자가 없고 현재 업스케일은 RunPodProjectImageProcessor 사용 → `RunPodProjectImageProcessor.cs` |
| `backend/src/UpscaleLab.Infrastructure/Upscaling/ReplicateUpscaleService.cs` | 사용하지 않는 Replicate 업스케일 예측 생성·조회 HTTP 구현 | DI 등록 이외에 소비자가 없고 현재 업스케일은 RunPodProjectImageProcessor 사용 → `RunPodProjectImageProcessor.cs` |
| `backend/src/UpscaleLab.Infrastructure/Processing/SeeThroughImageLayerProcessor.cs` | API 서버에서 로컬 Python See-through 추론 프로세스를 실행하는 어댑터 | 현재 IImageLayerProcessor는 RunPodImageLayerProcessor; 캐릭터도 별도 RunPod endpoint 사용 → `RunPodImageLayerProcessor.cs; RunPodPretrainedSeeThroughProcessor.cs` |
| `backend/ai-worker/see_through_adapter.py` | 로컬 See-through PSD 추론 결과를 PNG·manifest로 변환 | 현재 IImageLayerProcessor는 RunPodImageLayerProcessor; 캐릭터도 별도 RunPod endpoint 사용 → `RunPodImageLayerProcessor.cs; RunPodPretrainedSeeThroughProcessor.cs` |
| `backend/ai-worker/README.md` | 서버 로컬 Python/See-through CLI 실행과 PSD 변환 어댑터 | 현재 IImageLayerProcessor는 RunPodImageLayerProcessor; 캐릭터도 별도 RunPod endpoint 사용 → `RunPodImageLayerProcessor.cs; RunPodPretrainedSeeThroughProcessor.cs` |

## 미사용 magnetic lasso와 테스트 (2)

| 파일 | 이전 역할 | 삭제 근거 / 통합 위치 |
|---|---|---|
| `web/src/vendor/photocraft-magnetic-lasso.ts` | 경계 추적 magnetic lasso와 전용 테스트 | 실제 main.tsx 및 개발 실험실 import 그래프에서 사용하지 않음; 현재 올가미는 입력 경로를 그대로 사용 → `web/src/subject-mask.ts` |
| `web/tests/photocraft-magnetic-lasso.test.mjs` | 경계 추적 magnetic lasso와 전용 테스트 | 실제 main.tsx 및 개발 실험실 import 그래프에서 사용하지 않음; 현재 올가미는 입력 경로를 그대로 사용 → `web/src/subject-mask.ts` |

## 완료된 일회성 도구 (1)

| 파일 | 이전 역할 | 삭제 근거 / 통합 위치 |
|---|---|---|
| `web/scripts/download-assets.mjs` | 초기 Unsplash 샘플 이미지 다운로드 | 완료된 일회성 작업이며 이미지는 Git에 포함; 출처는 ASSETS.md에 유지 → `web/ASSETS.md` |

## 문서 통합·이전 작업 기록 (64)

| 파일 | 이전 역할 | 삭제 근거 / 통합 위치 |
|---|---|---|
| `docs/account-and-device-ui-deployment-20261029.md` | Account settings and device preview deployment — 2026-10-29 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/account-photo-popup-deployment-20261029.md` | Profile photo popup and account menu dismissal — 2026-10-29 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/auth-sessions.md` | 자동 로그인과 이메일 인증 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/auth-api.md` |
| `docs/aws-team-handoff.md` | AWS 팀 배포 핸드오프 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/deployment.md` |
| `docs/background-layer-controls-deployment-20261028.md` | 배경 분리 설정 패널 수정 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/background-separation-deployment-20261028.md` | 클라우드 배경 분리 운영 배포 — 2026-10-28 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/background-separation-improvement-review-20261031.md` | 배경 분리 품질·응답 속도 개선 판단 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/subject-selection.md` |
| `docs/billing-footer-deployment-20261031.md` | 토큰 메뉴 중복 수정과 작업 브랜치 정리 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/birefnet-roi-deployment-20261031.md` | BiRefNet 범위 재분리와 강도 기본값 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/crop-control-refinement-deployment-20261029.md` | Crop frame and control refinement — 2026-10-29 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/crop-preview-palette-deployment-20261029.md` | Crop preview size and palette — 2026-10-29 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/device-layout-deployment-20261028.md` | 작업실·기기 관리 화면 정리 운영 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/device-management-deployment-20261028.md` | 기기 관리 UI 및 배경화면 삭제 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/device-panel-alignment-deployment-20261029.md` | PC and mobile device panel alignment — 2026-10-29 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/device-preview-sizing-deployment-20261029.md` | Device preview sizing and wallpaper names — 2026-10-29 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/device-selection-deployment-20261031.md` | 기기 관리 표시와 선택 도구 응답 개선 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/editor-effect-reset-deployment-20261028.md` | 원본 복구·기능별 취소·마우스 움직임 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/editor-refinement-deployment-20261031.md` | 자르기 저장·기본값·레이어 미리보기 운영 반영 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/editor-refinements.md` | 자르기 자동 저장과 레이어 미리보기 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md` |
| `docs/editor-save-and-layers-deployment-20261028.md` | 편집기 저장·레이어 수정 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/explore-apply-and-studio-layout-deployment-20261029.md` | 탐색 기기 적용 및 작업실 배치 운영 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/explore-cleanup-deployment-20261028.md` | 탐색 표시 항목 정리 운영 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/explore-details-deployment-20261028.md` | 탐색 상세 패널 및 새 이미지 작업실 화면 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/explore-import-fix-20261029.md` | 탐색 이미지의 내 이미지 저장 오류 수정 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/gallery-download-counts.md` | Gallery download counts | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md` |
| `docs/gyro-https-deployment-20261028.md` | HTTPS 자이로 연결 및 버튼 글자 크기 수정 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/image-card-menu-deployment-20261028.md` | 내 이미지 카드 설정 메뉴 수정 운영 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/image-editing.md` | 보관함 이미지 수정 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md` |
| `docs/implementation-report.md` | Upscale Lab 백엔드 구축 결과 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `README.md; docs/architecture.md` |
| `docs/inline-subject-deployment-20261029.md` | Inline separation deployment — 2026-10-29 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/inline-subject-separation.md` | Inline subject separation | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/subject-selection.md` |
| `docs/main-recovery-deployment-20261030.md` | main 병합 및 운영 롤백 배포 — 2026-10-30 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/manual-separation-restoration-20261101.md` | 수동 분리·기기 관리·연결 앱 배포 복구 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/manual-subject-deployment-20261030.md` | 배경 분리의 수동 인식·적용 운영 반영 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/mobile-tablet-ui-20261029.md` | 모바일·태블릿 웹 UI 개선 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/partial-selection-deployment-20261031.md` | 자동 분리 후 부분 선택 보완 웹 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/private-image-storage.md` | 앱 전용 이미지 저장소 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `mobile/README.md` |
| `docs/production-audit-20261030.md` | 운영 배포 및 이미지 처리 장애 진단 — 2026-10-30 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/production-restoration-deployment-20261101.md` | 수동 분리·기기 관리·연결 앱 다운로드 운영 복구 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/selection-billing-deployment-20261030.md` | 토큰·결제 기능과 피사체 선택의 통합 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/shared-import-processing-deployment-20261102.md` | 공유 이미지 가져오기·클라우드 분리 수정 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/smart-brush-autosave-deployment-20261031.md` | 브러쉬 범위 인식과 자르기 자동 저장 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/smart-subject-deployment-20261030.md` | 기존 작업실의 AI 피사체 선택 운영 배포 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/smart-subject-selection.md` | 작업실의 AI 피사체 선택 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/subject-selection.md` |
| `docs/storytold-integration-analysis.md` | Storytold / ArtCraft 기술 분석 및 Upscale Lab 적용 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `third_party/photocraft/README.md` |
| `docs/storytold-ui-deployment-20261029.md` | 분리 버튼 배치 및 PhotoCraft 브러시 운영 반영 — 2026-10-29 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/studio-crop-and-author-photo-deployment-20261029.md` | Movable crop regions and Explore author photos — 2026-10-29 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/subject-mask-deployment-20261029.md` | 자동 분리 및 수동 피사체 수정 운영 배포 — 2026-10-29 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/subject-mask-refinement.md` | 자동 분리 후 피사체 수정 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/subject-selection.md` |
| `docs/subject-selection-additive.md` | 자동 분리 후 BiRefNet 선택 범위 보완 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/subject-selection.md` |
| `docs/subject-selection-deployment-20261029.md` | Foreground selection UI deployment — 2026-10-29 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `docs/wallpaper-motion-deployment-20261028.md` | 기기 배경화면 움직임 및 커서 알림 수정 | 이전 배포·진단 기록 또는 분산된 기능 설명을 현재 문서로 통합 → `docs/service-behavior.md; docs/deployment.md; docs/subject-selection.md` |
| `CHARACTER_LAYERS_INTEGRATION_KO.md` | Upscale Lab — 사진 업로드 → 캐릭터 파츠 자동 분리 → 레이어 조합 v2 | 옛 모바일 작업/ZIP 전달/병합 검증 기록; 현행 기능과 제약을 통합 → `docs/live-2d.md` |
| `DARK_MODE_UPDATE_NOTES.md` | Upscale Lab — 다크모드 UI 통합 수정 | 옛 모바일 작업/ZIP 전달/병합 검증 기록; 현행 기능과 제약을 통합 → `docs/service-behavior.md` |
| `GALLERY_AUTO_RESTORE_NOTES.md` | Upscale Lab: 자동 갤러리 복원 (Android) | 옛 모바일 작업/ZIP 전달/병합 검증 기록; 현행 기능과 제약을 통합 → `docs/service-behavior.md` |
| `GALLERY_METADATA_FIX_NOTES.md` | Gallery image metadata persistence fix | 옛 모바일 작업/ZIP 전달/병합 검증 기록; 현행 기능과 제약을 통합 → `docs/service-behavior.md` |
| `LAYER_STACK_INTEGRATION_KO.md` | Upscale Lab — 자동 합성 / 수동 레이어 쌓기 통합 v1 | 옛 모바일 작업/ZIP 전달/병합 검증 기록; 현행 기능과 제약을 통합 → `docs/live-2d.md` |
| `LIVE2D_MERGE_FILES.txt` | LIVE2D_MERGE_FILES.txt | 옛 모바일 작업/ZIP 전달/병합 검증 기록; 현행 기능과 제약을 통합 → `docs/live-2d.md` |
| `LIVE2D_MERGE_README_KO.md` | Upscale Lab — 메인 RunPod/Toss 배포 코드 + Live2D 기능 통합 | 옛 모바일 작업/ZIP 전달/병합 검증 기록; 현행 기능과 제약을 통합 → `docs/live-2d.md` |
| `LIVE2D_MERGE_VALIDATION.txt` | LIVE2D_MERGE_VALIDATION.txt | 옛 모바일 작업/ZIP 전달/병합 검증 기록; 현행 기능과 제약을 통합 → `docs/live-2d.md` |
| `LIVE2D_STUDIO_INTEGRATION_KO.md` | Upscale Lab — 기존 작업실 Live 2D 탭 통합 v3 | 옛 모바일 작업/ZIP 전달/병합 검증 기록; 현행 기능과 제약을 통합 → `docs/live-2d.md` |
| `LOCAL_DELETE_POLICY.md` | Upscale Lab — 로컬 이미지 삭제와 공유 취소 분리 | 옛 모바일 작업/ZIP 전달/병합 검증 기록; 현행 기능과 제약을 통합 → `docs/service-behavior.md` |
| `UI_AUDIT_AND_FIX_NOTES.md` | Upscale Lab 최신 브랜치 정합성 수정 (2026-10-15) | 옛 모바일 작업/ZIP 전달/병합 검증 기록; 현행 기능과 제약을 통합 → `docs/service-behavior.md` |
| `mobile/docs/wallpaper-api.md` | 이전 독립 모바일 Dart/MethodChannel 배경화면 적용 API | 해당 화면과 native 채널을 제거했으며 현재 연결 앱은 upscale/companion 사용 → `docs/agent-api.md; docs/companion-apps.md` |

## 전체 채팅 PDF/Markdown (2)

| 파일 | 이전 역할 | 삭제 근거 / 통합 위치 |
|---|---|---|
| `output/pdf/Upscale_Lab_전체_채팅_2026-11-02.pdf` | 전체 채팅 PDF/Markdown 내보내기 | 소스·빌드·배포·런타임 참조가 없는 대화 산출물 |
| `output/pdf/Upscale_Lab_전체_채팅_2026-11-02.md` | 전체 채팅 PDF/Markdown 내보내기 | 소스·빌드·배포·런타임 참조가 없는 대화 산출물 |
