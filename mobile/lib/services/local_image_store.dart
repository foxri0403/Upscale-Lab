import 'package:flutter/services.dart';

import '../models/local_image.dart';
import '../models/library_order.dart';
import 'wallpaper_service.dart';

class LocalImageStore {
  const LocalImageStore({required this.scope});

  final String scope;

  static const _channel = MethodChannel('live_layer/storage');

  Future<LibraryOrder> getOrder() async =>
      LibraryOrder.fromMap(await _channel.invokeMapMethod<Object?, Object?>(
              'getLibraryOrder', {'scope': scope}) ??
          const {});

  Future<void> saveOrder(LibraryOrder order) => _channel.invokeMethod<void>(
      'saveLibraryOrder', {'scope': scope, ...order.toMap()});

  Future<bool> previewWallpaper(String id) async =>
      await _channel.invokeMethod<bool>(
          'previewLocalWallpaper', {'scope': scope, 'id': id}) ??
      false;

  /// Applies an image owned by the captured account to the selected screen(s).
  /// Use this after the UI chooses a target; the system preview controls its own
  /// target selection and cannot enforce this option.
  Future<void> applyWallpaper(String id, {required WallpaperTarget target}) =>
      _channel.invokeMethod<void>('applyLocalWallpaper', {
        'scope': scope,
        'id': id,
        'target': target.name,
      });

  Future<List<LocalImageItem>> getAll() async {
    final values = await _channel
            .invokeListMethod<Object?>('listLocalImages', {'scope': scope}) ??
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
  }) async {
    final value = await _channel.invokeMapMethod<Object?, Object?>(
      'importLocalImage',
      {
        'scope': scope,
        'sourcePath': sourcePath,
        'fileName': fileName,
        'title': title,
        'tags': tags,
        'width': width,
        'height': height,
        'mode': mode,
      },
    );
    if (value == null) throw StateError('로컬 이미지 정보를 받지 못했습니다.');
    return LocalImageItem.fromPlatformMap(value);
  }

  Future<void> markUnshared(String id) {
    return _channel.invokeMethod<void>(
        'markLocalImageUnshared', {'scope': scope, 'id': id});
  }

  Future<void> updateTags({required String id, required List<String> tags}) {
    return _channel.invokeMethod<void>('updateLocalImageTag', {
      'id': id,
      'scope': scope,
      'tags': tags,
    });
  }

  Future<void> updateDetails({
    required String id,
    required String title,
    required List<String> tags,
  }) =>
      _channel.invokeMethod<void>('updateLocalImageDetails', {
        'scope': scope,
        'id': id,
        'title': title,
        'tags': tags,
      });

  Future<void> markShared({
    required String id,
    required String cloudProjectId,
    required String galleryPostId,
  }) {
    return _channel.invokeMethod<void>('markLocalImageShared', {
      'scope': scope,
      'id': id,
      'cloudProjectId': cloudProjectId,
      'galleryPostId': galleryPostId,
    });
  }

  Future<void> delete(String id) => _channel
      .invokeMethod<void>('deleteLocalImage', {'scope': scope, 'id': id});

  Future<String> downloadShared(
      {required String url, required String fileName}) async {
    final path = await _channel.invokeMethod<String>('downloadSharedImage', {
      'scope': scope,
      'url': url,
      'fileName': fileName,
    });
    if (path == null) throw StateError('공유 이미지를 받지 못했습니다.');
    return path;
  }
}
