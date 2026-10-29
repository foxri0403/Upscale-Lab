import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'live_2d_page.dart';
import 'services/live_2d_service.dart';
import 'services/api_client.dart';
import 'widgets/live_2d_preview.dart';

import 'models/image_editor.dart';
import 'models/image_ratio.dart';
import 'services/image_editor_service.dart';
import 'widgets/app_components.dart';
import 'widgets/device_display_builder.dart';
import 'widgets/image_ratio_selector.dart';

class ImageEditorResult {
  const ImageEditorResult(this.editedFile, this.ratio, {this.live2D});
  final EditedImageFile? editedFile;
  final ImageRatioSettings ratio;
  final Live2DDraft? live2D;
}

class ImageEditorPage extends StatefulWidget {
  const ImageEditorPage(
      {super.key,
      required this.filePath,
      this.apiClient,
      this.initialRatio = const ImageRatioSettings()});
  final String filePath;
  final ApiClient? apiClient;
  final ImageRatioSettings initialRatio;

  @override
  State<ImageEditorPage> createState() => _ImageEditorPageState();
}

class _ImageEditorPageState extends State<ImageEditorPage> {
  EditorImageSource? _source;
  CropGeometry? _geometry;
  String? _error;
  late ImageRatioSettings _ratio = widget.initialRatio;
  PhotoFilter _filter = PhotoFilter.original;
  bool _cropTab = true;
  bool _saving = false;
  Live2DDraft? _live2D;

  void _clearLive2D() {
    Live2DService.discard(_live2D);
    _live2D = null;
  }

  Future<void> _openLive2D() async {
    final geometry = _geometry;
    if (geometry == null || _saving) return;
    if (_live2D != null) {
      setState(() => _cropTab = false);
      return;
    }
    final draft = await Navigator.of(context).push<Live2DDraft>(
        MaterialPageRoute(
            builder: (_) => Live2DPage(
                sourcePath: widget.filePath,
                apiClient: widget.apiClient,
                crop: geometry.normalizedCrop,
                filter: _filter,
                keepSourceLayers: true)));
    if (!mounted) {
      await Live2DService.discard(draft);
      return;
    }
    if (draft != null) {
      setState(() {
        _clearLive2D();
        _live2D = draft;
        _cropTab = false;
      });
    }
  }

