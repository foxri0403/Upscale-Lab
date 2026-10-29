import 'dart:async';
import 'dart:io';
import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';
import '../models/image_editor.dart';
import '../services/live_2d_service.dart';

/// Matches the wallpaper's normalized tilt, 8% overscan, and bounded movement.
class Live2DPreview extends StatefulWidget {
  const Live2DPreview(
      {super.key,
      required this.backgroundPath,
      required this.foregroundPath,
      this.layer = 'composite',
      this.motionEnabled = true,
      this.crop = const Rect.fromLTWH(0, 0, 1, 1),
      this.filter = PhotoFilter.original});
  final String backgroundPath;
  final String foregroundPath;
  final String layer;
  final bool motionEnabled;
  final Rect crop;
  final PhotoFilter filter;
  @override
  State<Live2DPreview> createState() => _Live2DPreviewState();
}

class _Live2DPreviewState extends State<Live2DPreview>
    with SingleTickerProviderStateMixin {
  StreamSubscription<dynamic>? _events;
  Offset _tilt = Offset.zero;
  Offset _target = Offset.zero;
  late final Ticker _ticker = createTicker(_frame);
  Duration _previous = Duration.zero;
  void _frame(Duration elapsed) {
    final seconds =
        ((elapsed - _previous).inMicroseconds / 1000000).clamp(0.0, .1);
    _previous = elapsed;
    final amount = 1 - math.exp(-seconds / .065);
    setState(() {
      _tilt += (_target - _tilt) * amount;
      if ((_target - _tilt).distance < .0001) {
        _tilt = _target;
        _ticker.stop();
        _previous = Duration.zero;
      }
    });
  }

  @override
  void didUpdateWidget(Live2DPreview oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.motionEnabled != widget.motionEnabled) _syncMotion();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _syncMotion();
  }

  void _syncMotion() {
    if (!widget.motionEnabled ||
        !(ModalRoute.of(context)?.isCurrent ?? true) ||
        !TickerMode.valuesOf(context).enabled) {
      _events?.cancel();
      _events = null;
      _tilt = Offset.zero;
      _target = Offset.zero;
      _ticker.stop();
      _previous = Duration.zero;
      return;
    }
    if (_events != null) return;
    _events = Live2DService.tilt.listen((event) {
      if (!mounted || event is! List || event.length != 2) return;
      final x = (event[0] as num).toDouble();
      final y = (event[1] as num).toDouble();
      if (x.isFinite && y.isFinite) {
        _target = Offset(x.clamp(-1, 1), y.clamp(-1, 1));
        if (!_ticker.isActive) {
          _previous = Duration.zero;
          _ticker.start();
        }
      }
    }, onError: (_) {
      if (mounted) setState(() => _tilt = Offset.zero);
    });
  }

  @override
  void dispose() {
    _events?.cancel();
    _ticker.dispose();
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
                  child: _image(widget.backgroundPath)),
            if (widget.layer != 'background')
              Transform.translate(
                  key: const ValueKey('live2d-moving-foreground'),
                  offset: widget.layer == 'composite'
                      ? Offset(-_tilt.dx * bounds.maxWidth * .035,
                          _tilt.dy * bounds.maxHeight * .035)
                      : Offset.zero,
                  child: Transform.scale(
                      scale: 1.08, child: _image(widget.foregroundPath))),
          ])));

  Widget _image(String path) {
    if (widget.crop == const Rect.fromLTWH(0, 0, 1, 1)) {
      return ColorFiltered(
          colorFilter: ColorFilter.matrix(widget.filter.matrix),
          child: Image.file(File(path), fit: BoxFit.cover));
    }
    return LayoutBuilder(builder: (_, bounds) {
      final crop = widget.crop;
      final width = bounds.maxWidth / crop.width;
      final height = bounds.maxHeight / crop.height;
      return ClipRect(
          child: Stack(children: [
        Positioned(
            left: -crop.left * width,
            top: -crop.top * height,
            width: width,
            height: height,
            child: ColorFiltered(
                colorFilter: ColorFilter.matrix(widget.filter.matrix),
                child: Image.file(File(path), fit: BoxFit.fill)))
      ]));
    });
  }
}
