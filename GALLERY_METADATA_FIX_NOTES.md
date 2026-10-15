# Upscale Lab - gallery image ownership metadata fix

## Base

This patch is based specifically on `Upscale-Lab-gusrb03-image-registration-studio.zip` uploaded in this conversation. It keeps the image registration studio, Flutter views, gallery APIs, Android app integration, and cloud sharing/deletion policy in that source unchanged.

## Problem and fix

The previous Android gallery storage writes JSON to ExifInterface `TAG_USER_COMMENT`. On some PNG/JPEG variants this does not survive the save/read verification and both local registration and shared-image import fail with `local_import_failed` / `이미지에 사용자 정보를 저장할 수 없습니다.`.

- PNG: write/read an `UpscaleLab` iTXt chunk with CRC.
- JPEG: write/read a dedicated APP15 marker without image re-encoding.
- Prefer new embedded marker on loading; fall back to old Exif `UserComment` data for previously registered images.
- Detect the source file's real PNG/JPEG format instead of trusting a filename from cloud downloads. Convert supported other image formats to PNG using the existing Android BitmapFactory fallback.
- Verify the complete JSON metadata before and after MediaStore publication and updates, including owner, tags, ratio, and sharing IDs.
- Preserve `GalleryImageMetadata.isOwned`, path validation, existing `MainActivity.kt`, Android permission prompts, app UI, and cloud deletion rules.

## Files

- `mobile/android/app/src/main/kotlin/com/livelayer/app/GalleryLocalImageStore.kt` (modified)
- `mobile/android/app/src/main/kotlin/com/livelayer/app/ImageOwnerMetadata.kt` (new)
- `mobile/android/app/src/test/kotlin/com/livelayer/app/ImageOwnerMetadataTest.kt` (new)

## Verification

- Standalone Kotlin/JVM compile & runtime checks performed for writing, reading, and rewriting metadata in real PNG and JPEG files.
- Wrong file extensions checked (actual PNG with .jpg name and JPEG with .png name).
- Files were opened and decoded by Pillow after metadata update; PNG alpha preserved.
- Flutter analyze, Android Gradle build, and a Galaxy device test were **not** possible in this environment. In-app MediaStore permissions and automatic restore after reinstall still require testing on a physical device.

## Device test sequence

1. Install updated APK over existing app without uninstalling.
2. Register a new PNG and JPEG image; check they appear in `Pictures/Upscale Lab` and Home.
3. Import an image from Explore; check it appears in Home and the gallery.
4. Modify its title/tags, restart the app, and confirm they survive.
5. Back up your images before testing uninstall/reinstall; then test same-account auto-restore after the files are confirmed in the gallery.
6. Confirm Home deletion removes only local photo and does not remove a shared cloud post.
