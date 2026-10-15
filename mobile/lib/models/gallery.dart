import 'gallery_tag_codec.dart';
import 'gallery_tags.dart';

export 'gallery_tags.dart';

class GalleryPostItem {
  const GalleryPostItem({
    required this.id,
    required this.username,
    required this.title,
    required this.imageUrl,
    required this.tag,
    this.selectedTags,
    this.thumbnailUrl,
    this.fileName,
    this.savedUserCount,
    required this.createdAt,
  });

  final String id;
  final String username;
  final String title;
  final String imageUrl;
  final String tag;
  final List<String>? selectedTags;
  final String? thumbnailUrl;
  final String? fileName;
  /// Number of distinct users who saved this post, when supplied by the API.
  final int? savedUserCount;
  List<String> get tags => selectedTags ?? [tag];
  final DateTime createdAt;

  bool matches(
      {String? search, String? selectedTag, Iterable<String>? selectedTags}) {
    if (selectedTag != null && !tags.contains(selectedTag)) return false;
    if (selectedTags != null &&
        selectedTags.isNotEmpty &&
        !selectedTags.any(tags.contains)) {
      return false;
    }
    final normalizedSearch = search?.trim().toLowerCase();
    return normalizedSearch == null ||
        normalizedSearch.isEmpty ||
        title.toLowerCase().contains(normalizedSearch);
  }

  static int? _readSavedUserCount(Map<String, dynamic> json) {
    final value = json['savedUserCount'];
    if (value is int && value >= 0) return value;
    return null;
  }

  factory GalleryPostItem.fromJson(Map<String, dynamic> json) {
    final tags = GalleryTagCodec.fromPost(json);
    return GalleryPostItem(
      id: json['id'] as String,
      username: json['username'] as String,
      title: json['title'] as String,
      imageUrl: json['imageUrl'] as String,
      tag: json['tag'] as String? ?? unspecifiedGalleryTag,
      selectedTags: tags.isEmpty ? [unspecifiedGalleryTag] : tags,
      thumbnailUrl: json['thumbnailUrl'] as String?,
      fileName: json['fileName'] as String?,
      savedUserCount: _readSavedUserCount(json),
      createdAt: DateTime.parse(json['createdAt'] as String).toLocal(),
    );
  }
}
