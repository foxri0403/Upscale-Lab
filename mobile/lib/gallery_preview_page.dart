import 'dart:math' as math;

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'models/image_ratio.dart';
import 'services/image_editor_service.dart';
import 'services/wallpaper_service.dart';
import 'theme/app_theme.dart';
import 'widgets/device_display_builder.dart';
import 'widgets/image_ratio_selector.dart';

class WallpaperPreviewPage extends StatefulWidget {
  const WallpaperPreviewPage({
    super.key,
    required this.filePath,
    this.title,
    this.uploader,
    this.tags = const [],
    this.finishLabel,
    this.downloadCount = 0,
    this.downloadCountUpdate,
    this.ratio = const ImageRatioSettings(),
  });

  final String filePath;
  final String? title;
  final String? uploader;
  final List<String> tags;
  final String? finishLabel;
  final int downloadCount;
  final Future<int>? downloadCountUpdate;
  final ImageRatioSettings ratio;

  @override
  State<WallpaperPreviewPage> createState() => _WallpaperPreviewPageState();
}

class _WallpaperPreviewPageState extends State<WallpaperPreviewPage> {
  final _panelController = DraggableScrollableController();
  final _imageTransform = TransformationController();
  bool _applying = false;
  bool _applied = false;
  late ImageRatioSettings _ratio = widget.ratio;
  EditorImageSource? _source;
  String? _error;
  double _panelMinSize = .05;
  double _panelExpandedSize = .35;
  double _panelHeight = 1;
  int _imagePointers = 0;
  Offset? _imageDragStart;
  bool _imageGestureUsed = false;

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
      setState(() => _source = source);
    } catch (_) {
      if (mounted) setState(() => _error = '이미지를 불러오지 못했습니다.');
    }
  }

  @override
  void dispose() {
    _panelController.dispose();
    _imageTransform.dispose();
    _source?.preview.dispose();
    super.dispose();
  }

  Future<void> _chooseRatio() async {
    var selected = _ratio;
    final ratio = await showModalBottomSheet<ImageRatioSettings>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (sheetContext) => SingleChildScrollView(
        padding: const EdgeInsets.all(20),
        child: DeviceDisplayBuilder(
            builder: (context, display) => StatefulBuilder(
                  builder: (context, update) => Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      ImageRatioSelector(
                          value: selected,
                          display: display,
                          onChanged: (ratio) => update(() => selected = ratio)),
                      const SizedBox(height: 16),
                      FilledButton(
                          onPressed: () =>
                              Navigator.pop(sheetContext, selected),
                          child: const Text('설정 적용')),
                    ],
                  ),
                )),
      ),
    );
    if (ratio != null && mounted) {
      setState(() {
        _ratio = ratio;
        _applied = false;
      });
    }
  }

  Future<void> _apply() async {
    final target = await showModalBottomSheet<WallpaperTarget>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (sheetContext) => SafeArea(
          child: SingleChildScrollView(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          const ListTile(title: Text('배경화면 적용 위치 선택')),
          ListTile(
              title: const Text('홈 화면에 적용'),
              onTap: () => Navigator.pop(sheetContext, WallpaperTarget.home)),
          ListTile(
              title: const Text('잠금 화면에 적용'),
              onTap: () => Navigator.pop(sheetContext, WallpaperTarget.lock)),
          ListTile(
              title: const Text('전체 적용'),
              onTap: () => Navigator.pop(sheetContext, WallpaperTarget.both)),
        ]),
      )),
    );
    if (target == null || !mounted) return;
    setState(() => _applying = true);
    try {
      await const WallpaperService().applyLocal(
          sourcePath: widget.filePath, target: target, ratio: _ratio);
      if (!mounted) return;
      setState(() => _applied = true);
      ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('배경화면을 적용했습니다. 앱 종료와 재부팅 후에도 유지됩니다.')));
    } on PlatformException catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(error.message ?? '$error')));
      }
    } finally {
      if (mounted) setState(() => _applying = false);
    }
  }

  void _showPanel(bool show) {
    if (!_panelController.isAttached) return;
    _panelController.animateTo(show ? _panelExpandedSize : _panelMinSize,
        duration: const Duration(milliseconds: 240),
        curve: Curves.easeOutCubic);
  }

  void _togglePanel() {
    if (!_panelController.isAttached) return;
    _showPanel(_panelController.size <= _panelMinSize + .01);
  }

  void _dragHandle(DragUpdateDetails details) {
    if (!_panelController.isAttached) return;
    _panelController.jumpTo(
        (_panelController.size - details.delta.dy / _panelHeight)
            .clamp(_panelMinSize, .9));
  }

  void _settleHandle(DragEndDetails details) {
    if (!_panelController.isAttached) return;
    final velocity = details.primaryVelocity ?? 0;
    _showPanel(velocity < -200 ||
        (velocity <= 200 &&
            _panelController.size > (_panelMinSize + _panelExpandedSize) / 2));
  }

  void _imagePointerDown(PointerDownEvent event) {
    _imagePointers++;
    if (_imagePointers == 1) {
      _imageDragStart = event.position;
      _imageGestureUsed = false;
    } else {
      _imageGestureUsed = true;
    }
  }

  void _imagePointerMove(PointerMoveEvent event) {
    final start = _imageDragStart;
    if (_imagePointers != 1 ||
        start == null ||
        _imageGestureUsed ||
        _imageTransform.value.getMaxScaleOnAxis() > 1.01) {
      return;
    }
    final delta = event.position - start;
    if (delta.dy.abs() < 40 || delta.dy.abs() < delta.dx.abs() * 1.2) return;
    _imageGestureUsed = true;
    _showPanel(delta.dy < 0);
  }

  void _imagePointerEnd(PointerEvent event) {
    _imagePointers = math.max(0, _imagePointers - 1);
    if (_imagePointers == 0) _imageDragStart = null;
  }

  Widget _preview() {
    final source = _source;
    if (source == null) {
      return Center(
          child: _error == null
              ? const CircularProgressIndicator(color: Colors.white)
              : Column(mainAxisSize: MainAxisSize.min, children: [
                  Text(_error!, style: const TextStyle(color: Colors.white)),
                  TextButton(
                      onPressed: _load,
                      style:
                          TextButton.styleFrom(foregroundColor: Colors.white),
                      child: const Text('다시 시도')),
                ]));
    }
    return Listener(
      key: const ValueKey('wallpaper-image-gestures'),
      behavior: HitTestBehavior.opaque,
      onPointerDown: _imagePointerDown,
      onPointerMove: _imagePointerMove,
      onPointerUp: _imagePointerEnd,
      onPointerCancel: _imagePointerEnd,
      onPointerSignal: (event) {
        if (event is PointerScrollEvent &&
            _imageTransform.value.getMaxScaleOnAxis() <= 1.01 &&
            event.scrollDelta.dy != 0) {
          _showPanel(event.scrollDelta.dy < 0);
        }
      },
      child: InteractiveViewer(
        transformationController: _imageTransform,
        maxScale: 8,
        child: Center(
            child: AspectRatio(
          aspectRatio: source.size.aspectRatio,
          child: RawImage(
              key: const ValueKey('wallpaper-preview-image'),
              image: source.preview,
              fit: BoxFit.contain),
        )),
      ),
    );
  }

  Widget _panelControls() => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: [
          if (widget.uploader != null) ...[
            Text('업로드자 · ${widget.uploader}',
                style: const TextStyle(
                    color: Colors.white, fontWeight: FontWeight.w600)),
            const SizedBox(height: 8),
          ],
          if (widget.tags.isNotEmpty) ...[
            Text(widget.tags.map((tag) => '#$tag').join('  '),
                style: const TextStyle(color: Colors.white70, fontSize: 13)),
            const SizedBox(height: 12),
          ],
          Row(children: [
            Expanded(
                child: OutlinedButton.icon(
                    key: const ValueKey('wallpaper-ratio-settings'),
                    style: OutlinedButton.styleFrom(
                        foregroundColor: Colors.white,
                        backgroundColor: Colors.white.withValues(alpha: .08),
                        side: const BorderSide(color: Colors.white38)),
                    onPressed:
                        _applying || _source == null ? null : _chooseRatio,
                    icon: const Icon(Icons.aspect_ratio),
                    label: Text(_ratio.automatic ? '비율 자동' : '비율 수동'))),
            const SizedBox(width: 8),
            Expanded(
                child: FilledButton(
                    key: const ValueKey('wallpaper-apply'),
                    style: FilledButton.styleFrom(
                        foregroundColor: Colors.white,
                        backgroundColor: Colors.white.withValues(alpha: .18)),
                    onPressed: _applying || _source == null ? null : _apply,
                    child: _applying
                        ? const SizedBox.square(
                            dimension: 18,
                            child: CircularProgressIndicator(
                                strokeWidth: 2, color: Colors.white))
                        : Text(_applied ? '적용됨' : '배경화면 적용'))),
          ]),
          if (widget.finishLabel != null) ...[
            const SizedBox(height: 8),
            TextButton(
                style: TextButton.styleFrom(
                    foregroundColor: Colors.white,
                    backgroundColor: Colors.white.withValues(alpha: .08)),
                onPressed: _applying || _source == null
                    ? null
                    : () => Navigator.pop(context, _ratio),
                child: Text(widget.finishLabel!)),
          ],
        ],
      );

  Widget _panel() => SafeArea(
      top: false,
      child: LayoutBuilder(
        builder: (context, bounds) {
          if (bounds.maxHeight <= 0) return const SizedBox.shrink();
          _panelHeight = bounds.maxHeight;
          final handleHeight = math.min(36.0, bounds.maxHeight * .3);
          _panelMinSize = handleHeight / bounds.maxHeight;
          final scale =
              (MediaQuery.textScalerOf(context).scale(14) / 14).clamp(1.0, 1.5);
          final desiredHeight =
              (widget.uploader != null || widget.tags.isNotEmpty
                      ? 260.0
                      : 170.0) *
                  scale;
          _panelExpandedSize =
              (desiredHeight / bounds.maxHeight).clamp(_panelMinSize, .9);
          return DraggableScrollableSheet(
            controller: _panelController,
            minChildSize: _panelMinSize,
            initialChildSize: _panelExpandedSize,
            maxChildSize: .9,
            snap: true,
            snapSizes: [
              if (_panelExpandedSize > _panelMinSize && _panelExpandedSize < .9)
                _panelExpandedSize
            ],
            shouldCloseOnMinExtent: false,
            builder: (context, scrollController) => Material(
              key: const ValueKey('wallpaper-details-panel'),
              color: Colors.black.withValues(alpha: .62),
              borderRadius:
                  const BorderRadius.vertical(top: Radius.circular(20)),
              clipBehavior: Clip.antiAlias,
              child: Column(children: [
                Semantics(
                    button: true,
                    label: '이미지 정보 표시/숨기기',
                    child: GestureDetector(
                      key: const ValueKey('wallpaper-panel-handle'),
                      behavior: HitTestBehavior.opaque,
                      onTap: _togglePanel,
                      onVerticalDragUpdate: _dragHandle,
                      onVerticalDragEnd: _settleHandle,
                      child: SizedBox(
                          height: handleHeight,
                          width: double.infinity,
                          child: Center(
                              child: Container(
                                  width: 84,
                                  height: 4,
                                  decoration: BoxDecoration(
                                      color: Colors.white70,
                                      borderRadius:
                                          BorderRadius.circular(4))))),
                    )),
                Expanded(
                    child: ListView(
                  controller: scrollController,
                  padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
                  children: [_panelControls()],
                )),
              ]),
            ),
          );
        },
      ));

  @override
  Widget build(BuildContext context) => AnnotatedRegion<SystemUiOverlayStyle>(
        value: AppTheme.overlayFor(Brightness.dark),
        child: PopScope(
            canPop: !_applying,
            child: Scaffold(
              backgroundColor: Colors.black,
              body: Stack(fit: StackFit.expand, children: [
                SafeArea(child: _preview()),
                SafeArea(
                    child: Align(
                        alignment: Alignment.topCenter,
                        child: Material(
                          color: Colors.black.withValues(alpha: .3),
                          child: Row(children: [
                            IconButton(
                                tooltip: '돌아가기',
                                onPressed: _applying
                                    ? null
                                    : () => Navigator.maybePop(context),
                                color: Colors.white,
                                icon: const Icon(Icons.close)),
                            Expanded(
                                child: Text(widget.title ?? '배경화면 미리보기',
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: const TextStyle(
                                        color: Colors.white,
                                        fontWeight: FontWeight.w600))),
                            Padding(
                                padding:
                                    const EdgeInsets.symmetric(horizontal: 16),
                                child: FutureBuilder<int>(
                                  future: widget.downloadCountUpdate,
                                  initialData: widget.downloadCount,
                                  builder: (context, snapshot) => Tooltip(
                                    message: '다운로드 ${snapshot.data ?? 0}회',
                                    child: Row(
                                        mainAxisSize: MainAxisSize.min,
                                        children: [
                                          const Icon(Icons.download_outlined,
                                              size: 18, color: Colors.white),
                                          const SizedBox(width: 4),
                                          Text('${snapshot.data ?? 0}',
                                              style: const TextStyle(
                                                  color: Colors.white)),
                                        ]),
                                  ),
                                )),
                          ]),
                        ))),
                _panel(),
              ]),
            )),
      );
}
