import 'live_layer_models.dart';

/// Inspect the actual layer categories returned by the server.
/// Never report a foreground/background split as detailed character rigging.
class CharacterLayerDiagnostics {
  const CharacterLayerDiagnostics({
    required this.count,
    required this.hasFace,
    required this.hasEyes,
    required this.hasHair,
    required this.hasBackground,
  });

  final int count;
  final bool hasFace;
  final bool hasEyes;
  final bool hasHair;
  final bool hasBackground;

  bool get hasCharacterParts => hasFace && count > 1;

  static CharacterLayerDiagnostics inspect(List<LiveLayer> layers) {
    final types = layers.map((item) => item.layerType.toLowerCase()).toSet();
    return CharacterLayerDiagnostics(
      count: layers.length,
      hasFace: types.contains('face'),
      hasEyes: types.contains('eye'),
      hasHair: types.contains('hair') ||
          types.contains('fronthair') || types.contains('backhair'),
      hasBackground: types.contains('background'),
    );
  }
}
