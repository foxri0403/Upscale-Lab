import 'package:flutter/material.dart';
import 'package:flutter/services.dart' show PlatformException;
import 'package:flutter/rendering.dart' show OverflowBoxFit;
import 'services/live_2d_service.dart';
import 'widgets/live_2d_preview.dart';

import 'models/gallery_tags.dart';
import 'models/image_editor.dart';
import 'models/image_ratio.dart';
import 'services/image_editor_service.dart';
import 'services/wallpaper_service.dart';
import 'widgets/app_components.dart';
import 'widgets/device_display_builder.dart';
import 'widgets/gallery_tag_picker.dart';
import 'widgets/image_ratio_selector.dart';
import 'widgets/registration_image_canvas.dart';
import 'widgets/wallpaper_apply_dialog.dart';

class ImageRegistrationOptions {
  const ImageRegistrationOptions({
    required this.title,
    required this.tags,
    this.editedFile,
    this.ratio = const ImageRatioSettings(),
    this.upscaleFactor,
    this.wallpaperTarget,
    this.live2D,
  });
  final String title;
  final List<String> tags;
  final EditedImageFile? editedFile;
  final int? upscaleFactor;
  final ImageRatioSettings ratio;
  final WallpaperTarget? wallpaperTarget;
  final Live2DDraft? live2D;
}

enum _RegistrationTool {
  information('사진 정보', Icons.info_outline),
  crop('자르기', Icons.crop),
  filter('필터', Icons.filter_vintage_outlined),
  upscale('업스케일', Icons.open_in_full),
  parallax('Live 2D', Icons.view_in_ar_outlined);

  const _RegistrationTool(this.label, this.icon);
  final String label;
  final IconData icon;
}

class ImageFeaturePage extends StatefulWidget {
  const ImageFeaturePage(
      {super.key,
      required this.filePath,
      required this.title,
      this.initialTags = const [],
      this.initialRatio = const ImageRatioSettings(),
      this.loadLive2D,
      this.onSave});
  final String filePath;
  final String title;
  final List<String> initialTags;
  final ImageRatioSettings initialRatio;
  final Future<Live2DDraft?> Function()? loadLive2D;
  final Future<void> Function(ImageRegistrationOptions)? onSave;

  @override
  State<ImageFeaturePage> createState() => _ImageFeaturePageState();
}

class _ImageFeaturePageState extends State<ImageFeaturePage> {
  late final _titleController = TextEditingController(text: widget.title);
  final _titleFocus = FocusNode();
  final _toolKeys = {
    for (final tool in _RegistrationTool.values) tool: GlobalKey()
  };
  late final Set<String> _tags =
      widget.initialTags.where(selectableGalleryTags.contains).toSet();
  EditorImageSource? _source;
  String? _error;
  String? _titleError;
  bool _saving = false;
  bool _exporting = false;
  _RegistrationTool _tool = _RegistrationTool.information;
  int _upscaleFactor = 2;
  late ImageRatioSettings _ratio = widget.initialRatio;
  PhotoFilter _filter = PhotoFilter.original;
  Rect _crop = const Rect.fromLTWH(0, 0, 1, 1);
  int _canvasRevision = 0;
  Live2DDraft? _live2D;
  Live2DDraft? _candidate;
  bool _splitting = false;
  String? _splitError;
  String _liveLayer = 'composite';
  int _splitGeneration = 0;

  void _clearLive2D() {
    _splitGeneration++;
    final draft = _live2D;
    final candidate = _candidate;
    _live2D = null;
    _candidate = null;
    _splitting = false;
    _splitError = null;
    Live2DService.discard(draft);
    if (candidate?.id != draft?.id) Live2DService.discard(candidate);
  }

  Future<void> _openLive2D({bool retry = false}) async {
    if (_source == null || _saving || _splitting) return;
    setState(() => _tool = _RegistrationTool.parallax);
    if (!retry && (_live2D != null || _candidate != null)) return;
    final generation = ++_splitGeneration;
    setState(() {
      _splitting = true;
      _splitError = null;
    });
    try {
      final draft = await Live2DService.split(widget.filePath);
      if (!mounted || generation != _splitGeneration) {
        await Live2DService.discard(draft);
        return;
      }
      final previous = _candidate;
      setState(() => _candidate = draft);
      await Live2DService.discard(previous);
    } catch (error) {
      if (mounted && generation == _splitGeneration) {
        setState(() => _splitError = error is PlatformException
            ? error.message ?? '이미지를 분리하지 못했습니다.'
            : '이미지를 분리하지 못했습니다. 다시 시도해 주세요.');
      }
    } finally {
      if (mounted && generation == _splitGeneration) {
        setState(() => _splitting = false);
      }
    }
  }

