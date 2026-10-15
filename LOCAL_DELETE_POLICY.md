# Upscale Lab — 로컬 이미지 삭제와 공유 취소 분리

## 적용 기준

- **홈 화면 > 삭제**: `LocalImageStore.delete(image.id)`만 호출해 휴대폰 `Pictures/Upscale Lab`의 해당 사용자 이미지와 메타데이터를 제거합니다. 서버 게시물·클라우드 프로젝트는 삭제하지 않습니다.
- **홈 화면 > 공유 취소**: 사용자가 확인창에서 명시적으로 공유 취소를 선택했을 때만 `deleteGalleryPost`, `deleteProject`, `markUnshared`를 호출합니다. 휴대폰 이미지 파일은 유지합니다.
- **공유 시도 중 오류**: 공유를 요청한 이후 서버 응답이 불확실하거나, 게시물 생성 후 로컬 메타데이터 기록에 실패해도 클라우드 프로젝트를 자동 삭제하지 않습니다. 중복 공유 방지를 위해 탐색에서 게시 여부 확인 안내를 표시합니다.
- **업스케일링 중의 임시 업로드 삭제 정책**: 이번 수정 범위 밖입니다. 백엔드의 임시 업로드 정리 로직은 변경하지 않았습니다.

## 수정 파일

- `mobile/lib/unified_home.dart`
- `mobile/test/local_delete_preserves_shared_post_test.dart` (신규 회귀 테스트)

## 테스트

Flutter SDK가 설치된 환경에서 `cd mobile && flutter test test/local_delete_preserves_shared_post_test.dart`를 실행하고, 이어서 변경 파일에 `flutter analyze`를 실행하세요. 로컬 삭제·명시적인 공유 취소·공유 응답 불확실 및 EXIF 기록 실패 시의 클라우드 보존을 회귀 테스트로 확인합니다. 실기기에서는 갤러리 이미지 삭제 동의창까지 별도로 검증해야 합니다.

**주의:** 로컬 이미지를 삭제해도 공유된 이미지는 탐색/서버에 남지만, 해당 이미지를 홈 보관함에서 관리할 수는 없게 됩니다. 향후 탐색에서 게시물 관리가 필요한 경우 서버 사용자 소유자 기준 메뉴가 별도로 필요합니다.
