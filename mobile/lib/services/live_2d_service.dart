import 'package:flutter/services.dart';
import 'dart:ui' as ui;
import '../models/image_editor.dart';
import 'image_editor_service.dart';
import 'wallpaper_service.dart';

class Live2DDraft {
  const Live2DDraft(
      {required this.id,
      required this.backgroundPath,
      required this.foregroundPath,
      required this.width,
      required this.height});
  final String id;
  final String backgroundPath;
  final String foregroundPath;
  final int width;
  final int height;

  factory Live2DDraft.fromMap(Map<Object?, Object?> map) => Live2DDraft(
      id: map['draftId']! as String,
      backgroundPath: map['backgroundPath']! as String,
      foregroundPath: map['foregroundPath']! as String,
      width: (map['width']! as num).toInt(),
      height: (map['height']! as num).toInt());
}

abstract final class Live2DService {
  static const _channel = MethodChannel('live_layer/wallpaper');
  static final tilt =
      const EventChannel('live_layer/tilt').receiveBroadcastStream();
  static Future<Live2DDraft> split(String path) async =>
      Live2DDraft.fromMap((await _channel.invokeMapMethod<Object?, Object?>(
          'splitLive2D', {'sourcePath': path}))!);

  static Future<bool> activate(Live2DDraft draft) async =>
      await _channel
          .invokeMethod<bool>('activateLive2DDraft', {'draftId': draft.id}) ??
      false;

  static Future<Live2DDraft> clone(Live2DDraft draft) async =>
      Live2DDraft.fromMap((await _channel.invokeMapMethod<Object?, Object?>(
          'cloneLive2DDraft', {'draftId': draft.id}))!);

  static Future<WallpaperTarget?> activeTarget() async {
    try {
      return switch (
          await _channel.invokeMethod<int>('getActiveLive2DTarget')) {
        1 => WallpaperTarget.home,
        2 => WallpaperTarget.lock,
        3 => WallpaperTarget.both,
        _ => null,
      };
    } on PlatformException {
      return null;
    }
  }

  /// Read Android's actual connection on every registration, including after
  /// app restarts. A saved permission flag cannot establish a wallpaper service.
  static Future<WallpaperTarget> requireActiveTarget() async {
    final target = await activeTarget();
    if (target == null) {
      throw PlatformException(
          code: 'live2d_activation_required',
          message: 'Live 2D 적용에서 Android 배경화면 설정을 먼저 완료해 주세요.');
    }
    return target;
  }

  /// Keep the separation untouched while editing. Bake the same crop/filter
  /// into both planes only when saving, preserving the foreground alpha.
  /// The caller owns the returned draft and the original until transfer.
  static Future<Live2DDraft> export(Live2DDraft draft,
      {required ui.Rect crop, required PhotoFilter filter}) async {
    if (!ImageEditorService.hasEdits(crop, filter)) return draft;
    EditedImageFile? background;
    EditedImageFile? foreground;
    try {
      background = await ImageEditorService.export(
          sourcePath: draft.backgroundPath, crop: crop, filter: filter);
      foreground = await ImageEditorService.export(
          sourcePath: draft.foregroundPath, crop: crop, filter: filter);
      return Live2DDraft.fromMap(
          (await _channel.invokeMapMethod<Object?, Object?>('editLive2D', {
        'draftId': draft.id,
        'backgroundPath': background!.path,
        'foregroundPath': foreground!.path,
      }))!);
    } finally {
      await background?.dispose();
      await foreground?.dispose();
    }
  }

  static Future<void> discard(Live2DDraft? draft) async {
    if (draft == null) return;
    try {
      await _channel.invokeMethod<void>('discardLive2D', {'draftId': draft.id});
    } on PlatformException {
      // Cache cleanup must not fail a completed save.
    } on MissingPluginException {
      // The Android activity can already be detached during disposal.
    }
  }
}
