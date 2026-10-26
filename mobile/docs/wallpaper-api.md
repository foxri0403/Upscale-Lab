# Wallpaper operations for UI integration

The static wallpaper API accepts `WallpaperTarget.home`, `.lock`, or `.both` for
application and removal. Image registration and editing expose automatic/manual
ratios, and the app preview preserves its existing target selection sheet.
These source changes do not require an APK release or version change.

## Dart API

For an image registered in the signed-in user's library, use the captured
`LocalImageStore`. Android resolves the ID within that account's owned records:

```dart
import 'services/local_image_store.dart';
import 'services/wallpaper_service.dart';

final store = LocalImageStore(scope: api.storageScope, ownerId: api.userId);
await store.applyWallpaper(image.id, target: WallpaperTarget.lock);
await store.applyWallpaper(image.id, target: WallpaperTarget.home);
await store.applyWallpaper(image.id, target: WallpaperTarget.both);
```

For a downloaded shared image, editor result, or refreshed remote image URL:

```dart
import 'models/image_ratio.dart';

const wallpaper = WallpaperService();
await wallpaper.applyLocal(
  sourcePath: downloadedPath,
  target: WallpaperTarget.lock,
  ratio: const ImageRatioSettings(),
);
await wallpaper.applyRemote(
  url: refreshedProject.originalImageUrl,
  target: WallpaperTarget.both,
);

await wallpaper.clear(target: WallpaperTarget.home);
await wallpaper.clear(target: WallpaperTarget.lock);
await wallpaper.clear(target: WallpaperTarget.both);
```

Each future completes after Android finishes the operation and the app updates
its source cache. Catch `PlatformException` to display failures. UI callers should
prevent duplicate actions while awaiting the future and report success only after
completion. Refresh short-lived S3 URLs before calling `applyRemote`.

`previewWallpaper` opens the device's crop/set screen, which controls its own target
selection. To enforce an in-app target choice, call `applyWallpaper` or
`WallpaperService` directly. Owned Live 2D images use the same `applyWallpaper`
entry point and update the active service without opening a preview. The selected
destination combination must match this service's currently active destinations;
otherwise `live2d_activation_required` is returned without changing a wallpaper.
Initial service activation or destination changes use a separate explicit
`activateLive2D` action in the Live 2D controls and require Android confirmation. Removal uses
Android's wallpaper reset API and can also reset a live wallpaper on a selected
screen when the OS supports it.

## Platform channel contract

| Channel | Method | Arguments |
| --- | --- | --- |
| `live_layer/storage` | `applyLocalWallpaper` | `scope`, `ownerId`, `id`, `target` |
| `live_layer/wallpaper` | `applyLocalStaticWallpaper` | `sourcePath`, `target`, optional `ratio` |
| `live_layer/wallpaper` | `applyStaticWallpaper` | `url`, `target`, optional `ratio` |
| `live_layer/wallpaper` | `clearWallpaper` | `target` |
| `live_layer/storage` | `activateLocalLive2D` | `scope`, `ownerId`, `id`, optional `target`, `showSettings` |
| `live_layer/wallpaper` | `activateLive2DDraft` | `draftId`, `showSettings` |
| `live_layer/wallpaper` | `getActiveLive2DTarget` | none (returns Android flags, or `0`) |
| `live_layer/wallpaper` | `cloneLive2DDraft` | `draftId` |
| `live_layer/storage` | `getLocalLive2DDraft` | `scope`, `ownerId`, `id` (unshared owned image) |
| `live_layer/storage` | `saveLocalImageEdits` | `scope`, `ownerId`, `id`, `sourcePath`, `title`, `tags`, `width`, `height`, `ratio`, optional `draftId` |

