import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/models/character_layer_diagnostics.dart';
import 'package:live_layer/models/live_layer_models.dart';

LiveLayer part(String type, int order) => LiveLayer(
      id: 'part-$order',
      imageUrl: 'https://example.invalid/part-$order.png',
      layerType: type,
      layerOrder: order,
      depth: 0,
      positionX: 0,
      positionY: 0,
      rotation: 0,
      scale: 1,
      movementX: 0,
      movementY: 0,
    );

void main() {
  test('two-layer object split is not detailed character parts', () {
    final result = CharacterLayerDiagnostics.inspect([
      part('Background', 0), part('FrontPart', 1),
    ]);
    expect(result.hasCharacterParts, isFalse);
    expect(result.hasEyes, isFalse);
  });

  test('face, eyes and hair are detected from actual layer types', () {
    final result = CharacterLayerDiagnostics.inspect([
      part('Background', 0), part('Body', 1),
      part('Face', 2), part('Eye', 3), part('FrontHair', 4),
    ]);
    expect(result.hasCharacterParts, isTrue);
    expect(result.hasEyes, isTrue);
    expect(result.hasHair, isTrue);
    expect(result.hasBackground, isTrue);
  });

  test('partial character split is reported accurately', () {
    final result = CharacterLayerDiagnostics.inspect([
      part('Body', 0), part('Face', 1),
    ]);
    expect(result.hasCharacterParts, isTrue);
    expect(result.hasEyes, isFalse);
    expect(result.hasHair, isFalse);
  });
}
