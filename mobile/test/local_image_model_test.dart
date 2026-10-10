import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/models/local_image.dart';

void main() {
  test('local image is shared only when project and gallery ids exist', () {
    final local = LocalImageItem.fromPlatformMap({
      'id': 'local-id',
      'title': 'photo.png',
      'path': '/data/photo.png',
      'width': 1080,
      'height': 1920,
      'mode': 'original',
      'createdAt': '2026-10-10T10:00:00.000Z',
      'cloudProjectId': null,
      'galleryPostId': null,
    });
    final shared = LocalImageItem.fromPlatformMap({
      'id': 'local-id',
      'title': 'photo.png',
      'path': '/data/photo.png',
      'width': 1080,
      'height': 1920,
      'mode': 'original',
      'createdAt': '2026-10-10T10:00:00.000Z',
      'cloudProjectId': 'project-id',
      'galleryPostId': 'post-id',
    });

    expect(local.isShared, isFalse);
    expect(shared.isShared, isTrue);
  });
}
