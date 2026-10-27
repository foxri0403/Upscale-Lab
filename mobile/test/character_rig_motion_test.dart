import 'dart:math' as math;
import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/models/character_rig_motion.dart';

void main() {
  const size = Size(400, 600);
  test('background stays fixed for all tilt directions', () {
    for (final tilt in [const Offset(-1, 1), Offset.zero, const Offset(1, -1)]) {
      final pose = CharacterRigMotion.pose(layerType: 'Background', tilt: tilt, canvas: size);
      expect(pose.offset, Offset.zero);
      expect(pose.angleRadians, 0);
    }
  });

  test('eyes move independently while head tilts in a bounded angle', () {
    final face = CharacterRigMotion.pose(layerType: 'Face', tilt: const Offset(.8, .2), canvas: size);
    final eyes = CharacterRigMotion.pose(layerType: 'Eye', tilt: const Offset(.8, .2), canvas: size);
    expect(face.angleRadians.abs(), lessThanOrEqualTo(6 * math.pi / 180));
    expect(eyes.offset.dx, isNot(face.offset.dx));
  });

  test('hair uses lagged tilt and never rotates past six degrees', () {
    final hair = CharacterRigMotion.pose(
      layerType: 'FrontHair', tilt: const Offset(1, .3),
      laggedTilt: const Offset(.2, .1), canvas: size, sensitivity: 1.5);
    final face = CharacterRigMotion.pose(
      layerType: 'Face', tilt: const Offset(1, .3), canvas: size, sensitivity: 1.5);
    expect(hair.offset.dx.abs(), lessThan(face.offset.dx.abs() * 3));
    expect(hair.angleRadians.abs(), lessThanOrEqualTo(6 * math.pi / 180 + 1e-9));
    expect(face.angleRadians.abs(), lessThanOrEqualTo(6 * math.pi / 180 + 1e-9));
  });
}
