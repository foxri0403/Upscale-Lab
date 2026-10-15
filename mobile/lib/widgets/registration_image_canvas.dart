import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import '../models/image_editor.dart';
import '../services/image_editor_service.dart';

/// Viewing a width-fitted image never changes its saved crop. Only gestures in
/// the crop tool change source coordinates, shared by preview and PNG export.
class RegistrationImageCanvas extends StatefulWidget {
  const RegistrationImageCanvas({
    super.key,
    required this.source,
    required this.crop,
    required this.filter,
    required this.cropping,
    required this.aspectRatio,
    required this.onCropChanged,
    this.enabled = true,
  });

  final EditorImageSource source;
  final Rect crop;
  final PhotoFilter filter;
  final bool cropping;
  final double? aspectRatio;
  final ValueChanged<Rect> onCropChanged;
  final bool enabled;

  @override
  State<RegistrationImageCanvas> createState() =>
      _RegistrationImageCanvasState();
}

class _RegistrationImageCanvasState extends State<RegistrationImageCanvas> {
  late final _geometry = CropGeometry(widget.source.size);
  final _viewTransform = TransformationController();
  int? _corner;
  int _pointers = 0;
  double _startScale = 1;
  double _gestureScale = 1;
  Offset _startOffset = Offset.zero;
  Offset _startFocal = Offset.zero;

  @override
  void didUpdateWidget(RegistrationImageCanvas oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.crop != widget.crop ||
        oldWidget.cropping != widget.cropping) {
      _viewTransform.value = Matrix4.identity();
    }
  }

  @override
  void dispose() {
    _viewTransform.dispose();
    super.dispose();
  }

  void _start(ScaleStartDetails details) {
    _corner = null;
    if (details.pointerCount == 1) {
      final corners = [
        _geometry.frame.topLeft,
        _geometry.frame.topRight,
        _geometry.frame.bottomRight,
        _geometry.frame.bottomLeft
      ];
      for (var i = 0; i < corners.length; i++) {
        if ((details.localFocalPoint - corners[i]).distance < 28) {
          _corner = i;
          break;
        }
      }
    }
    _pointers = details.pointerCount;
    _startScale = _geometry.scale;
    _startOffset = _geometry.offset;
    _startFocal = details.localFocalPoint;
    _gestureScale = 1;
  }

  void _update(ScaleUpdateDetails details) {
    if (_pointers != details.pointerCount) {
      _corner = null;
      _pointers = details.pointerCount;
      _startScale = _geometry.scale;
      _startOffset = _geometry.offset;
      _startFocal = details.localFocalPoint;
      _gestureScale = details.scale;
    }
    setState(() {
      if (_corner case final corner?) {
        _geometry.resizeCorner(corner, details.localFocalPoint);
      } else {
        final scale = (_startScale * details.scale / _gestureScale)
            .clamp(_geometry.minimumScale, _geometry.minimumScale * 8);
        _geometry.transform(
            scale,
            details.localFocalPoint -
                (_startFocal - _startOffset) * (scale / _startScale));
      }
    });
    widget.onCropChanged(_geometry.normalizedCrop);
  }

  @override
  Widget build(BuildContext context) =>
      LayoutBuilder(builder: (context, bounds) {
        final width = bounds.maxWidth;
        if (!widget.cropping) {
          final source = widget.source.size;
          final height = width *
              (widget.crop.height * source.height) /
              (widget.crop.width * source.width);
          final viewportHeight = math.min(
              height,
              (MediaQuery.sizeOf(context).height * .46)
                  .clamp(180, 480)
                  .toDouble());
          return ClipRRect(
            borderRadius: BorderRadius.circular(16),
            child: SizedBox(
              key: const ValueKey('registration-original-image'),
              height: viewportHeight,
              child: Semantics(
                label: '이미지 미리보기. 드래그로 이동, 두 손가락으로 확대 및 축소',
                child: InteractiveViewer(
                  key: const ValueKey('registration-image-viewer'),
                  transformationController: _viewTransform,
                  constrained: false,
                  alignment: Alignment.topLeft,
                  panEnabled: widget.enabled,
                  scaleEnabled: widget.enabled,
                  child: CustomPaint(
                    painter: _RegistrationPreviewPainter(
                        widget.source.preview, widget.crop, widget.filter),
                    child: SizedBox(width: width, height: height),
                  ),
                ),
              ),
            ),
          );
        }

        final aspect = widget.aspectRatio ??
            (_geometry.viewport.isEmpty
                ? widget.source.size.aspectRatio
                : _geometry.sourceRect.size.aspectRatio);
        final height = math.max(120.0, (width - 24) / aspect + 24);
        final size = Size(width, height);
        final previous = _geometry.normalizedCrop;
        _geometry.layout(size, widget.aspectRatio, padding: 12);
        if (previous != _geometry.normalizedCrop) {
          final crop = _geometry.normalizedCrop;
          WidgetsBinding.instance.addPostFrameCallback((_) {
            if (mounted && widget.cropping) widget.onCropChanged(crop);
          });
        }
        return ClipRRect(
          borderRadius: BorderRadius.circular(16),
          child: GestureDetector(
            key: const ValueKey('registration-crop-canvas'),
            behavior: HitTestBehavior.opaque,
            onScaleStart: widget.enabled ? _start : null,
            onScaleUpdate: widget.enabled ? _update : null,
            child: CustomPaint(
              painter: _RegistrationCropPainter(
                  widget.source.preview,
                  _geometry.imageRect,
                  _geometry.frame,
                  widget.filter,
                  Theme.of(context).colorScheme.surfaceContainerHighest),
              child: SizedBox(width: width, height: height),
            ),
          ),
        );
      });
}

