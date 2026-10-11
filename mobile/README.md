# Upscale Lab mobile client

This Flutter client supports JWT login with optional secure auto-login, local-first image registration, explicit cloud sharing, per-image share/delete actions, static wallpaper previews, a sensor-filtered layer preview, and an Android live wallpaper backed by locally cached project layers and `WallpaperService`.

Installable APK deliverables use the `Upscale_Lab-<version>-.apk` naming convention. The current app version is `0.4.3` with Android build number `10`; the APK is `artifacts/Upscale_Lab-0.4.3-.apk`.

Version `0.4.3` renames the discovery tab to **검색**, resolves storage-only image URLs through a signed preview URL, and lets users retry failed or expired previews. Preview requests do not increment download counts. Shared tag edits immediately PATCH the cloud post and save the confirmed tag locally; selecting the current tag also resynchronizes a shared post. Sharing and tag edits report an error if the server does not return the selected tag instead of silently accepting an unsupported server.

This release requires the matching backend, including the existing `AddGalleryTags` migration, `PATCH /api/gallery/{id}/tag`, and `GET /api/gallery/{id}/preview-url`. Older servers returning `s3://` image URLs and no `tag` field must be updated manually; installing the APK alone cannot add those server capabilities.

Version `0.4.2` includes the responsive UI fixes from `feature/responsive-ui-0.4.1`: scrollable login and sign-up forms with a maximum width of 440 logical pixels, keyboard-aware login spacing, a safe-area wallpaper preview with scrollable controls, and clearer wallpaper action buttons. Android requests that the bottom navigation bar remain hidden until an edge swipe; newer Android versions may override this request.

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

The home screen uses one local-first library. Registering an image copies it into app-private persistent storage and never calls an upload API. The registration screen pre-fills the original filename, lets the user edit it, and stores one searchable genre tag. Tapping the tag on an image card edits it locally and also updates its owned gallery post when shared. Each image card has settings for **공유**, **공유 취소**, **홈 배경화면에서 제거**, and **삭제**. Wallpaper removal restores Android's default home wallpaper without deleting the registered image. Sharing is the only action that uploads the image to S3, creates its server project, and publishes the gallery item. Sharing cancellation removes the public gallery post and cloud project while preserving the local copy. The Search tab (**검색**) lists public cloud posts, searches image titles only, and applies exact tag filtering in both the API and client.

After image selection, the app opens the original/upscaling/2.5D feature screen and then a full-screen wallpaper preview. The user can apply the wallpaper or finish without applying; completion persists the image and immediately adds it to the home screen. Upscaling and 2.5D conversion remain visibly marked as not implemented, so those choices currently preview and store the original without implying that a conversion happened.

The login screen includes **자동 로그인**. When selected, the JWT and expiration are stored with Android's secure storage and verified against `/api/auth/me` at the next launch. Logging out, changing the server address, token expiration, or validation failure clears the saved session.

Static wallpaper application keeps a persistent app-private source copy before handing it to Android's `WallpaperManager`; Android keeps the applied wallpaper after the app exits and across device restarts. **라이브 배경화면 적용** downloads processed layers into app-private persistent storage before opening Android's required live-wallpaper confirmation screen. `WallpaperService` reloads that saved manifest and cached layers whenever Android recreates it, including after reboot.

Download and wallpaper actions refresh the project first because the S3 image links are short-lived. Android preserves the original extension and MIME type for the mobile image formats accepted by the backend, including HEIC/HEIF and AVIF.