The wire target is exactly `home`, `lock`, or `both`. An omitted/null target defaults
to `home` for existing callers. Invalid values fail with `invalid_target`.
`clearHomeWallpaper` remains a home-only compatibility alias. Settings use one
**배경화면 제거** action with independent home/lock checkboxes, disabled deletion
when neither is selected, and **취소 / 삭제** actions. The editor's **Live 2D 적용**
exports the current crop/filter into a wallpaper snapshot and invokes
`activateLive2DDraft` with `showSettings=true`. Explicit Apply always opens Android
target settings, including an already active home-only service. Confirmation returns to the same editor;
cancellation or failure preserves editable layers. **저장** persists only to the
private library. Static registration uses independent home/lock checkboxes.
Live 2D registration reuses `getActiveLive2DTarget` without a target dialog, even
after restarting the app. `LocalImageStore.registerWallpaper` never opens Android
settings on failure; `live2d_activation_required` tells the user to connect with
**Live 2D 적용**. Saved previews offer that explicit reconnect action only when
Android reports no live connection. Saved previews also expose **Live 2D 적용 화면 변경**
through the image menu using `activateLocalLive2D` with `showSettings=true`.
These explicit settings calls cannot take the direct-update shortcut. Editor save/registration returns to the library. Shared images
cannot be modified, including through metadata-only edit methods, and edit saves
preserve the existing image ID.

All account-scoped storage operations now carry both the legacy server/account
`scope` and the stable authenticated `ownerId`. Android resolves registered images
from its permanent private `files/image_library/<owner>/<image>/` directory.
Pixels and metadata are separate files; gallery permissions and app-cache working
copies are unnecessary for normal library operations. The scope remains in use for
sorting and migration of the previous app-private library. An owned `mode=live2d`
record stays live through the generic `applyLocalWallpaper` bridge and is never
flattened to a static source. `previewLocalWallpaper` is a separate explicit preview
operation. See [private image storage](../../docs/private-image-storage.md).

All account-scoped storage operations now carry both the legacy server/account
`scope` and the stable authenticated `ownerId`. Android resolves registered images
from `Pictures/Upscale Lab` using the image's embedded owner marker, then regenerates
an app-cache working copy for wallpaper operations. The scope remains in use for
sorting and migration of the previous app-private library.

Local and remote application also accept an optional `ratio` map. An omitted
ratio defaults to automatic, which detects the full applying display and center
crops without stretching. The typed Dart service accepts `ImageRatioSettings`;
import `models/image_ratio.dart` to construct it. A manual ratio stores a
`preset` and `portrait` setting; editor free crops also store
`freeAspectRatio`. Account-scoped `applyWallpaper` and `previewWallpaper`
read the image's saved ratio from owned metadata. Automatic settings are resolved
on each applying device and work with home, lock, or both targets.

Lock-only and both-screen operations require Android 7.0/API 24 or newer. An older
device returns `unsupported_target` before downloading, applying, or removing
anything; home-only operations retain their previous fallback. Apply failures use
`wallpaper_failed`; removal failures use `wallpaper_clear_failed`. Private-library
lookup failures use the existing `local_storage_failed` code. Missing URL/source
arguments use `invalid_url`/`invalid_source`.

## Android reset and persistence behavior

- Home removal resets the home wallpaper and preserves the lock wallpaper.
- Lock removal removes the independent lock wallpaper, so Android displays the
  current home wallpaper on the lock screen.
- Both removal restores Android's default wallpaper on both screens.
- Static application to both uses one call with both Android flags. Later changes
  to either screen preserve the other screen's independent source copy.
- App-private source copies are kept separately in `applied_wallpaper/home.img`
  and `applied_wallpaper/lock.img`. Removal deletes only selected copies, including
  the legacy home `current.img` when home is selected. Library images are retained.
- Ratio processing prepares a temporary cropped image for Android while keeping
  the original bytes in each selected screen's source cache. Crop or application
  failures preserve previous caches and remove staging files.
- Android persists the actual applied wallpaper across app exits and reboots.
  A failed source read or rejected Android operation preserves previous copies
  and cleans up temporary files.

These reset rules follow
[Android WallpaperManager.clear(int)](https://developer.android.com/reference/android/app/WallpaperManager#clear(int)).
No wallpaper read permission is required.

## Validation

Run `flutter analyze` and `flutter test` from `mobile`. Native target and cache
behavior is covered by `StaticWallpaperControllerTest`; run
`./gradlew :app:testDebugUnitTest` from `mobile/android` (PowerShell:
`.\gradlew.bat :app:testDebugUnitTest`). This compiles/tests native source without
packaging an APK.

On a real device, apply different images to home and lock, replace each separately,
apply one image to both, and remove each separately and then both. Verify the
unselected screen and library images remain intact, and check persistence after
restarting the app/device. Launcher-specific crop controls remain OS behavior.
