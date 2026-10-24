import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'models/image_editor.dart';
import 'services/live_2d_service.dart';
import 'widgets/live_2d_preview.dart';

class Live2DPage extends StatefulWidget {
  const Live2DPage(
      {super.key,
      required this.sourcePath,
      this.crop = const Rect.fromLTWH(0, 0, 1, 1),
      this.filter = PhotoFilter.original,
      this.keepSourceLayers = false,
      this.initialDraft});
  final String sourcePath;
  final Rect crop;
  final PhotoFilter filter;
  final Live2DDraft? initialDraft;

  /// Editors retain the full layers so subsequent crops remain reversible.
  final bool keepSourceLayers;
  @override
  State<Live2DPage> createState() => _Live2DPageState();
}

class _Live2DPageState extends State<Live2DPage> {
  Live2DDraft? _draft;
  bool _processing = true;
  String? _error;
  String _layer = 'composite';
  @override
  void initState() {
    super.initState();
    _draft = widget.initialDraft;
    if (_draft == null) {
      _split();
    } else {
      _processing = false;
    }
  }

  Future<void> _split() async {
    setState(() {
      _processing = true;
      _error = null;
    });
    try {
      final draft = await Live2DService.split(widget.sourcePath);
      if (!mounted) {
        await Live2DService.discard(draft);
        return;
      }
      setState(() => _draft = draft);
    } catch (error) {
      if (mounted) {
        setState(() => _error = error is PlatformException
            ? error.message ?? '이미지를 분리하지 못했습니다.'
            : '이미지를 분리하지 못했습니다. 다시 시도해 주세요.');
      }
    } finally {
      if (mounted) setState(() => _processing = false);
    }
  }

  Future<void> _activate() async {
    final original = _draft;
    if (original == null || _processing) return;
    setState(() => _processing = true);
    Live2DDraft? output;
    Live2DDraft? applied;
    try {
      applied = await Live2DService.export(original,
          crop: widget.crop, filter: widget.filter);
      final confirmed = await Live2DService.activate(applied);
      if (!confirmed) return;
      output = widget.keepSourceLayers ? original : applied;
      if (!mounted) return;
      setState(() => _processing = false);
      await WidgetsBinding.instance.endOfFrame;
      if (!mounted) return;
      _draft = null;
      if (output.id != original.id) Live2DService.discard(original);
      Navigator.pop(context, output);
      if (applied.id == output.id) applied = null;
      output = null; // Ownership transfers to the caller.
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('Live 2D를 활성화하지 못했습니다. 다시 시도해 주세요.')));
      }
    } finally {
      if (output != null && output.id != _draft?.id) {
        await Live2DService.discard(output);
      }
      if (applied != null && applied.id != _draft?.id) {
        await Live2DService.discard(applied);
      }
      if (mounted) setState(() => _processing = false);
    }
  }

  @override
  void dispose() {
    Live2DService.discard(_draft);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final draft = _draft;
    return PopScope(
        canPop: !_processing,
        child: Scaffold(
            appBar: AppBar(title: const Text('Live 2D')),
            body: SafeArea(
                child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Expanded(
                              child: _processing
                                  ? const Center(
                                      child: Column(
                                          mainAxisSize: MainAxisSize.min,
                                          children: [
                                          CircularProgressIndicator(),
                                          SizedBox(height: 16),
                                          Text('배경과 인물·사물을 분리하고 있습니다…')
                                        ]))
                                  : draft == null
                                      ? Center(
                                          child: SingleChildScrollView(
                                              child: Column(
                                                  mainAxisSize:
                                                      MainAxisSize.min,
                                                  children: [
                                              Text(_error ?? '분리된 이미지가 없습니다.',
                                                  textAlign: TextAlign.center),
                                              const SizedBox(height: 12),
                                              FilledButton(
                                                  onPressed: _split,
                                                  child: const Text('다시 시도'))
                                            ])))
                                      : Center(
                                          child: AspectRatio(
                                              key: const ValueKey(
                                                  'live2d-preview-aspect'),
                                              aspectRatio: widget.crop.width *
                                                  draft.width /
                                                  (widget.crop.height *
                                                      draft.height),
                                              child: Live2DPreview(
                                                  backgroundPath:
                                                      draft.backgroundPath,
                                                  foregroundPath:
                                                      draft.foregroundPath,
                                                  crop: widget.crop,
                                                  filter: widget.filter,
                                                  layer: _layer)))),
                          const SizedBox(height: 12),
                          Flexible(
                              child: SingleChildScrollView(
                                  child: Column(
                                      crossAxisAlignment:
                                          CrossAxisAlignment.stretch,
                                      children: [
                                if (draft != null) ...[
                                  SegmentedButton<String>(
                                      segments: const [
                                        ButtonSegment(
                                            value: 'composite',
                                            label: Text('미리보기')),
                                        ButtonSegment(
                                            value: 'background',
                                            label: Text('배경')),
                                        ButtonSegment(
                                            value: 'foreground',
                                            label: Text('인물·사물')),
                                      ],
                                      selected: {
                                        _layer
                                      },
                                      onSelectionChanged: (value) =>
                                          setState(() => _layer = value.first)),
                                  const SizedBox(height: 12),
                                  const Text(
                                      '기기를 기울여 보세요. 배경은 고정되고 인물·사물만 움직입니다.\n가려진 배경은 주변 색으로 채워져 실제 배경과 다를 수 있습니다.',
                                      textAlign: TextAlign.center),
                                  const SizedBox(height: 12),
                                  FilledButton(
                                      key: const ValueKey('activate-live2d'),
                                      onPressed: _processing ? null : _activate,
                                      child: _processing
                                          ? const Row(
                                              mainAxisSize: MainAxisSize.min,
                                              children: [
                                                  SizedBox.square(
                                                      dimension: 16,
                                                      child:
                                                          CircularProgressIndicator(
                                                              strokeWidth: 2)),
                                                  SizedBox(width: 8),
                                                  Text('활성화 확인 중…')
                                                ])
                                          : const Text('배경화면에서 Live 2D 활성화')),
                                  const Text(
                                      '현재 이미지를 배경화면에 적용하고 편집에 사용합니다. 이미 활성화되어 있으면 바로 적용하며, 최초 활성화에는 Android 확인이 필요합니다.',
                                      textAlign: TextAlign.center),
                                ] else
                                  const Text(
                                      '최초 사용 시 Google Play 서비스의 분리 모델 다운로드가 필요합니다.\n자이로 센서가 없는 기기에서는 정지 이미지로 표시됩니다.',
                                      textAlign: TextAlign.center),
                              ]))),
                        ])))));
  }
}