class _RegistrationPreviewPainter extends CustomPainter {
  const _RegistrationPreviewPainter(this.image, this.crop, this.filter);
  final ui.Image image;
  final Rect crop;
  final PhotoFilter filter;

  @override
  void paint(Canvas canvas, Size size) => canvas.drawImageRect(
      image,
      Rect.fromLTRB(crop.left * image.width, crop.top * image.height,
          crop.right * image.width, crop.bottom * image.height),
      Offset.zero & size,
      Paint()
        ..filterQuality = FilterQuality.medium
        ..colorFilter = ColorFilter.matrix(filter.matrix));

  @override
  bool shouldRepaint(_RegistrationPreviewPainter oldDelegate) =>
      image != oldDelegate.image ||
      crop != oldDelegate.crop ||
      filter != oldDelegate.filter;
}

class _RegistrationCropPainter extends CustomPainter {
  const _RegistrationCropPainter(
      this.image, this.imageRect, this.frame, this.filter, this.background);
  final ui.Image image;
  final Rect imageRect;
  final Rect frame;
  final PhotoFilter filter;
  final Color background;

  @override
  void paint(Canvas canvas, Size size) {
    canvas.drawColor(background, BlendMode.src);
    canvas.drawImageRect(
        image,
        Rect.fromLTWH(0, 0, image.width.toDouble(), image.height.toDouble()),
        imageRect,
        Paint()
          ..filterQuality = FilterQuality.medium
          ..colorFilter = ColorFilter.matrix(filter.matrix));
    canvas.drawPath(
        Path()
          ..fillType = PathFillType.evenOdd
          ..addRect(Offset.zero & size)
          ..addRect(frame),
        Paint()..color = const Color(0x88000000));
    canvas.drawRect(
        frame,
        Paint()
          ..color = Colors.white
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.5);
    final guide = Paint()
      ..color = const Color(0x88FFFFFF)
      ..strokeWidth = .7;
    for (var i = 1; i < 3; i++) {
      canvas.drawLine(Offset(frame.left + frame.width * i / 3, frame.top),
          Offset(frame.left + frame.width * i / 3, frame.bottom), guide);
      canvas.drawLine(Offset(frame.left, frame.top + frame.height * i / 3),
          Offset(frame.right, frame.top + frame.height * i / 3), guide);
    }
    for (final point in [
      frame.topLeft,
      frame.topRight,
      frame.bottomRight,
      frame.bottomLeft
    ]) {
      canvas.drawCircle(point, 7, Paint()..color = Colors.white);
    }
  }

  @override
  bool shouldRepaint(_RegistrationCropPainter oldDelegate) =>
      image != oldDelegate.image ||
      imageRect != oldDelegate.imageRect ||
      frame != oldDelegate.frame ||
      filter != oldDelegate.filter ||
      background != oldDelegate.background;
}