  int? _corner;
  int _pointers = 0;
  double _startScale = 1;
  double _startGestureScale = 1;
  Offset _startOffset = Offset.zero;
  Offset _startFocal = Offset.zero;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _error = null);
    try {
      final source = await ImageEditorService.loadPreview(widget.filePath);
      if (!mounted) {
        source.preview.dispose();
        return;
      }
      setState(() {
        _source = source;
        _geometry = CropGeometry(source.size);
      });
    } catch (_) {
      if (mounted) setState(() => _error = '이미지를 불러오지 못했습니다.');
    }
  }

  @override
  void dispose() {
    _clearLive2D();
    _source?.preview.dispose();
    super.dispose();
  }

  void _pending(String feature) {
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text('$feature 기능은 준비 중입니다.')));
  }

  Future<void> _finish() async {
    final geometry = _geometry;
    if (geometry == null || _saving) return;
    setState(() => _saving = true);
    EditedImageFile? edited;
    Live2DDraft? prepared;
    try {
      edited = await ImageEditorService.export(
          sourcePath: widget.filePath,
          crop: geometry.normalizedCrop,
          filter: _filter);
      if (_live2D case final draft?) {
        prepared = await Live2DService.export(draft,
            crop: geometry.normalizedCrop, filter: _filter);
      }
      if (!mounted) {
        await edited?.dispose();
        return;
      }
      setState(() => _saving = false);
      await WidgetsBinding.instance.endOfFrame;
      if (mounted && ModalRoute.of(context)?.isCurrent == true) {
        final original = _live2D;
        _live2D = null;
        if (original?.id != prepared?.id) Live2DService.discard(original);
        Navigator.pop(
            context,
            ImageEditorResult(
                edited,
                _ratio.preset == CropPreset.free && !_ratio.automatic
                    ? _ratio.copyWith(
                        freeAspectRatio: geometry.sourceRect.size.aspectRatio)
                    : _ratio,
                live2D: prepared));
        prepared = null;
      } else {
        await edited?.dispose();
      }
    } catch (_) {
      await edited?.dispose();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('이미지를 저장하지 못했습니다. 다시 시도해 주세요.')));
      }
    } finally {
      if (prepared != null && prepared.id != _live2D?.id) {
        await Live2DService.discard(prepared);
      }
      if (mounted) setState(() => _saving = false);
    }
  }

  void _start(ScaleStartDetails details) {
    final geometry = _geometry!;
    _corner = null;
    if (_cropTab && details.pointerCount == 1) {
      final corners = [
        geometry.frame.topLeft,
        geometry.frame.topRight,
        geometry.frame.bottomRight,
        geometry.frame.bottomLeft
      ];
      for (var i = 0; i < corners.length; i++) {
        if ((details.localFocalPoint - corners[i]).distance < 28) {
          _corner = i;
          break;
        }
      }
    }
    _pointers = details.pointerCount;
    _startScale = geometry.scale;
    _startOffset = geometry.offset;
    _startFocal = details.localFocalPoint;
    _startGestureScale = 1;
  }

  void _update(ScaleUpdateDetails details) {
    final geometry = _geometry!;
    if (_pointers != details.pointerCount) {
      _corner = null;
      _pointers = details.pointerCount;
      _startScale = geometry.scale;
      _startOffset = geometry.offset;
      _startFocal = details.localFocalPoint;
      _startGestureScale = details.scale;
    }
    setState(() {
      if (_corner case final corner?) {
        geometry.resizeCorner(corner, details.localFocalPoint);
      } else {
        final scale = (_startScale * details.scale / _startGestureScale)
            .clamp(geometry.minimumScale, geometry.minimumScale * 8);
        final offset = details.localFocalPoint -
            (_startFocal - _startOffset) * (scale / _startScale);
        geometry.transform(scale, offset);
      }
    });
  }

  @override
  Widget build(BuildContext context) => DeviceDisplayBuilder(builder: _build);

  Widget _build(BuildContext context, Size display) {
    final source = _source;
    final screen = MediaQuery.sizeOf(context);
    return PopScope(
      canPop: !_saving,
      child: Scaffold(
        appBar: AppBar(title: Text('이미지 수정'), actions: [
          IconButton(
              tooltip: '편집 초기화',
              onPressed: source == null || _saving
                  ? null
                  : () => setState(() {
                        _clearLive2D();
                        _ratio = widget.initialRatio;
                        _filter = PhotoFilter.original;
                        _geometry!.layout(
                            _geometry!.viewport,
                            _ratio.aspectRatio(source.size, display,
                                editing: true),
                            reset: true);
                      }),
              icon: const Icon(Icons.restart_alt)),
        ]),
        body: SafeArea(
          child: AppFormScrollView(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 20),
            child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const AppSectionTitle('변환 방식'),
                  const SizedBox(height: 10),
                  IntrinsicHeight(
                      child: Row(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                        Expanded(
                            child: _conversion('원본 이미지',
                                selected: _live2D == null,
                                action: () => setState(_clearLive2D))),
                        const SizedBox(width: 8),
                        Expanded(child: _conversion('업스케일', pending: true)),
                        const SizedBox(width: 8),
                        Expanded(
                            child: _conversion('Live 2D',
                                selected: _live2D != null,
                                action: _openLive2D)),
                      ])),
                  const SizedBox(height: 14),
                  SizedBox(
                    height: (screen.height * .4).clamp(240, 460),
                    child: source == null
                        ? Center(
                            child: _error == null
                                ? const CircularProgressIndicator()
                                : Column(
                                    mainAxisSize: MainAxisSize.min,
                                    children: [
                                        Text(_error!),
                                        TextButton(
                                            onPressed: _load,
                                            child: Text('다시 시도')),
                                      ]))
                        : _live2D != null && !_cropTab
                            ? Center(
                                child: AspectRatio(
                                    aspectRatio:
                                        _geometry!.sourceRect.size.aspectRatio,
                                    child: Live2DPreview(
                                        motionEnabled: _live2D!.motionEnabled,
                                        backgroundPath: _live2D!.backgroundPath,
                                        foregroundPath: _live2D!.foregroundPath,
                                        crop: _geometry!.normalizedCrop,
                                        filter: _filter)))
                            : LayoutBuilder(builder: (context, constraints) {
                                final geometry = _geometry!;
                                geometry.layout(
                                    constraints.biggest,
                                    _ratio.aspectRatio(source.size, display,
                                        editing: true));
                                return ClipRRect(
                                  borderRadius: BorderRadius.circular(16),
                                  child: Semantics(
                                    label:
                                        '이미지 편집 영역. 두 손가락으로 확대 및 축소, 드래그로 이동',
                                    child: GestureDetector(
                                      key:
                                          const ValueKey('image-editor-canvas'),
                                      behavior: HitTestBehavior.opaque,
                                      onScaleStart: _saving ? null : _start,
                                      onScaleUpdate: _saving ? null : _update,
                                      child: CustomPaint(
                                          painter: _CropPainter(
                                              source.preview,
                                              geometry.imageRect,
                                              geometry.frame,
                                              _filter,
                                              _cropTab),
                                          child: const SizedBox.expand()),
                                    ),
                                  ),
                                );
                              }),
                  ),
                  const SizedBox(height: 10),
                  Text('두 손가락으로 확대·축소',
                      textAlign: TextAlign.center,
                      style: TextStyle(
                          color: Theme.of(context).colorScheme.onSurfaceVariant,
                          fontSize: 12)),
                  const SizedBox(height: 12),
                  Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                    Flexible(child: _tool(false, Icons.tune, '필터')),
                    Container(
                        width: 1,
                        height: 24,
                        margin: const EdgeInsets.symmetric(horizontal: 12),
                        color: Theme.of(context).colorScheme.outlineVariant),
                    Flexible(child: _tool(true, Icons.crop, '자르기')),
                  ]),
                  const Divider(height: 24),
                  if (_cropTab) ...[
                    ImageRatioSelector(
                        value: _ratio,
                        display: display,
                        allowFree: true,
                        onChanged: source == null || _saving
                            ? null
                            : (ratio) => setState(() {
                                  _ratio = ratio;
                                })),
                    const SizedBox(height: 8),
                    Text('모서리를 드래그해 자르기 영역을 조절하세요.',
                        textAlign: TextAlign.center,
                        style: TextStyle(
                            color:
                                Theme.of(context).colorScheme.onSurfaceVariant,
                            fontSize: 12)),
                  ] else if (source != null) ...[
                    const AppSectionTitle('필터'),
                    const SizedBox(height: 10),
                    SingleChildScrollView(
                        scrollDirection: Axis.horizontal,
                        child: Row(children: [
                          for (final filter in PhotoFilter.values)
                            _filterItem(source.preview, filter),
                        ])),
                  ],
                  const SizedBox(height: 20),
                  FilledButton.icon(
                    onPressed: source == null || _saving ? null : _finish,
                    icon: _saving
                        ? const SizedBox.square(
                            dimension: 18,
                            child: CircularProgressIndicator(strokeWidth: 2))
                        : const Icon(Icons.check),
                    label: Text(_saving ? '등록 이미지 저장 중' : '완료하고 등록'),
                  ),
                ]),
          ),
        ),
      ),
    );
  }

  Widget _conversion(String label,
          {bool selected = false,
          bool pending = false,
          VoidCallback? action}) =>
      OutlinedButton(
        onPressed: _saving
            ? null
            : () {
                if (pending) _pending(label);
                action?.call();
              },
        style: OutlinedButton.styleFrom(
            padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 4),
            foregroundColor: selected
                ? Theme.of(context).colorScheme.primary
                : Theme.of(context).colorScheme.onSurface,
            backgroundColor: selected
                ? Theme.of(context).colorScheme.primaryContainer
                : Theme.of(context).colorScheme.surface,
            side: BorderSide(
                color: selected
                    ? Theme.of(context).colorScheme.primary
                    : Theme.of(context).colorScheme.outlineVariant)),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Text(label, textAlign: TextAlign.center),
          if (pending)
            Text('준비 중',
                style: TextStyle(
                    fontSize: 10,
                    color: Theme.of(context).colorScheme.onSurfaceVariant)),
        ]),
      );

  Widget _tool(bool crop, IconData icon, String label) => TextButton.icon(
      onPressed: _saving ? null : () => setState(() => _cropTab = crop),
      style: TextButton.styleFrom(
          foregroundColor: _cropTab == crop
              ? Theme.of(context).colorScheme.primary
              : Theme.of(context).colorScheme.onSurfaceVariant),
      icon: Icon(icon),
      label: Text(label));

  Widget _filterItem(ui.Image image, PhotoFilter filter) => Padding(
        padding: const EdgeInsets.only(right: 10),
        child: Semantics(
            selected: _filter == filter,
            button: true,
            label: '${filter.label} 필터',
            child: InkWell(
              key: ValueKey(filter),
              onTap: _saving
                  ? null
                  : () => setState(() {
                        _filter = filter;
                      }),
              borderRadius: BorderRadius.circular(12),
              child: Container(
                  width: 76,
                  padding: const EdgeInsets.all(4),
                  decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(12),
                      border: Border.all(
                          color: _filter == filter
                              ? Theme.of(context).colorScheme.primary
                              : Theme.of(context).colorScheme.outlineVariant,
                          width: 2)),
                  child: Column(children: [
                    ClipRRect(
                        borderRadius: BorderRadius.circular(8),
                        child: ColorFiltered(
                            colorFilter: ColorFilter.matrix(filter.matrix),
                            child: RawImage(
                                image: image,
                                width: 64,
                                height: 58,
                                fit: BoxFit.cover))),
                    const SizedBox(height: 6),
                    Text(filter.label,
                        textAlign: TextAlign.center,
                        style: TextStyle(
                            fontSize: 12,
                            color: _filter == filter
                                ? Theme.of(context).colorScheme.primary
                                : Theme.of(context).colorScheme.onSurface)),
                  ])),
            )),
      );
}

