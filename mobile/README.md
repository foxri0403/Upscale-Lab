# Upscale Lab mobile client

This Flutter client supports JWT login, project upload, asynchronous processing start, project status display, a sensor-filtered layer preview, sensitivity control, and an Android live-wallpaper picker backed by `WallpaperService`.

## Run

Install a current Flutter SDK, then run:

```powershell
cd mobile
flutter pub get
flutter analyze
flutter run --dart-define=API_BASE_URL=http://YOUR_API_HOST:5080
```

The emulator default in `main.dart` is `http://10.0.2.2:5080`. A physical device must use a reachable HTTPS API URL.

The native wallpaper service currently provides the verified integration shell and sensor-reactive canvas. Persisting downloaded project layer files for offline native rendering is the next Android milestone; the in-app Flutter preview already renders the API layers.
