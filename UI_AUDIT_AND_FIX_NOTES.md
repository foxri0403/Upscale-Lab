# Upscale Lab 최신 브랜치 정합성 수정 (2026-10-15)

기준: 사용자가 업로드한 Upscale-Lab-gusrb03-wallpaper-screen-targets.zip.

## 수정
- mobile/lib/models/gallery.dart: GalleryPostItem.savedUserCount nullable 필드와 JSON 파싱 추가. 현재 unified_home.dart에 이미 존재하던 해당 필드 참조와 타입을 일치시킴. 백엔드 미구현 시 null.
- mobile/lib/unified_home.dart: wallpaper_service.dart 중복 import 제거. 백엔드 미구현이어도 탐색 카드·미리보기의 보관 인원 표시 영역을 '집계 준비 중'으로 보이게 함.
- mobile/lib/widgets/local_image_card.dart: 이전 3차 수정본의 Theme 기반 다크모드 색상 복구.
- mobile/lib/widgets/app_components.dart: 이전 3차 수정본의 UI 테마 개선 복구.

## 보존
- 기존 로그인·자동 로그인, 로컬 이미지, 갤러리 API, Android MainActivity.kt, 이미지 비율 설정과 wallpaper_service.dart 네이티브 호출 코드는 건드리지 않음.
- 기존 브랜치 main.dart 및 unified_home.dart의 미리보기 화면과 테마/태그 선택 코드 유지.

## 주의
- 정적 코드 대조 및 ZIP 무결성 검사 완료, Flutter SDK가 없어 flutter analyze/APK 빌드 미실행.
- 이번 ZIP은 GitHub에서 자동 APK 업데이트하지 않음. ZIP 파일의 모든 변경 파일을 동일 브랜치에 반영하고 해당 브랜치의 최신 커밋으로 APK를 새로 빌드해야 함.
- 백엔드의 보관 인원 API는 아직 미구현이며 표시 공간만 있음.
- Live2D 관련 실제 렌더링·리깅은 이 수정 범위에 포함되지 않음.
