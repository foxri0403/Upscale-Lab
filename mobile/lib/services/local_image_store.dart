import 'package:flutter/services.dart';

import '../models/local_image.dart';

class LocalImageStore {
  const LocalImageStore();

  static const _channel = MethodChannel('live_layer/storage');

  Future<List<LocalImageItem>> getAll() async {
    final values =
        await _channel.invokeListMethod<Object?>('listLocalImages') ??
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
    required String tag,
    required int width,
    required int height,
    required String mode,
  }) async {
    final value = await _channel.invokeMapMethod<Object?, Object?>(
      'importLocalImage',
      {
        'sourcePath': sourcePath,
        'fileName': fileName,
        'title': title,
        'tag': tag,
        'width': width,
        'height': height,
        'mode': mode,
      },
    );
    if (value == null) throw StateError('로컬 이미지 정보를 받지 못했습니다.');
    return LocalImageItem.fromPlatformMap(value);
  }

  Future<void> markUnshared(String id) {
    return _channel.invokeMethod<void>('markLocalImageUnshared', {'id': id});
  }

  Future<void> updateTag({required String id, required String tag}) {
    return _channel.invokeMethod<void>('updateLocalImageTag', {
      'id': id,
      'tag': tag,
    });
  }

  Future<void> markShared({
    required String id,
    required String cloudProjectId,
    required String galleryPostId,
  }) {
    return _channel.invokeMethod<void>('markLocalImageShared', {
      'id': id,
      'cloudProjectId': cloudProjectId,
      'galleryPostId': galleryPostId,
    });
  }

  Future<void> delete(String id) =>
      _channel.invokeMethod<void>('deleteLocalImage', {'id': id});
}
