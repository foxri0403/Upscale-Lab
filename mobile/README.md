# Upscale Lab mobile client

This Flutter client supports JWT login, project image upload, asynchronous processing start, project status display, original-image download, one-tap static wallpaper application, a sensor-filtered layer preview, and an Android live wallpaper backed by locally cached project layers and `WallpaperService`.

## Run

Install a current Flutter SDK, then run:

```powershell
cd mobile
flutter pub get
flutter analyze
flutter run --dart-define=API_BASE_URL=http://YOUR_API_HOST:5080
```

The emulator default in `main.dart` is `http://10.0.2.2:5080`. A physical device must use a reachable HTTPS API URL.

On the project preview screen, **원본 다운로드** saves the source through Android's download manager and **원본 바로 적용** sets it as the home-screen wallpaper. **라이브 배경화면 적용** downloads the current processed layers into app-private persistent storage before opening Android's required live-wallpaper confirmation screen. Once confirmed, the wallpaper renders those cached layers offline and applies the saved sensor sensitivity.
