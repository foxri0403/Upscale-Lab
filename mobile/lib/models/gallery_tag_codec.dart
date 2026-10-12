import 'dart:convert';

import 'gallery_tags.dart';

// Older gallery APIs persist Description but understand only the primary Tag.
// Confirm/read metadata returned by the server; never substitute requested tags.
class GalleryTagCodec {
  static const _marker = '\n[UpscaleLab.Tags:v1]';

  static List<String> normalize(Iterable<String> tags) {
    final values = tags.map((tag) => tag.trim()).toSet();
    if (values.any((tag) => !galleryTags.contains(tag))) {
      throw ArgumentError('지원하지 않는 갤러리 태그입니다.');
    }
    if (values.length > 1) values.remove(unspecifiedGalleryTag);
    return values.isEmpty ? [unspecifiedGalleryTag] : values.toList();
  }

  static String sharingDescription(List<String> tags) =>
      'Upscale Lab 모바일 앱에서 공유했습니다.$_marker${jsonEncode(tags)}';

  static List<String> fromPost(Map<String, dynamic> post) {
    final raw = post['tags'];
    if (raw is List && raw.isNotEmpty) return _validTags(raw);
    final primary = post['tag'];
    final description = post['description'];
    if (description is String && primary is String) {
      final marker = description.lastIndexOf(_marker);
      if (marker >= 0) {
        try {
          final decoded =
              jsonDecode(description.substring(marker + _marker.length));
          if (decoded is List) {
            final tags = _validTags(decoded);
            // A legacy primary-tag edit must invalidate old compatibility metadata.
            if (tags.isNotEmpty && tags.first == primary) return tags;
          }
        } on FormatException {
          // A regular description containing the marker is still valid text.
        }
      }
    }
    return primary is String && galleryTags.contains(primary) ? [primary] : [];
  }

  static List<String> _validTags(List<dynamic> raw) {
    if (raw.any((tag) => tag is! String || !galleryTags.contains(tag))) {
      return [];
    }
    return raw.cast<String>().toSet().toList();
  }
}
