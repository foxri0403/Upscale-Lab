import 'package:flutter/services.dart';

import '../models/local_image.dart';
import '../models/library_order.dart';
import 'wallpaper_service.dart';
import 'live_2d_service.dart';
import '../models/image_ratio.dart';

class LocalImageStore {
  const LocalImageStore({required this.scope, required this.ownerId});

  final String scope;
  final String ownerId;

  static const _channel = MethodChannel('live_layer/storage');

  Future<Map<Object?, Object?>> migrateGalleryImages() async =>
      (await _channel.invokeMapMethod<Object?, Object?>(
          'migrateGalleryImages', {'scope': scope, 'ownerId': ownerId}))!;

  Future<void> attachLive2D(String id, String draftId) =>
      _channel.invokeMethod<void>('attachLocalLive2D',
          {'scope': scope, 'ownerId': ownerId, 'id': id, 'draftId': draftId});

  Future<Map<Object?, Object?>> live2DLayers(String id) async =>
      (await _channel.invokeMapMethod<Object?, Object?>(
          'getLocalLive2D', {'scope': scope, 'ownerId': ownerId, 'id': id}))!;

  /// Android's required activation belongs to the Live 2D controls, not Apply.
  Future<bool> activateLive2D(String id, {WallpaperTarget? target}) async =>
      await _channel.invokeMethod<bool>('activateLocalLive2D', {
        'scope': scope,
        'ownerId': ownerId,
        'id': id,
        'showSettings': true,
        if (target != null) 'target': target.name
      }) ??
      false;

  Future<Live2DDraft> editableLive2D(String id) async =>
      Live2DDraft.fromMap((await _channel.invokeMapMethod<Object?, Object?>(
          'getLocalLive2DDraft',
          {'scope': scope, 'ownerId': ownerId, 'id': id}))!);

  Future<LocalImageItem> saveEdits(
          {required String id,
          required String sourcePath,
          required String title,
          required List<String> tags,
          required int width,
          required int height,
          required ImageRatioSettings ratio,
          String? draftId}) async =>
      LocalImageItem.fromPlatformMap((await _channel
          .invokeMapMethod<Object?, Object?>('saveLocalImageEdits', {
        'scope': scope,
        'ownerId': ownerId,
        'id': id,
        'sourcePath': sourcePath,
        'title': title,
        'tags': tags,
        'width': width,
        'height': height,
        'ratio': ratio.toMap(),
        if (draftId != null) 'draftId': draftId
      }))!);

  Future<LibraryOrder> getOrder() async =>
      LibraryOrder.fromMap(await _channel.invokeMapMethod<Object?, Object?>(
              'getLibraryOrder', {'scope': scope, 'ownerId': ownerId}) ??
          const {});

  Future<void> saveOrder(LibraryOrder order) => _channel.invokeMethod<void>(
      'saveLibraryOrder',
      {'scope': scope, 'ownerId': ownerId, ...order.toMap()});

  Future<bool> previewWallpaper(String id) async =>
      await _channel.invokeMethod<bool>('previewLocalWallpaper',
          {'scope': scope, 'ownerId': ownerId, 'id': id}) ??
      false;

  /// Applies an image owned by the captured account to the selected screen(s).
  /// Use this after the UI chooses a target; the system preview controls its own
  /// target selection and cannot enforce this option.
  Future<void> applyWallpaper(String id, {required WallpaperTarget target}) =>
      _channel.invokeMethod<void>('applyLocalWallpaper', {
        'scope': scope,
        'ownerId': ownerId,
        'id': id,
        'target': target.name,
      });

  /// Registration never launches Android settings. Live 2D connects through
  /// its explicit Apply action, then reuses Android's current destinations.
  Future<bool> registerWallpaper(String id,
      {required WallpaperTarget target}) async {
    await applyWallpaper(id, target: target);
    return true;
  }

  Future<List<LocalImageItem>> getAll() async {
    final values = await _channel.invokeListMethod<Object?>(
            'listLocalImages', {'scope': scope, 'ownerId': ownerId}) ??
        const <Object?>[];
    return values
        .map((value) => LocalImageItem.fromPlatformMap(
              Map<Object?, Object?>.from(value! as Map),
            ))
        .toList();
  }

  Future<LocalImageItem> import({
    required String sourcePath,
    required String fileName,
    required String title,
    required List<String> tags,
    required int width,
    required int height,
    required String mode,
    ImageRatioSettings ratio = const ImageRatioSettings(),
  }) async {
    final value = await _channel.invokeMapMethod<Object?, Object?>(
      'importLocalImage',
      {
        'scope': scope,
        'ownerId': ownerId,
        'sourcePath': sourcePath,
        'fileName': fileName,
        'title': title,
        'tags': tags,
        'width': width,
        'height': height,
        'mode': mode,
        'ratio': ratio.toMap(),
      },
    );
    if (value == null) throw StateError('로컬 이미지 정보를 받지 못했습니다.');
    return LocalImageItem.fromPlatformMap(value);
  }

  Future<void> markUnshared(String id) {
    return _channel.invokeMethod<void>('markLocalImageUnshared',
        {'scope': scope, 'ownerId': ownerId, 'id': id});
  }

  Future<void> updateTags({required String id, required List<String> tags}) {
    return _channel.invokeMethod<void>('updateLocalImageTag', {
      'id': id,
      'scope': scope,
      'ownerId': ownerId,
      'tags': tags,
    });
  }

  Future<void> updateDetails({
    required String id,
    required String title,
    required List<String> tags,
    ImageRatioSettings? ratio,
  }) =>
      _channel.invokeMethod<void>('updateLocalImageDetails', {
        'scope': scope,
        'ownerId': ownerId,
        'id': id,
        'title': title,
        'tags': tags,
        if (ratio != null) 'ratio': ratio.toMap(),
      });

  Future<void> markShared({
    required String id,
    required String cloudProjectId,
    required String galleryPostId,
  }) {
    return _channel.invokeMethod<void>('markLocalImageShared', {
      'scope': scope,
      'ownerId': ownerId,
      'id': id,
      'cloudProjectId': cloudProjectId,
      'galleryPostId': galleryPostId,
    });
  }

  Future<void> delete(String id) => _channel.invokeMethod<void>(
      'deleteLocalImage', {'scope': scope, 'ownerId': ownerId, 'id': id});

  Future<String> downloadShared(
      {required String url, required String fileName}) async {
    final path = await _channel.invokeMethod<String>('downloadSharedImage', {
      'scope': scope,
      'ownerId': ownerId,
      'url': url,
      'fileName': fileName,
    });
    if (path == null) throw StateError('공유 이미지를 받지 못했습니다.');
    return path;
  }
}
