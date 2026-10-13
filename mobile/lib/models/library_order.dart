import 'local_image.dart';

enum LibrarySort {
  manual('사용자순'),
  newest('등록순'),
  oldest('오래된순'),
  korean('가나다순'),
  english('ABC순');

  const LibrarySort(this.label);
  final String label;
}

class LibraryOrder {
  const LibraryOrder({this.sort = LibrarySort.manual, this.ids = const []});
  final LibrarySort sort;
  final List<String> ids;

  factory LibraryOrder.fromMap(Map<Object?, Object?> map) => LibraryOrder(
        sort: LibrarySort.values
                .where((value) => value.name == map['sort'])
                .firstOrNull ??
            LibrarySort.manual,
        ids: (map['ids'] as List?)?.whereType<String>().toSet().toList() ??
            const [],
      );

  Map<String, Object> toMap() => {'sort': sort.name, 'ids': ids};

  List<LocalImageItem> arrange(List<LocalImageItem> source) {
    final positions = {for (var i = 0; i < ids.length; i++) ids[i]: i};
    final result = [...source];
    int byDate(LocalImageItem a, LocalImageItem b) {
      final comparison = a.createdAt.compareTo(b.createdAt);
      return comparison != 0 ? comparison : a.id.compareTo(b.id);
    }

    result.sort((a, b) {
      switch (sort) {
        case LibrarySort.manual:
          final ai = positions[a.id];
          final bi = positions[b.id];
          if (ai == null && bi == null) return -byDate(a, b);
          if (ai == null) return 1;
          if (bi == null) return -1;
          return ai.compareTo(bi);
        case LibrarySort.newest:
          return -byDate(a, b);
        case LibrarySort.oldest:
          return byDate(a, b);
        case LibrarySort.korean:
        case LibrarySort.english:
          final at = a.title.trim().toLowerCase();
          final bt = b.title.trim().toLowerCase();
          final group = _group(at).compareTo(_group(bt));
          if (group != 0) return group;
          final name = at.compareTo(bt);
          return name != 0 ? name : byDate(a, b);
      }
    });
    return result;
  }

  int _group(String text) {
    final korean = RegExp(r'^[가-힣ㄱ-ㅎㅏ-ㅣ]').hasMatch(text);
    final english = RegExp(r'^[a-z]').hasMatch(text);
    if (sort == LibrarySort.english) return english ? 0 : (korean ? 1 : 2);
    return korean ? 0 : (english ? 1 : 2);
  }

  LibraryOrder beginDrag(List<LocalImageItem> displayed) =>
      LibraryOrder(ids: displayed.map((i) => i.id).toList());

  LibraryOrder move(String moving, String target) {
    final next = [...ids];
    final from = next.indexOf(moving);
    final to = next.indexOf(target);
    if (from < 0 || to < 0 || from == to) return this;
    next.removeAt(from);
    next.insert(to, moving);
    return LibraryOrder(ids: next);
  }
}
