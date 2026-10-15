# Gallery image metadata persistence fix

## Problem and behavior

Local registration and shared-image import could fail with `local_import_failed` / `이미지에 사용자 정보를 저장할 수 없습니다.` because EXIF `UserComment` did not survive writing and reading some PNG/JPEG images.

New images store the complete ownership and image metadata in a PNG iTXt chunk or JPEG APP15 segment. Loading still supports EXIF `UserComment` for existing images. The embedded marker filters images by account; it does not encrypt the album or prevent other gallery apps from changing metadata.

The storage path detects the actual image format from its bytes, including cloud downloads whose filename has the wrong extension. JPEG/PNG pixels are preserved without re-encoding; supported other formats use the existing PNG conversion. Registration verifies metadata both before and after MediaStore publication. Updates prepare and verify a temporary copy before writing, preserve image identity and ownership, and verify the saved result.

The existing registration UI, Android permission flow, account filtering, and cloud sharing/deletion policy continue to use the same interfaces.

## Validation

- Android `:app:testDebugUnitTest`: all 24 tests pass. Metadata survives initial save, reopening, and replacement for PNG/JPEG, including filenames with swapped extensions. Excluding the new marker, the complete original image bytes (including pixels and alpha) remain unchanged. PNG marker CRCs are checked, and repeated updates do not grow duplicate markers. Oversized JPEG metadata leaves the source unchanged.
- Existing Android ownership, path validation, metadata comparison, ratio, and wallpaper tests run with the same suite.
- Flutter `test --no-pub`: 99 pass and 9 existing UI tests fail (theme, search, preview, and edit expectations). An exported copy of pre-merge commit `a80c20a` reproduces the same 99/9 result. Flutter source, tests, and package inputs are unchanged by this merge.
- Flutter static analysis reports three pre-existing findings in unchanged files: two unused declarations in `lib/main.dart` and an unnecessary import in `test/dark_mode_theme_test.dart`.
- Physical-device MediaStore permission handling and reinstall restoration still require device verification.

## Device verification

1. Install an updated build over the existing app.
2. Register a PNG and JPEG; confirm that each appears in `Pictures/Upscale Lab` and Home.
3. Import a shared image from Explore and confirm the same behavior.
4. Change title/tags, restart the app, and confirm that they persist.
5. Back up the images before testing uninstall/reinstall, then confirm same-account restoration with full photo permission.
6. Confirm that deleting an image from Home removes only the local photo while its shared cloud post remains available.
