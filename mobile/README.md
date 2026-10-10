# Upscale Lab mobile client

This Flutter client supports JWT login with optional secure auto-login, local-first image registration, explicit cloud sharing, per-image share/delete actions, static wallpaper previews, a sensor-filtered layer preview, and an Android live wallpaper backed by locally cached project layers and `WallpaperService`.

Installable APK deliverables use the `Upscale_Lab-<version>-.apk` naming convention. The current app version is `0.3.1` with Android build number `6`.

Before distributing an APK, verify it with Android `apksigner`; an unsigned release APK cannot be installed. Existing team builds use the local Android debug certificate, so test updates must use that same certificate until a production signing key and secure release configuration are provided.

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

The home screen uses one local-first library. Registering an image copies it into app-private persistent storage and never calls an upload API. Each image card has settings for **공유** and **삭제**. Sharing is the only action that uploads the image to S3, creates its server project, and publishes the gallery item. Deleting a shared card removes its gallery post and cloud project before deleting the local copy.

After image selection, the app opens the original/upscaling/2.5D feature screen and then a full-screen wallpaper preview. The user can apply the wallpaper or finish without applying; completion persists the image and immediately adds it to the home screen. Upscaling and 2.5D conversion remain visibly marked as not implemented, so those choices currently preview and store the original without implying that a conversion happened.

The login screen includes **자동 로그인**. When selected, the JWT and expiration are stored with Android's secure storage and verified against `/api/auth/me` at the next launch. Logging out, changing the server address, token expiration, or validation failure clears the saved session.

Static wallpaper application keeps a persistent app-private source copy before handing it to Android's `WallpaperManager`; Android keeps the applied wallpaper after the app exits and across device restarts. **라이브 배경화면 적용** downloads processed layers into app-private persistent storage before opening Android's required live-wallpaper confirmation screen. `WallpaperService` reloads that saved manifest and cached layers whenever Android recreates it, including after reboot.

Download and wallpaper actions refresh the project first because the S3 image links are short-lived. Android preserves the original extension and MIME type for the mobile image formats accepted by the backend, including HEIC/HEIF and AVIF.
