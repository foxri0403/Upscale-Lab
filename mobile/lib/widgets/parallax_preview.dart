import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:sensors_plus/sensors_plus.dart';

import '../models/live_layer_models.dart';

class ParallaxPreview extends StatefulWidget {
  const ParallaxPreview({super.key, required this.layers, required this.sensitivity});

  final List<LiveLayer> layers;
  final double sensitivity;

  @override
  State<ParallaxPreview> createState() => _ParallaxPreviewState();
}

class _ParallaxPreviewState extends State<ParallaxPreview> {
  StreamSubscription<AccelerometerEvent>? _subscription;
  double _filteredX = 0;
  double _filteredY = 0;

  @override
  void initState() {
    super.initState();
    try {
      _subscription = accelerometerEventStream().listen(
        _onSensor,
        onError: (_) => _resetSensor(),
        cancelOnError: false,
      );
    } catch (_) {
      _resetSensor();
    }
  }

  void _onSensor(AccelerometerEvent event) {
    const alpha = 0.12;
    const threshold = 0.08;
    final nextX = _filteredX + alpha * (event.x - _filteredX);
    final nextY = _filteredY + alpha * (event.y - _filteredY);
    if ((nextX - _filteredX).abs() < threshold && (nextY - _filteredY).abs() < threshold) {
      return;
    }
    if (!mounted) return;
    setState(() {
      _filteredX = nextX.clamp(-4, 4);
      _filteredY = nextY.clamp(-4, 4);
    });
  }

  void _resetSensor() {
    if (!mounted) return;
    setState(() {
      _filteredX = 0;
      _filteredY = 0;
    });
  }

  @override
  void dispose() {
    _subscription?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final ordered = [...widget.layers]..sort((a, b) => a.layerOrder.compareTo(b.layerOrder));
    return ClipRect(
      child: Stack(
        fit: StackFit.expand,
        children: [
          for (final layer in ordered)
            Transform.translate(
              offset: Offset(
                layer.positionX - _filteredX * layer.movementX * widget.sensitivity,
                layer.positionY + _filteredY * layer.movementY * widget.sensitivity,
              ),
              child: Transform.rotate(
                angle: layer.rotation * math.pi / 180,
                child: Transform.scale(
                  scale: layer.scale,
                  child: Image.network(
                    layer.imageUrl,
                    fit: BoxFit.cover,
                    errorBuilder: (_, __, ___) => const SizedBox.shrink(),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}
