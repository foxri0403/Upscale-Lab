const unspecifiedGalleryTag = '장르 설정되지 않음';

const galleryTags = <String>[
  '동물',
  '애니',
  '게임',
  '여자',
  '남자',
  '풍경',
  '픽셀 아트',
  '레트로',
  'SF',
  '스포츠',
  '자동차',
  '비행기',
  '밀리터리',
  unspecifiedGalleryTag,
];

class GalleryPostItem {
  const GalleryPostItem({
    required this.id,
    required this.username,
    required this.title,
    required this.imageUrl,
    required this.tag,
    required this.createdAt,
  });

  final String id;
  final String username;
  final String title;
  final String imageUrl;
  final String tag;
  final DateTime createdAt;

  factory GalleryPostItem.fromJson(Map<String, dynamic> json) =>
      GalleryPostItem(
        id: json['id'] as String,
        username: json['username'] as String,
        title: json['title'] as String,
        imageUrl: json['imageUrl'] as String,
        tag: json['tag'] as String? ?? unspecifiedGalleryTag,
        createdAt: DateTime.parse(json['createdAt'] as String).toLocal(),
      );
}
