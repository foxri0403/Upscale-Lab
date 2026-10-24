import 'dart:async';
import 'dart:io';
import 'package:flutter/material.dart';
import '../services/live_2d_service.dart';

/// Matches the wallpaper's normalized tilt, 8% overscan, and bounded movement.
class Live2DPreview extends StatefulWidget {
  const Live2DPreview(
      {super.key,
      required this.backgroundPath,
      required this.foregroundPath,
      this.layer = 'composite'});
  final String backgroundPath;
  final String foregroundPath;
  final String layer;
  @override
  State<Live2DPreview> createState() => _Live2DPreviewState();
}

class _Live2DPreviewState extends State<Live2DPreview> {
  StreamSubscription<dynamic>? _events;
  Offset _tilt = Offset.zero;
  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!(ModalRoute.of(context)?.isCurrent ?? true)) {
      _events?.cancel();
      _events = null;
      _tilt = Offset.zero;
      return;
    }
    if (_events != null) return;
    _events = Live2DService.tilt.listen((event) {
      if (!mounted || event is! List || event.length != 2) return;
      final x = (event[0] as num).toDouble();
      final y = (event[1] as num).toDouble();
      if (x.isFinite && y.isFinite) {
        setState(() => _tilt = Offset(x.clamp(-1, 1), y.clamp(-1, 1)));
      }
    }, onError: (_) {
      if (mounted) setState(() => _tilt = Offset.zero);
    });
  }

  @override
  void dispose() {
    _events?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => LayoutBuilder(
      builder: (_, bounds) => ClipRect(
              child: Stack(fit: StackFit.expand, children: [
            const ColoredBox(color: Color(0xFF30343B)),
            if (widget.layer != 'foreground')
              Transform.scale(
                  key: const ValueKey('live2d-fixed-background'),
                  scale: 1.08,
                  child: Image.file(File(widget.backgroundPath),
                      fit: BoxFit.cover)),
            if (widget.layer != 'background')
              Transform.translate(
                  key: const ValueKey('live2d-moving-foreground'),
                  offset: widget.layer == 'composite'
                      ? Offset(-_tilt.dx * bounds.maxWidth * .035,
                          _tilt.dy * bounds.maxHeight * .035)
                      : Offset.zero,
                  child: Transform.scale(
                      scale: 1.08,
                      child: Image.file(File(widget.foregroundPath),
                          fit: BoxFit.cover))),
          ])));
}
