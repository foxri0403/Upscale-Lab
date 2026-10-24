import 'package:flutter/services.dart';

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
