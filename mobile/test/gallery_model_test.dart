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
}
