import 'dart:math' as math;
import 'dart:ui';

enum CropPreset { original, free, wide, standard, square }

extension CropPresetDetails on CropPreset {
  String label(bool portrait) => switch (this) {
        CropPreset.original => '원본',
        CropPreset.free => '자유 형태',
        CropPreset.wide => portrait ? '9:16' : '16:9',
        CropPreset.standard => portrait ? '3:4' : '4:3',
        CropPreset.square => '1:1',
      };

  double? aspectRatio({
    required bool portrait,
    required Size source,
  }) {
    return switch (this) {
      CropPreset.original => source.aspectRatio,
      CropPreset.free => null,
      CropPreset.wide => portrait ? 9 / 16 : 16 / 9,
      CropPreset.standard => portrait ? 3 / 4 : 4 / 3,
      CropPreset.square => 1,
    };
  }
}

enum PhotoFilter { original, vivid, soft, monochrome, sepia }

extension PhotoFilterDetails on PhotoFilter {
  String get label => switch (this) {
        PhotoFilter.original => '원본',
        PhotoFilter.vivid => '선명',
        PhotoFilter.soft => '차분',
        PhotoFilter.monochrome => '흑백',
        PhotoFilter.sepia => '세피아',
      };

  // One representative per effect family; no warm/cool variants.
  List<double> get matrix {
    if (this == PhotoFilter.sepia) {
      return const [
        .393,
        .769,
        .189,
        0,
        0,
        .349,
        .686,
        .168,
        0,
        0,
        .272,
        .534,
        .131,
        0,
        0,
        0,
        0,
        0,
        1,
        0,
      ];
    }
    final (contrast, saturation, brightness) = switch (this) {
      PhotoFilter.vivid => (1.12, 1.3, 2.0),
      PhotoFilter.soft => (.85, .7, 12.0),
      PhotoFilter.monochrome => (1.0, 0.0, 0.0),
      _ => (1.0, 1.0, 0.0),
    };
    const luminance = [.2126, .7152, .0722];
    return [
      for (var row = 0; row < 3; row++) ...[
        for (var column = 0; column < 3; column++)
          contrast *
              (luminance[column] * (1 - saturation) +
                  (row == column ? saturation : 0)),
        0,
        128 * (1 - contrast) + brightness,
      ],
      0,
      0,
      0,
      1,
      0,
    ];
  }
}

/// Image and crop coordinates share one transform, used by preview and export.
/// The image always covers the frame, so gestures cannot introduce blank pixels.
class CropGeometry {
  CropGeometry(this.source);

  final Size source;
  Size viewport = Size.zero;
  Rect frame = Rect.zero;
  double scale = 1;
  Offset offset = Offset.zero;
  double? _aspect;

  double get minimumScale =>
      math.max(frame.width / source.width, frame.height / source.height);
  Rect get imageRect => offset & (source * scale);
  Rect get sourceRect => Rect.fromLTRB(
        ((frame.left - offset.dx) / scale).clamp(0, source.width),
        ((frame.top - offset.dy) / scale).clamp(0, source.height),
        ((frame.right - offset.dx) / scale).clamp(0, source.width),
        ((frame.bottom - offset.dy) / scale).clamp(0, source.height),
      );

  Rect get normalizedCrop {
    final crop = sourceRect;
    return Rect.fromLTRB(crop.left / source.width, crop.top / source.height,
        crop.right / source.width, crop.bottom / source.height);
  }

  void layout(Size size, double? aspect, {bool reset = false}) {
    if (!reset && size == viewport && aspect == _aspect) return;
    final previous =
        viewport.isEmpty || reset || aspect != _aspect ? null : sourceRect;
    viewport = size;
    _aspect = aspect;
    final bounds = (Offset.zero & size).deflate(24);
    final ratio = aspect ?? previous?.size.aspectRatio ?? source.aspectRatio;
    final width = math.min(bounds.width, bounds.height * ratio);
    frame = Rect.fromCenter(
        center: bounds.center, width: width, height: width / ratio);
    final crop = previous ?? (Offset.zero & source);
    scale = math.max(frame.width / crop.width, frame.height / crop.height);
    offset = frame.center - crop.center * scale;
    transform(scale, offset);
  }

  void transform(double nextScale, Offset nextOffset) {
    scale = nextScale.clamp(minimumScale, minimumScale * 8);
    offset = Offset(
      nextOffset.dx.clamp(frame.right - source.width * scale, frame.left),
      nextOffset.dy.clamp(frame.bottom - source.height * scale, frame.top),
    );
  }

  /// Corner order: top left, top right, bottom right, bottom left.
  void resizeCorner(int corner, Offset position) {
    final left = corner == 0 || corner == 3;
    final top = corner == 0 || corner == 1;
    final anchor =
        Offset(left ? frame.right : frame.left, top ? frame.bottom : frame.top);
    final bounds = imageRect.intersect((Offset.zero & viewport).deflate(16));
    final maxWidth = left ? anchor.dx - bounds.left : bounds.right - anchor.dx;
    final maxHeight = top ? anchor.dy - bounds.top : bounds.bottom - anchor.dy;
    double width = (left ? anchor.dx - position.dx : position.dx - anchor.dx)
        .clamp(math.min(48.0, maxWidth), maxWidth)
        .toDouble();
    double height = (top ? anchor.dy - position.dy : position.dy - anchor.dy)
        .clamp(math.min(48.0, maxHeight), maxHeight)
        .toDouble();
    if (_aspect case final ratio?) {
      final max = math.min(maxWidth, maxHeight * ratio);
      final min = math.min(max, math.max(48.0, 48 * ratio));
      width = math.max(width, height * ratio).clamp(min, max);
      height = width / ratio;
    }
    frame = Rect.fromLTWH(left ? anchor.dx - width : anchor.dx,
        top ? anchor.dy - height : anchor.dy, width, height);
  }
}
