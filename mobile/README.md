# Upscale Lab mobile client

This Flutter client supports JWT login, explicit local/cloud image storage, project status display, original-image download, one-tap static wallpaper application, a sensor-filtered layer preview, and an Android live wallpaper backed by locally cached project layers and `WallpaperService`.

Installable APK deliverables use the `Upscale_Lab-<version>-.apk` naming convention. The current app version is `0.2.0` with Android build number `3`.

## Run

Install a current Flutter SDK, then run:

```powershell
cd mobile
flutter pub get
flutter analyze
flutter run --dart-define=API_BASE_URL=http://YOUR_API_HOST:5080
```

The emulator default in `main.dart` is `http://10.0.2.2:5080`. A physical device must use a reachable HTTPS API URL.

The login screen includes **서버 주소 설정**. Team test builds persist the selected API URL in Android app preferences, so the same APK can switch from a local server to the final AWS HTTPS endpoint without rebuilding. Changing the server logs out the current session.

The home screen separates **로컬** and **클라우드** storage. Local saves never call an upload API and write the selected image to the device's `Pictures/Upscale Lab` collection through Android MediaStore. Cloud saves upload the original to S3 and persist its project metadata in the server database only after explicit confirmation. Upload no longer starts 2.5D processing automatically.

The creation sheet exposes original, upscaling, and 2.5D workflows plus their output destination. Upscaling and 2.5D conversion remain marked as not implemented; until their engines are connected, the original file is stored at the selected destination. This keeps the storage decision reusable for those future outputs without pretending a conversion occurred.

On the project preview screen, **원본 다운로드** saves the source through Android's download manager and **원본 바로 적용** sets it as the home-screen wallpaper. **라이브 배경화면 적용** downloads the current processed layers into app-private persistent storage before opening Android's required live-wallpaper confirmation screen. Once confirmed, the wallpaper renders those cached layers offline and applies the saved sensor sensitivity.

Download and wallpaper actions refresh the project first because the S3 image links are short-lived. Android preserves the original extension and MIME type for the mobile image formats accepted by the backend, including HEIC/HEIF and AVIF.
