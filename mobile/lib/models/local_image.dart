class LocalImageItem {
  const LocalImageItem({
    required this.id,
    required this.title,
    required this.path,
    required this.width,
    required this.height,
    required this.mode,
    required this.createdAt,
    this.cloudProjectId,
    this.galleryPostId,
  });

  final String id;
  final String title;
  final String path;
  final int width;
  final int height;
  final String mode;
  final DateTime createdAt;
  final String? cloudProjectId;
  final String? galleryPostId;

  bool get isShared => cloudProjectId != null && galleryPostId != null;

  factory LocalImageItem.fromPlatformMap(Map<Object?, Object?> map) {
    return LocalImageItem(
      id: map['id']! as String,
      title: map['title']! as String,
      path: map['path']! as String,
      width: (map['width']! as num).toInt(),
      height: (map['height']! as num).toInt(),
      mode: map['mode']! as String,
      createdAt: DateTime.parse(map['createdAt']! as String),
      cloudProjectId: map['cloudProjectId'] as String?,
      galleryPostId: map['galleryPostId'] as String?,
    );
  }
}
