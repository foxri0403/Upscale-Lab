import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/models/local_image.dart';
import 'package:live_layer/models/image_editor.dart';

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
    expect(local.tag, '장르 설정되지 않음');
    expect(local.ratio.automatic, isTrue);
    expect(shared.isShared, isTrue);
  });

  test('image ratio metadata restores the manual preset and orientation', () {
    final item = LocalImageItem.fromPlatformMap({
      'id': 'image',
      'title': 'photo',
      'path': '/photo.png',
      'width': 40,
      'height': 40,
      'mode': 'original',
      'createdAt': '2026-10-14T00:00:00Z',
      'ratio': {'mode': 'manual', 'preset': 'standard', 'portrait': false},
    });
    expect(item.ratio.automatic, isFalse);
    expect(item.ratio.preset, CropPreset.standard);
    expect(item.ratio.portrait, isFalse);
  });
}
