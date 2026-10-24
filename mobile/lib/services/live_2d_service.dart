import 'package:flutter/services.dart';
import 'dart:ui' as ui;
import '../models/image_editor.dart';
import 'image_editor_service.dart';

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
