import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/models/gallery.dart';

void main() {
  final post = GalleryPostItem(
    id: 'post-id',
    username: 'animal_artist',
    title: 'Retro Car',
    imageUrl: 'https://example.test/car.png',
    tag: '자동차',
    createdAt: DateTime.utc(2026, 10, 11),
  );

  test('gallery filtering requires an exact tag match', () {
    expect(post.matches(selectedTag: '자동차'), isTrue);
    expect(post.matches(selectedTag: '동물'), isFalse);
  });

  test('gallery search matches title only', () {
    expect(post.matches(search: 'retro'), isTrue);
    expect(post.matches(search: 'animal_artist'), isFalse);
    expect(post.matches(search: '자동차'), isFalse);
  });

  test('gallery reads the persisted download count and defaults to zero', () {
    final json = <String, dynamic>{'id': 'post-id', 'username': 'artist',
      'title': 'Photo', 'imageUrl': 'https://example.test/photo.png',
      'tag': '풍경', 'createdAt': '2026-10-16T00:00:00Z'};
    expect(GalleryPostItem.fromJson(json).downloadCount, 0);
    expect(GalleryPostItem.fromJson({...json, 'downloadCount': 27}).downloadCount, 27);
    for (final invalid in [null, -1, '27', 2.5]) {
      expect(GalleryPostItem.fromJson({...json, 'downloadCount': invalid}).downloadCount, 0);
    }
  });
}
