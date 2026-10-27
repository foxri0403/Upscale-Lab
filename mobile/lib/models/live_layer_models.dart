class LiveLayerProject {
  const LiveLayerProject({
    required this.id,
    required this.title,
    required this.status,
    required this.originalImageUrl,
    required this.layers,
    this.originalWidth = 0,
    this.originalHeight = 0,
  });

  final String id;
  final String title;
  final String status;
  final String originalImageUrl;
  final List<LiveLayer> layers;
  final int originalWidth;
  final int originalHeight;

  factory LiveLayerProject.fromJson(Map<String, dynamic> json) {
    return LiveLayerProject(
      id: json['id'] as String,
      title: json['title'] as String,
      status: json['status'] as String,
      originalImageUrl: json['originalImageUrl'] as String,
      originalWidth: (json['originalWidth'] as num?)?.toInt() ?? 0,
      originalHeight: (json['originalHeight'] as num?)?.toInt() ?? 0,
      layers: (json['layers'] as List<dynamic>? ?? const [])
          .map((item) => LiveLayer.fromJson(item as Map<String, dynamic>))
          .toList(),
    );
  }
}

class LiveLayer {
  const LiveLayer({
    required this.id,
    required this.imageUrl,
    this.layerType = 'Other',
    required this.layerOrder,
    required this.depth,
    required this.positionX,
    required this.positionY,
    required this.rotation,
    required this.scale,
    required this.movementX,
    required this.movementY,
  });

  final String id;
  final String imageUrl;
  final String layerType;
  final int layerOrder;
  final double depth;
  final double positionX;
  final double positionY;
  final double rotation;
  final double scale;
  final double movementX;
  final double movementY;

  factory LiveLayer.fromJson(Map<String, dynamic> json) {
    double number(String key, [double fallback = 0]) =>
        (json[key] as num?)?.toDouble() ?? fallback;
    return LiveLayer(
      id: json['id'] as String,
      imageUrl: json['imageUrl'] as String,
      layerType: json['layerType']?.toString() ?? 'Other',
      layerOrder: json['layerOrder'] as int,
      depth: number('depth'),
      positionX: number('positionX'),
      positionY: number('positionY'),
      rotation: number('rotation'),
      scale: number('scale', 1),
      movementX: number('movementX'),
      movementY: number('movementY'),
    );
  }

  Map<String, dynamic> toWallpaperJson() => {
        'id': id,
        'imageUrl': imageUrl,
        'layerOrder': layerOrder,
        'depth': depth,
        'positionX': positionX,
        'positionY': positionY,
        'rotation': rotation,
        'scale': scale,
        'movementX': movementX,
        'movementY': movementY,
      };
}
