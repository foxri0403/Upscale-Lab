import 'dart:ui';

import 'image_editor.dart';

/// Automatic ratios are resolved on the device using the image, never persisted
/// as a fixed screen size. Manual choices survive switching modes and restarts.
class ImageRatioSettings {
  const ImageRatioSettings({
    this.automatic = true,
    this.preset = CropPreset.original,
    this.portrait = true,
    this.freeAspectRatio,
  });

  final bool automatic;
  final CropPreset preset;
  final bool portrait;
  final double? freeAspectRatio;

  ImageRatioSettings copyWith({
    bool? automatic,
    CropPreset? preset,
    bool? portrait,
    double? freeAspectRatio,
  }) =>
      ImageRatioSettings(
        automatic: automatic ?? this.automatic,
        preset: preset ?? this.preset,
        portrait: portrait ?? this.portrait,
        freeAspectRatio: freeAspectRatio ?? this.freeAspectRatio,
      );

  double? aspectRatio(Size source, Size display, {bool editing = false}) {
    if (automatic) return display.aspectRatio;
    if (preset == CropPreset.free) {
      return editing ? null : freeAspectRatio ?? source.aspectRatio;
    }
    return preset.aspectRatio(portrait: portrait, source: source);
  }

  Map<String, Object?> toMap() => {
        'mode': automatic ? 'automatic' : 'manual',
        'preset': preset.name,
        'portrait': portrait,
        if (freeAspectRatio != null) 'freeAspectRatio': freeAspectRatio,
      };

  factory ImageRatioSettings.fromMap(Map<Object?, Object?>? map) {
    if (map == null) return const ImageRatioSettings();
    final freeRatio = (map['freeAspectRatio'] as num?)?.toDouble();
    return ImageRatioSettings(
      automatic: map['mode'] != 'manual',
      preset: CropPreset.values.firstWhere(
        (preset) => preset.name == map['preset'],
        orElse: () => CropPreset.original,
      ),
      portrait: map['portrait'] != false,
      freeAspectRatio: freeRatio != null && freeRatio.isFinite && freeRatio > 0
          ? freeRatio
          : null,
    );
  }
}
