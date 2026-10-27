import 'dart:math' as math;
import 'package:flutter/material.dart';
import '../models/character_rig_motion.dart';
import '../models/live_layer_models.dart';

/// Renders full-canvas transparent PNG parts in their backend stacking order.
/// The background is fixed; face, eyes and hair have independent motion.
class CharacterLive2DPreview extends StatelessWidget {
  const CharacterLive2DPreview({
    super.key,
    required this.layers,
    required this.tilt,
    required this.laggedTilt,
    this.sensitivity = 1,
    this.headAnchorY = 0.32,
    this.showOnlyType,
  });

  final List<LiveLayer> layers;
  final Offset tilt;
  final Offset laggedTilt;
  final double sensitivity;
  final double headAnchorY;
  final String? showOnlyType;

  @override
  Widget build(BuildContext context) {
    final ordered = [...layers]
      ..sort((a, b) => a.layerOrder.compareTo(b.layerOrder));
    return LayoutBuilder(builder: (context, constraints) {
      final canvas = Size(constraints.maxWidth, constraints.maxHeight);
      final pivot = FractionalOffset(0.5, headAnchorY.clamp(.1, .8).toDouble());
      return ClipRect(
        child: Stack(
          fit: StackFit.expand,
          children: [
            const ColoredBox(color: Color(0xFF30343B)),
            for (final layer in ordered)
              if (showOnlyType == null ||
                  layer.layerType.toLowerCase() ==
                      showOnlyType!.toLowerCase())
                _part(layer, canvas, pivot),
          ],
        ),
      );
    });
  }

  Widget _part(LiveLayer layer, Size canvas, Alignment pivot) {
    final pose = CharacterRigMotion.pose(
      layerType: layer.layerType,
      tilt: tilt,
      laggedTilt: laggedTilt,
      canvas: canvas,
      sensitivity: sensitivity,
    );
    return Transform.translate(
      key: ValueKey('part-translate-${layer.id}'),
      offset: pose.offset,
      child: Transform.rotate(
        angle: pose.angleRadians,
        alignment: pivot,
        child: Transform.scale(
          // A small common overscan prevents edge gaps at extreme tilt.
          scale: 1.04,
          child: Image.network(
            layer.imageUrl,
            fit: BoxFit.fill,
            cacheWidth: math.max(128, math.min(1280, canvas.width.ceil() * 2)),
            gaplessPlayback: true,
            errorBuilder: (_, __, ___) => Center(
              child: DecoratedBox(
                decoration: BoxDecoration(
                  color: const Color(0xB8000000),
                  borderRadius: BorderRadius.circular(6),
                ),
                child: Padding(
                  padding: const EdgeInsets.all(8),
                  child: Text('레이어 이미지 로드 실패: ${layer.layerType}',
                    style: const TextStyle(color: Colors.white),
                    textAlign: TextAlign.center),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