class _CropPainter extends CustomPainter {
  const _CropPainter(
      this.image, this.imageRect, this.frame, this.filter, this.guides);
  final ui.Image image;
  final Rect imageRect;
  final Rect frame;
  final PhotoFilter filter;
  final bool guides;

  @override
  void paint(Canvas canvas, Size size) {
    canvas.drawColor(const Color(0xFF172033), BlendMode.src);
    canvas.drawImageRect(
        image,
        Rect.fromLTWH(0, 0, image.width.toDouble(), image.height.toDouble()),
        imageRect,
        Paint()
          ..filterQuality = FilterQuality.medium
          ..colorFilter = ColorFilter.matrix(filter.matrix));
    final outside = Path()
      ..fillType = PathFillType.evenOdd
      ..addRect(Offset.zero & size)
      ..addRect(frame);
    canvas.drawPath(outside, Paint()..color = const Color(0x88000000));
    if (!guides) return;
    canvas.drawRect(
        frame,
        Paint()
          ..color = Colors.white
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.5);
    final guidePaint = Paint()
      ..color = const Color(0x88FFFFFF)
      ..strokeWidth = .7;
    for (var i = 1; i <= 2; i++) {
      canvas.drawLine(Offset(frame.left + frame.width * i / 3, frame.top),
          Offset(frame.left + frame.width * i / 3, frame.bottom), guidePaint);
      canvas.drawLine(Offset(frame.left, frame.top + frame.height * i / 3),
          Offset(frame.right, frame.top + frame.height * i / 3), guidePaint);
    }
    for (final corner in [
      frame.topLeft,
      frame.topRight,
      frame.bottomRight,
      frame.bottomLeft
    ]) {
      canvas.drawCircle(corner, 7, Paint()..color = Colors.white);
    }
  }

  @override
  bool shouldRepaint(_CropPainter oldDelegate) =>
      image != oldDelegate.image ||
      imageRect != oldDelegate.imageRect ||
      frame != oldDelegate.frame ||
      filter != oldDelegate.filter ||
      guides != oldDelegate.guides;
}
