import 'package:flutter/services.dart';

import '../models/image_ratio.dart';

/// The Android screen(s) whose static wallpaper will be changed.
enum WallpaperTarget { home, lock, both }

/// Wallpaper operations for UI callers. Errors are returned as PlatformException.
///
/// Android owns the applied wallpaper after these futures complete. Clearing the
/// lock wallpaper makes Android display the home wallpaper on the lock screen.
class WallpaperService {
  const WallpaperService();

  static const _channel = MethodChannel('live_layer/wallpaper');

  /// Applies a local source, including a downloaded gallery image.
  Future<void> applyLocal({
    required String sourcePath,
    required WallpaperTarget target,
    ImageRatioSettings ratio = const ImageRatioSettings(),
  }) =>
      _channel.invokeMethod<void>('applyLocalStaticWallpaper', {
        'sourcePath': sourcePath,
        'target': target.name,
        'ratio': ratio.toMap(),
      });

  /// Callers should refresh expiring image URLs before applying.
  Future<void> applyRemote({
    required String url,
    required WallpaperTarget target,
    ImageRatioSettings ratio = const ImageRatioSettings(),
  }) =>
      _channel.invokeMethod<void>('applyStaticWallpaper', {
        'url': url,
        'target': target.name,
        'ratio': ratio.toMap(),
      });

  /// Resets the selected screen(s) without deleting library images.
  Future<void> clear({required WallpaperTarget target}) =>
      _channel.invokeMethod<void>('clearWallpaper', {'target': target.name});
}
