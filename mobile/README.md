# Upscale Lab mobile client

This Flutter client supports JWT login with optional secure auto-login, local-first image registration, explicit cloud sharing, per-image share/delete actions, static wallpaper previews, a sensor-filtered layer preview, and an Android live wallpaper backed by locally cached project layers and `WallpaperService`.

Installable APK deliverables use the `Upscale_Lab-<version>-.apk` naming convention. The current app version is `0.5.1` with Android build number `12`; the APK is `artifacts/Upscale_Lab-0.5.1-.apk`. Read the current version and versioning rules in the root `AGENTS.md` before generating an APK, and replace its latest-version entry after every build.

New shares also persist the complete tag set in versioned description metadata for legacy single-tag servers. The app verifies the persisted response and reconstructs tags when another client reads the post. Search supports any-of multi-selection and keeps tags/title when switching tabs. Legacy servers can filter only the primary tag, so secondary-tag filtering is performed by the app within the returned posts; full server-side filtering and shared tag editing require the matching backend. Upgraded servers read compatibility metadata on old posts and remove it on native tag edits.

Image registration and editing support multiple genre tags. **검색** supports multiple independent tag filters: a post appears if it matches any selected tag and the title search. Shared name/tag edits save locally only after the backend confirms both the name and the entire selected tag set. The matching backend provides `PATCH /api/gallery/{id}` for these combined edits; older servers show an update-required error without changing local metadata. Tap a shared image to download its original, preview/apply it as the home wallpaper, and select **내 보관함에 저장** to keep a private local copy. Imported copies are not linked to the uploader's cloud project or shared automatically.

Native tag arrays, shared tag editing, filtering across the full gallery, and thumbnails require the matching backend and the `AddGalleryMultiTags` migration, which copies existing single tags into the new array. `PATCH /api/gallery/{id}/tag` accepts `tags` (the legacy `tag` remains compatible), and `GET /api/gallery?tags=동물&tags=풍경` matches any requested tag. `GET /api/gallery/{id}/thumbnail` generates a JPEG with a maximum side of 480 pixels on first access, persists it in S3, and uses a bounded 64 MiB server cache for repeat requests. The client uses a stable thumbnail URL and bounds decoded preview size; original downloads occur on import. Preview access does not count as a download. Unsupported/corrupt image formats fall back to the original. The first thumbnail generation and original download still depend on image size and network speed. Deploy the backend and apply the migration manually after checking the target DB and backup; the APK alone cannot add server capabilities.

The private library uses one app-private image folder and one metadata collection. Every new record has an `ownerScope` derived from the server URL and JWT user ID. All listing, tag edits, share state changes, and deletion are scoped to that account. Switching back to the same server/account restores that account's records. Legacy records without ownership remain preserved but hidden instead of being assigned to the next person who logs in; users can explicitly reimport source images from the photo picker. Explicit photo-album exports remain device-wide and are not scanned into the private library. No per-account albums are created in the phone gallery.

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

The home screen uses one local-first library. Registering an image copies it into app-private persistent storage and never calls an upload API. The registration screen pre-fills the original filename, lets the user edit it, and stores multiple searchable genre tags. The upper-right controls switch between a one-column thumbnail list and a two-column grid in both Home and Search. Private items display only their name and tags; shared library items add a right-aligned **공유** label and a blue circle with a white check in the name row. The three-dot menu contains **공유** (or **공유 취소** for shared items), **수정**, and **삭제**. **수정** edits the name and tags together. **설정 → 홈 배경화면 제거** restores Android's default home wallpaper without deleting registered images. Sharing is the only action that uploads the image to S3, creates its server project, and publishes the gallery item. Sharing cancellation removes the public gallery post and cloud project while preserving the local copy. The Search tab (**검색**) lists public cloud posts, searches image titles only, and applies any-of exact tag filtering in both the API and client.

All screens use the shared bright theme in `lib/theme/app_theme.dart`, including account forms, search, registration, dialogs, settings, and previews. Android system bars remain visible; Flutter reserves their safe-area insets below the app tabs and actions. For three-button navigation, select that navigation mode in the device/emulator's Android settings; the app does not replace the system buttons. UI-only development can run directly from Android Studio by opening `mobile`, selecting the Flutter SDK and an emulator, and running `lib/main.dart`. An APK release and version increment are unnecessary for this workflow.

After image selection, the app opens the original/upscaling/2.5D feature screen and then a full-screen wallpaper preview. The user can apply the wallpaper or finish without applying; completion persists the image and immediately adds it to the home screen. Upscaling and 2.5D conversion remain visibly marked as not implemented, so those choices currently preview and store the original without implying that a conversion happened.

The login screen includes **자동 로그인**. When selected, the JWT and expiration are stored with Android's secure storage and verified against `/api/auth/me` at the next launch. Logging out, changing the server address, token expiration, or validation failure clears the saved session.

Static wallpaper application keeps a persistent app-private source copy before handing it to Android's `WallpaperManager`; Android keeps the applied wallpaper after the app exits and across device restarts. **라이브 배경화면 적용** downloads processed layers into app-private persistent storage before opening Android's required live-wallpaper confirmation screen. `WallpaperService` reloads that saved manifest and cached layers whenever Android recreates it, including after reboot.

Download and wallpaper actions refresh the project first because the S3 image links are short-lived. Android preserves the original extension and MIME type for the mobile image formats accepted by the backend, including HEIC/HEIF and AVIF.
