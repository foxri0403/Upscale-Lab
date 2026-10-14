import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/models/local_image.dart';
import 'package:live_layer/models/library_order.dart';

void main() {
  LocalImageItem image(String id, String title, int day) => LocalImageItem(
      id: id,
      title: title,
      path: '/$id.png',
      width: 100,
      height: 200,
      mode: 'original',
      tag: '풍경',
      createdAt: DateTime.utc(2026, 10, day));
  final images = [
    image('z', '하늘', 2),
    image('b', 'beta', 1),
    image('a', 'Apple', 3),
    image('g', '가을', 4)
  ];
  List<String> ids(LibraryOrder order) =>
      order.arrange(images).map((i) => i.id).toList();

  test('date and Korean/English sorts actually arrange all items', () {
    expect(ids(const LibraryOrder(sort: LibrarySort.newest)),
        ['g', 'a', 'z', 'b']);
    expect(ids(const LibraryOrder(sort: LibrarySort.oldest)),
        ['b', 'z', 'a', 'g']);
    expect(ids(const LibraryOrder(sort: LibrarySort.korean)),
        ['g', 'z', 'a', 'b']);
    expect(ids(const LibraryOrder(sort: LibrarySort.english)),
        ['a', 'b', 'g', 'z']);
  });
  test(
      'starting a drag snapshots displayed sort before moving and survives serialization',
      () {
    const ordered =
        LibraryOrder(sort: LibrarySort.korean, ids: ['b', 'a', 'z', 'g']);
    final manual = ordered.beginDrag(ordered.arrange(images));
    expect(ids(manual), ids(ordered));
    final moved = manual.move('g', 'a');
    expect(ids(LibraryOrder.fromMap(moved.toMap())), ['z', 'a', 'g', 'b']);
  });
  test('manual order retains saved positions and puts new items after them',
      () {
    expect(ids(const LibraryOrder(ids: ['removed', 'b', 'z'])),
        ['b', 'z', 'g', 'a']);
    expect(
        LibraryOrder.fromMap({
          'sort': 'invalid',
          'ids': ['a', 'a']
        }).ids,
        ['a']);
  });
}
