# Upscale Lab — 다크모드 UI 통합 수정

기준: 대화에서 전달한 `Upscale-Lab-branch-consistency-fix.zip`.
현재 GitHub 최신 커밋을 직접 조회하거나 실기기 APK를 실행한 것은 아닙니다.

## 이번 수정 범위

- 공통 `AppTheme`: 라이트/다크 Material 3 색상 팔레트 통일. 카드/입력창/팝업/칩/내비게이션/버튼/세그먼트/스낵바/상태 표시줄 색상 분리.
- `main.dart`: 로그인, 아이디 찾기, 비밀번호 재설정, 회원가입, 이메일 인증의 밝은 색상 고정 부분 수정. 시스템 상태 표시줄 아이콘을 현재 테마에 연동.
- `unified_home.dart`: 홈/탐색/설정의 아이콘·설명글·테두리 테마 색상 적용. 앱 내부 전체 화면 이미지 미리보기는 이미지 가독성을 위해 검정 배경을 유지하고 시스템바 아이콘은 흰색으로 표시.
- `app_components.dart`: 공통 카드 경계선, 그리드/리스트 전환 아이콘, 공유 배지 색상 수정. 전환 버튼의 `BuildContext` 전달 보완.
- `local_image_card.dart`: 홈 카드 배경·테두리·메뉴·설명글·이미지 로딩 오류 UI·로딩 오버레이 수정.
- `image_registration_page.dart`, `image_editor_page.dart`, `image_ratio_selector.dart`: 태그/편집 도구/필터/비율 버튼 및 보조 문구의 색상 수정.
- `mobile/test/dark_mode_theme_test.dart`: 테마 팔레트/카드/시스템바 회귀 테스트 **추가** (아직 미실행).

## 변경된 파일 (9개: 기존 파일 8개 + 테스트 1개)

1. `mobile/lib/theme/app_theme.dart`
2. `mobile/lib/main.dart`
3. `mobile/lib/unified_home.dart`
4. `mobile/lib/widgets/app_components.dart`
5. `mobile/lib/widgets/local_image_card.dart`
6. `mobile/lib/image_registration_page.dart`
7. `mobile/lib/image_editor_page.dart`
8. `mobile/lib/widgets/image_ratio_selector.dart`
9. `mobile/test/dark_mode_theme_test.dart` (신규)

## 변경하지 않은 부분

- 인증/갤러리 API, 이미지 저장, 공유/공유 취소, 삭제, 네이티브 배경화면 연동 코드, 백엔드.
- 좋아요, 보관 인원 API 연동, Live2D/자동 파츠 분리 미구현 기능.
- 홈 검색 UI를 새로 추가하지 않음(탐색 검색 유지).

## 권장 검사

```bash
cd mobile
flutter pub get
flutter analyze
flutter test test/dark_mode_theme_test.dart
flutter test
flutter build apk --release
```

Flutter/Dart SDK가 이 수정 작업 환경에 없어 위 명령들을 직접 실행하지 못했습니다. 소스 파일의 괄호 균형, import 파일 존재, 수정 파일 비교 및 ZIP 무결성만 검사했습니다.

## APK에서 확인하기

1. 앱 실행 후 설정 > 화면 테마에서 라이트/다크/시스템을 변경하고 앱 재실행 후 저장 여부 확인.
2. 홈 카드/더보기 메뉴, 탐색의 검색·태그창, 공유 이미지 카드/미리보기/보관함 버튼, 설정 카드, 이미지 등록·편집, 로그인과 인증 폼의 텍스트 대비 확인.
3. 배경화면 앱 내부 전체 화면 미리보기는 이미지 가독성을 위해 검정 배경이 유지됩니다.
4. 빌드 시 선택된 브랜치와 커밋 SHA가 최신인지 확인하고, 재빌드한 APK를 설치하세요. GitHub 웹사이트 커밋만으로 기존 APK가 갱신되지는 않습니다.