  void _applyLive2D() {
    final draft = _candidate ?? _live2D;
    if (draft == null || _saving || _splitting) return;
    final previous = _live2D;
    setState(() {
      _live2D = draft;
      _candidate = null;
      _liveLayer = 'composite';
    });
    if (previous?.id != draft.id) Live2DService.discard(previous);
  }

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _error = null);
    EditorImageSource? source;
    Live2DDraft? live;
    try {
      source = await ImageEditorService.loadPreview(widget.filePath);
      live = await widget.loadLive2D?.call();
      if (!mounted) {
        source.preview.dispose();
        await Live2DService.discard(live);
        return;
      }
      setState(() {
        _source = source;
        _live2D = live;
      });
    } catch (_) {
      source?.preview.dispose();
      await Live2DService.discard(live);
      if (mounted) setState(() => _error = '이미지를 불러오지 못했습니다.');
    }
  }

  @override
  void dispose() {
    _clearLive2D();
    _source?.preview.dispose();
    _titleFocus.dispose();
    _titleController.dispose();
    super.dispose();
  }

  void _selectTool(_RegistrationTool tool) {
    FocusScope.of(context).unfocus();
    if (tool == _RegistrationTool.parallax) {
      _openLive2D();
      return;
    }
    if (tool == _RegistrationTool.upscale) {
      _clearLive2D();
    }
    setState(() => _tool = tool);
    final toolContext = _toolKeys[tool]!.currentContext;
    if (toolContext != null) {
      Scrollable.ensureVisible(toolContext,
          alignment: .5, duration: const Duration(milliseconds: 180));
    }
  }

  Future<void> _openTagPicker() async {
    FocusScope.of(context).unfocus();
    final selected = await showGalleryTagPicker(context, selectedTags: _tags);
    if (selected == null || !mounted) return;
    setState(
      () => _tags
        ..clear()
        ..addAll(selected),
    );
  }

  void _openPreview() {
    final source = _source;
    if (source == null || _saving) return;
    if (_live2D != null) return;
    FocusScope.of(context).unfocus();
    Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => RegistrationPhotoPreviewPage(
          source: source,
          crop: _crop,
          filter: _filter,
        ),
      ),
    );
  }

  Future<void> _save({bool wallpaper = false}) async {
    if (_source == null || _saving || _exporting) return;
    final title = _titleController.text.trim();
    if (title.isEmpty) {
      _selectTool(_RegistrationTool.information);
      setState(() => _titleError = '이미지 이름을 입력해 주세요.');
      _titleFocus.requestFocus();
      return;
    }
    FocusScope.of(context).unfocus();
    setState(() => _saving = true);
    EditedImageFile? edited;
    Live2DDraft? prepared;
    try {
      final target = wallpaper
          ? await showDialog<WallpaperTarget>(
              context: context, builder: (_) => const WallpaperApplyDialog())
          : null;
      if ((wallpaper && target == null) || !mounted) return;
      setState(() => _exporting = true);
      edited = await ImageEditorService.export(
          sourcePath: widget.filePath, crop: _crop, filter: _filter);
      if (_live2D case final draft?) {
        prepared =
            await Live2DService.export(draft, crop: _crop, filter: _filter);
      }
      final options = ImageRegistrationOptions(
          title: title,
          tags: registrationTags(_tags),
          editedFile: edited,
          upscaleFactor:
              _tool == _RegistrationTool.upscale ? _upscaleFactor : null,
          ratio: !_ratio.automatic && _ratio.preset == CropPreset.free
              ? _ratio.copyWith(
                  freeAspectRatio: _crop.width *
                      _source!.size.width /
                      (_crop.height * _source!.size.height))
              : _ratio,
          wallpaperTarget: target,
          live2D: prepared);
      await widget.onSave?.call(options);
      if (!mounted) {
        await edited?.dispose();
        return;
      }
      // Let PopScope observe the completed save before returning its result.
      setState(() => _saving = false);
      await WidgetsBinding.instance.endOfFrame;
      if (!mounted || ModalRoute.of(context)?.isCurrent != true) {
        await edited?.dispose();
        return;
      }
      if (widget.onSave != null) {
        await edited?.dispose();
        if (!mounted) return;
        Navigator.pop(context);
        return;
      }
      final original = _live2D;
      _live2D = null;
      if (original?.id != prepared?.id) Live2DService.discard(original);
      Navigator.pop(context, options);
      prepared = null;
    } catch (error) {
      await edited?.dispose();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(
            content: Text(error is PlatformException
                ? error.message ?? '이미지를 저장하지 못했습니다.'
                : '이미지를 저장하지 못했습니다. 다시 시도해 주세요.')));
      }
    } finally {
      if (prepared != null && prepared.id != _live2D?.id) {
        await Live2DService.discard(prepared);
      }
      if (mounted) {
        setState(() {
          _saving = false;
          _exporting = false;
        });
      }
    }
  }

  void _reset() => setState(() {
        _clearLive2D();
        _crop = const Rect.fromLTWH(0, 0, 1, 1);
        _ratio = widget.initialRatio;
        _filter = PhotoFilter.original;
        _canvasRevision++;
      });

  @override
  Widget build(BuildContext context) => DeviceDisplayBuilder(builder: _build);

  Widget _build(BuildContext context, Size display) {
    final source = _source;
    final colors = Theme.of(context).colorScheme;
    final showTools = MediaQuery.viewInsetsOf(context).bottom == 0;
    final wide = MediaQuery.sizeOf(context).aspectRatio > 1;
    return PopScope(
      canPop: !_saving,
      child: Scaffold(
        appBar: AppBar(
            title: Text(widget.onSave == null ? '새 이미지 등록' : '이미지 수정'),
            actions: [
              IconButton(
                  tooltip: '편집 초기화',
                  onPressed: source == null || _saving ? null : _reset,
                  icon: const Icon(Icons.restart_alt)),
            ]),
        body: SafeArea(
            child: Column(children: [
          Expanded(
              child: LayoutBuilder(
                  builder: (_, bounds) => Column(
                        key: const ValueKey('registration-content'),
                        children: [
                          Expanded(child: _preview(display)),
                          ConstrainedBox(
                            constraints:
                                BoxConstraints(maxHeight: bounds.maxHeight),
                            child: ClipRect(
                                child: OverflowBox(
                              fit: OverflowBoxFit.deferToChild,
                              alignment: Alignment.bottomCenter,
                              maxHeight: double.infinity,
                              child: Padding(
                                key: const ValueKey('registration-settings'),
                                padding:
                                    const EdgeInsets.fromLTRB(16, 8, 16, 8),
                                child: AppFormBody(
                                    child: Material(
                                  type: MaterialType.transparency,
                                  child: switch (_tool) {
                                    _RegistrationTool.information =>
                                      _information(),
                                    _RegistrationTool.crop =>
                                      ImageRatioSelector(
                                          value: _ratio,
                                          display: display,
                                          allowFree: true,
                                          automaticOnly: true,
                                          onChanged: source == null || _saving
                                              ? null
                                              : (ratio) => setState(() {
                                                    _ratio = ratio;
                                                  })),
                                    _RegistrationTool.filter => _filters(),
                                    _RegistrationTool.upscale => Row(
                                        mainAxisAlignment:
                                            MainAxisAlignment.center,
                                        children: [
                                          const Text('업스케일 배율'),
                                          const SizedBox(width: 16),
                                          SegmentedButton<int>(
                                            segments: const [
                                              ButtonSegment(
                                                  value: 2, label: Text('2배')),
                                              ButtonSegment(
                                                  value: 4, label: Text('4배')),
                                            ],
                                            selected: {_upscaleFactor},
                                            onSelectionChanged: _saving
                                                ? null
                                                : (values) {
                                                    setState(() {
                                                      _upscaleFactor =
                                                          values.first;
                                                    });
                                                  },
                                          ),
                                        ],
                                      ),
                                    _RegistrationTool.parallax =>
                                      _live2DControls(),
                                  },
                                )),
                              ),
                            )),
                          ),
                        ],
                      ))),
          if (wide && showTools)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Row(children: [
                Expanded(child: _tools(colors)),
                Padding(
                    padding: const EdgeInsets.only(right: 16),
                    child: SizedBox(width: 224, child: _saveActions())),
              ]),
            )
          else ...[
            if (showTools) _tools(colors),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
              child: _saveActions(),
            ),
          ],
        ])),
      ),
    );
  }

  Widget _preview(Size display) => Padding(
        padding: const EdgeInsets.fromLTRB(8, 4, 8, 0),
        child: LayoutBuilder(builder: (_, bounds) {
          final source = _source;
          if (source == null) {
            return Center(
                child: _error == null
                    ? const CircularProgressIndicator()
                    : Column(mainAxisSize: MainAxisSize.min, children: [
                        Text(_error!),
                        TextButton(
                            onPressed: _load, child: const Text('다시 시도')),
                      ]));
          }
          final draft = _tool == _RegistrationTool.parallax
              ? _candidate ?? _live2D
              : _live2D;
          return Center(
              child: Semantics(
            button: true,
            label: '사진 전체 보기',
            child: GestureDetector(
              key: const ValueKey('registration-preview-button'),
              onTap: _saving ? null : _openPreview,
              behavior: HitTestBehavior.opaque,
              child: RegistrationImageCanvas(
                key: ValueKey(_canvasRevision),
                source: source,
                crop: _crop,
                filter: _filter,
                preview: draft == null
                    ? null
                    : Live2DPreview(
                        backgroundPath: draft.backgroundPath,
                        foregroundPath: draft.foregroundPath,
                        layer: _tool == _RegistrationTool.parallax
                            ? _liveLayer
                            : 'composite',
                        crop: _crop,
                        filter: _filter),
                cropping: _tool == _RegistrationTool.crop,
                aspectRatio:
                    _ratio.aspectRatio(source.size, display, editing: true),
                onCropChanged: (crop) => setState(() {
                  _crop = crop;
                }),
                enabled: !_saving,
                maxHeight: bounds.maxHeight,
              ),
            ),
          ));
        }),
      );

  Widget _tools(ColorScheme colors) => SingleChildScrollView(
        key: const ValueKey('registration-tools'),
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        child: Row(children: [
          for (final tool in _RegistrationTool.values)
            Padding(
              key: _toolKeys[tool],
              padding: const EdgeInsets.symmetric(horizontal: 3),
              child: Semantics(
                  selected: _tool == tool,
                  child: TextButton(
                    key: ValueKey('registration-tool-${tool.name}'),
                    onPressed: _saving ? null : () => _selectTool(tool),
                    style: TextButton.styleFrom(
                      foregroundColor: _tool == tool
                          ? colors.primary
                          : colors.onSurfaceVariant,
                      backgroundColor: Colors.transparent,
                      minimumSize: const Size(76, 64),
                      shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(14)),
                    ),
                    child: Column(mainAxisSize: MainAxisSize.min, children: [
                      Icon(tool.icon, size: 23),
                      const SizedBox(height: 4),
                      Text(tool.label, style: const TextStyle(fontSize: 12)),
                    ]),
                  )),
            ),
        ]),
      );

  Widget _saveActions() => Row(children: [
        Expanded(
            child: OutlinedButton(
          key: const ValueKey('save-registration'),
          onPressed: _source == null || _saving ? null : () => _save(),
          child: _exporting
              ? const SizedBox.square(
                  dimension: 20,
                  child: CircularProgressIndicator(strokeWidth: 2))
              : const Text('저장'),
        )),
        const SizedBox(width: 8),
        Expanded(
            child: FilledButton(
                key: const ValueKey('register-wallpaper'),
                onPressed: _source == null || _saving
                    ? null
                    : () => _save(wallpaper: true),
                child: const Text('배경화면 등록', textAlign: TextAlign.center))),
      ]);

  Widget _live2DControls() {
    final draft = _candidate ?? _live2D;
    final wide = MediaQuery.sizeOf(context).aspectRatio > 1;
    final layers = SegmentedButton<String>(
        segments: const [
          ButtonSegment(value: 'composite', label: Text('미리보기')),
          ButtonSegment(value: 'background', label: Text('배경')),
          ButtonSegment(value: 'foreground', label: Text('인물·사물'))
        ],
        selected: {
          _liveLayer
        },
        onSelectionChanged: (values) =>
            setState(() => _liveLayer = values.single));
    final apply = FilledButton(
        key: const ValueKey('activate-live2d'),
        onPressed: _saving ? null : _applyLive2D,
        child: const Text('Live 2D 적용', textAlign: TextAlign.center));
    final retry = TextButton(
        onPressed: _saving ? null : () => _openLive2D(retry: true),
        child: const Text('다시 분리'));
    return Column(mainAxisSize: MainAxisSize.min, children: [
      if (_splitting)
        const Row(mainAxisAlignment: MainAxisAlignment.center, children: [
          SizedBox.square(
              dimension: 18, child: CircularProgressIndicator(strokeWidth: 2)),
          SizedBox(width: 10),
          Flexible(child: Text('배경과 인물·사물을 분리하고 있습니다…')),
        ]),
      if (_splitError != null) Text(_splitError!, textAlign: TextAlign.center),
      if (!_splitting && draft != null) ...[
        if (wide)
          SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Row(children: [
                layers,
                const SizedBox(width: 12),
                apply,
                const SizedBox(width: 8),
                retry,
              ]))
        else ...[
          SingleChildScrollView(
              scrollDirection: Axis.horizontal, child: layers),
          const SizedBox(height: 8),
          Row(mainAxisAlignment: MainAxisAlignment.center, children: [
            Flexible(child: apply),
            const SizedBox(width: 8),
            Flexible(child: retry),
          ]),
        ],
        Text(_live2D != null && _candidate == null
            ? 'Live 2D 적용됨 · 다른 편집을 계속할 수 있습니다.'
            : '확인 후 Live 2D 적용을 눌러 선택하세요.'),
      ],
      if (!_splitting && draft == null)
        TextButton(
            onPressed: _saving ? null : () => _openLive2D(retry: true),
            child: const Text('다시 시도')),
    ]);
  }

  Widget _information() => LayoutBuilder(builder: (_, bounds) {
        final name = TextFormField(
          controller: _titleController,
          focusNode: _titleFocus,
          enabled: !_saving,
          maxLength: 160,
          textInputAction: TextInputAction.done,
          onChanged: (_) {
            if (_titleError != null) setState(() => _titleError = null);
          },
          decoration: InputDecoration(
              labelText: '이미지 이름',
              counterText: '',
              errorText: _titleError,
              filled: false),
        );
        final tags = InkWell(
          key: const ValueKey('registration-tag-picker'),
          onTap: _saving ? null : _openTagPicker,
          borderRadius: BorderRadius.circular(12),
          child: InputDecorator(
            decoration: const InputDecoration(
                labelText: '태그',
                filled: false,
                suffixIcon: Icon(Icons.expand_more)),
            child: Text(
                _tags.isEmpty
                    ? '태그 선택'
                    : selectableGalleryTags.where(_tags.contains).join(', '),
                maxLines: 1,
                overflow: TextOverflow.ellipsis),
          ),
        );
        return bounds.maxWidth >= 480
            ? Row(children: [
                Expanded(child: name),
                const SizedBox(width: 12),
                Expanded(child: tags)
              ])
            : Column(
                mainAxisSize: MainAxisSize.min,
                children: [name, const SizedBox(height: 12), tags]);
      });

  Widget _filters() => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: Row(children: [
              for (final filter in PhotoFilter.values)
                Padding(
                  padding: const EdgeInsets.only(right: 8),
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
                                width: 2,
                                color: _filter == filter
                                    ? Theme.of(context).colorScheme.primary
                                    : Theme.of(context)
                                        .colorScheme
                                        .outlineVariant)),
                        child: Column(children: [
                          if (_source case final source?)
                            ClipRRect(
                              borderRadius: BorderRadius.circular(8),
                              child: ColorFiltered(
                                  colorFilter:
                                      ColorFilter.matrix(filter.matrix),
                                  child: SizedBox.square(
                                    dimension: 64,
                                    child: Center(
                                        child: AspectRatio(
                                      aspectRatio: source.size.aspectRatio,
                                      child: RawImage(
                                          image: source.preview,
                                          fit: BoxFit.contain),
                                    )),
                                  )),
                            ),
                          const SizedBox(height: 6),
                          Text(filter.label,
                              style: const TextStyle(fontSize: 12)),
                        ]),
                      ),
                    ),
                  ),
                ),
            ]),
          ),
        ],
      );
}
