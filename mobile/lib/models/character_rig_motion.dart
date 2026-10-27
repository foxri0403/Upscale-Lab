import 'dart:math' as math;
import 'package:flutter/widgets.dart';

/// Small-angle, sensor-driven pseudo-rig for See-through PNG part layers.
/// Every exported layer must have the same pixel canvas and stacking origin.
class CharacterRigPose {
  const CharacterRigPose({
    required this.offset,
    required this.angleRadians,
  });

  final Offset offset;
  final double angleRadians;
}

abstract final class CharacterRigMotion {
  static const double maxHeadDegrees = 6.0;

  /// The backend's semantic part classification determines relative motion.
  /// This is a 2.5D part transform, not a generated Cubism (.moc3) model.
  static CharacterRigPose pose({
    required String layerType,
    required Offset tilt,
    required Size canvas,
    Offset? laggedTilt,
    double sensitivity = 1,
  }) {
    final x = tilt.dx.clamp(-1.0, 1.0).toDouble();
    final y = tilt.dy.clamp(-1.0, 1.0).toDouble();
    final gain = sensitivity.clamp(0.0, 1.5).toDouble();
    final headAngle = -x * math.min(maxHeadDegrees, maxHeadDegrees * gain) * math.pi / 180;
    final dx = -x * canvas.width * 0.013 * gain;
    final dy = y * canvas.height * 0.009 * gain;

    switch (layerType.toLowerCase()) {
      case 'background':
        return const CharacterRigPose(offset: Offset.zero, angleRadians: 0);
      case 'eye':
        return CharacterRigPose(
          offset: Offset(dx - x * canvas.width * 0.008 * gain,
              dy + y * canvas.height * 0.003 * gain),
          angleRadians: headAngle,
        );
      case 'eyebrow':
      case 'nose':
      case 'mouth':
      case 'face':
        return CharacterRigPose(offset: Offset(dx, dy), angleRadians: headAngle);
      case 'fronthair':
      case 'backhair':
      case 'hair':
        final delayed = laggedTilt ?? tilt;
        final hx = delayed.dx.clamp(-1.0, 1.0).toDouble();
        final hy = delayed.dy.clamp(-1.0, 1.0).toDouble();
        return CharacterRigPose(
          offset: Offset(-hx * canvas.width * 0.019 * gain,
              hy * canvas.height * 0.012 * gain),
          angleRadians: -hx * math.min(maxHeadDegrees, maxHeadDegrees * gain * 1.12) * math.pi / 180,
        );
      case 'body':
      case 'clothes':
        return CharacterRigPose(
          offset: Offset(dx * 0.20, dy * 0.15),
          angleRadians: headAngle * 0.15,
        );
      default:
        // Unrecognized See-through tags remain visible, but do not jump.
        return CharacterRigPose(
          offset: Offset(dx * 0.45, dy * 0.45),
          angleRadians: headAngle * 0.45,
        );
    }
  }
}
