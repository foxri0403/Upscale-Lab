import 'package:flutter/services.dart';
import 'dart:ui' as ui;
import 'api_client.dart';
import '../models/image_editor.dart';
import 'image_editor_service.dart';
import 'wallpaper_service.dart';

class Live2DDraft {
  const Live2DDraft(
      {required this.id,
      required this.backgroundPath,
      required this.foregroundPath,
      required this.width,
      required this.height,
      this.motionEnabled = true});
  final String id;
  final String backgroundPath;
  final String foregroundPath;
  final int width;
  final int height;
  final bool motionEnabled;
  Live2DDraft withMotion(bool enabled) => Live2DDraft(
      id: id,
      backgroundPath: backgroundPath,
      foregroundPath: foregroundPath,
      width: width,
      height: height,
      motionEnabled: enabled);

  factory Live2DDraft.fromMap(Map<Object?, Object?> map) => Live2DDraft(
      id: map['draftId']! as String,
      backgroundPath: map['backgroundPath']! as String,
      foregroundPath: map['foregroundPath']! as String,
      width: (map['width']! as num).toInt(),
      height: (map['height']! as num).toInt(),
      motionEnabled: map['motionEnabled'] as bool? ?? true);
}

abstract final class Live2DService {
  static const _channel = MethodChannel('live_layer/wallpaper');
  static final tilt =
      const EventChannel('live_layer/tilt').receiveBroadcastStream();
  static Future<Live2DDraft> split(String path, {ApiClient? apiClient}) async {
    if (apiClient == null) {
      throw PlatformException(
          code: 'cloud_required', message: '로그인한 클라우드 서버에 연결해 주세요.');
    }
    final owner = apiClient.userId;
    bool sameAccount() {
      try {
        return apiClient.userId == owner;
      } catch (_) {
        return false;
      }
    }

    final buffer = await ui.ImmutableBuffer.fromFilePath(path);
    late int width, height;
    try {
      final descriptor = await ui.ImageDescriptor.encoded(buffer);
      width = descriptor.width;
      height = descriptor.height;
      descriptor.dispose();
    } finally {
      buffer.dispose();
    }
    final project = await apiClient.createProject(
        title: '배경 분리', filePath: path, width: width, height: height);
    try {
      await apiClient.startProcessing(project.id);
      final result =
          await apiClient.waitForProcessing(project.id, maxAttempts: 480);
      final processed = await apiClient.getProject(project.id);
      if (!sameAccount()) throw StateError('로그인 계정이 변경되었습니다.');
      final backgrounds = processed.layers
          .where((layer) => layer.layerType == 'Background')
          .toList();
      final foregrounds = processed.layers
          .where((layer) => layer.layerType != 'Background')
          .toList();
      if (backgrounds.length != 1 || foregrounds.length != 1) {
        throw StateError('분리된 레이어가 올바르지 않습니다.');
      }
      final draft = Live2DDraft.fromMap(
          (await _channel.invokeMapMethod<Object?, Object?>('splitLive2D', {
        'sourcePath': path,
        'backgroundUrl': backgrounds.single.imageUrl,
        'foregroundUrl': foregrounds.single.imageUrl,
        'width': result['resultWidth'],
        'height': result['resultHeight']
      }))!);
      if (!sameAccount()) {
        await discard(draft);
        throw StateError('로그인 계정이 변경되었습니다.');
      }
      return draft;
    } finally {
      // Device drafts own the downloaded files. Keep the user's cloud library free of temporary uploads.
      if (sameAccount()) {
        try {
          await apiClient.deleteProject(project.id);
        } catch (_) {}
      }
    }
  }

  static Future<void> setMotion(Live2DDraft draft, bool enabled) =>
      _channel.invokeMethod<void>(
          'setLive2DMotion', {'draftId': draft.id, 'enabled': enabled});

  /// Explicit Apply can change Android destinations even when already active.
  static Future<bool> activate(Live2DDraft draft) async =>
      await _channel.invokeMethod<bool>('activateLive2DDraft', {
        'draftId': draft.id,
        'showSettings': true,
      }) ??
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
