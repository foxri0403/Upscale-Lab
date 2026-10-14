import 'dart:ui';

import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/models/image_editor.dart';
import 'package:live_layer/models/image_ratio.dart';

void main() {
  const source = Size(4000, 2000);
  test(
      'automatic always resolves the current device, regardless of manual choice',
      () {
    const ratio = ImageRatioSettings(preset: CropPreset.wide, portrait: false);
    for (final display in [
      const Size(1080, 2400),
      const Size(2400, 1080),
      const Size(1800, 1800)
    ]) {
      expect(ratio.aspectRatio(source, display), display.aspectRatio);
      final geometry = CropGeometry(source)
        ..layout(const Size(360, 360), ratio.aspectRatio(source, display));
      expect(geometry.sourceRect.size.aspectRatio,
          closeTo(display.aspectRatio, 1e-9));
    }
  });
  test('manual selection and orientation survive switching modes and storage',
      () {
    const ratio = ImageRatioSettings(
        automatic: false, preset: CropPreset.standard, portrait: false);
    final restored =
        ImageRatioSettings.fromMap(ratio.copyWith(automatic: true).toMap())
            .copyWith(automatic: false);
    expect(restored.aspectRatio(source, const Size(1080, 2400)), 4 / 3);
    expect(restored.aspectRatio(source, const Size(2400, 1080)), 4 / 3);
    expect(restored.preset, CropPreset.standard);
  });
  test(
      'free crop is unconstrained in editor and preserves exported manual aspect',
      () {
    const ratio = ImageRatioSettings(
        automatic: false, preset: CropPreset.free, freeAspectRatio: 1.7);
    final restored = ImageRatioSettings.fromMap(ratio.toMap());
    expect(restored.aspectRatio(source, const Size(1080, 2400), editing: true),
        isNull);
    expect(restored.aspectRatio(source, const Size(1080, 2400)), 1.7);
  });
  test('legacy and invalid ratios fall back safely', () {
    expect(ImageRatioSettings.fromMap(null).automatic, isTrue);
    expect(ImageRatioSettings.fromMap({'preset': 'screen'}).preset,
        CropPreset.original);
    expect(
        ImageRatioSettings.fromMap({'freeAspectRatio': double.nan})
            .freeAspectRatio,
        isNull);
  });
}
