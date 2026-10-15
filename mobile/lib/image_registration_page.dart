import 'package:flutter/material.dart';

import 'models/gallery_tags.dart';
import 'models/image_editor.dart';
import 'models/image_ratio.dart';
import 'models/wallpaper_save_selection.dart';
import 'services/image_editor_service.dart';
import 'services/wallpaper_service.dart';
import 'widgets/app_components.dart';
import 'widgets/device_display_builder.dart';
import 'widgets/image_ratio_selector.dart';
import 'widgets/registration_image_canvas.dart';
import 'widgets/wallpaper_save_dialog.dart';

class ImageRegistrationOptions {
  const ImageRegistrationOptions({
    required this.title,
    required this.tags,
    this.editedFile,
    this.ratio = const ImageRatioSettings(),
    this.wallpaperTarget,
  });
  final String title;
  final List<String> tags;
  final EditedImageFile? editedFile;
  final ImageRatioSettings ratio;
  final WallpaperTarget? wallpaperTarget;
}

enum _RegistrationTool {
  information('사진 정보', Icons.info_outline),
  crop('자르기', Icons.crop),
  filter('필터', Icons.filter_vintage_outlined),
  upscale('업스케일', Icons.open_in_full),
  parallax('2.5D', Icons.view_in_ar_outlined);

  const _RegistrationTool(this.label, this.icon);
  final String label;
  final IconData icon;
  bool get pending => this == upscale || this == parallax;
}

class ImageFeaturePage extends StatefulWidget {
  const ImageFeaturePage(
      {super.key, required this.filePath, required this.title});
  final String filePath;
  final String title;

  @override
  State<ImageFeaturePage> createState() => _ImageFeaturePageState();
}

class _ImageFeaturePageState extends State<ImageFeaturePage> {
  late final _titleController = TextEditingController(text: widget.title);
  final _titleFocus = FocusNode();
  final _contentScroll = ScrollController();
  final _toolKeys = {
    for (final tool in _RegistrationTool.values) tool: GlobalKey()
  };
  final Set<String> _tags = {};
  EditorImageSource? _source;
  String? _error;
  String? _titleError;
  bool _saving = false;
  bool _exporting = false;
  _RegistrationTool _tool = _RegistrationTool.information;
  ImageRatioSettings _ratio = const ImageRatioSettings();
  PhotoFilter _filter = PhotoFilter.original;
  Rect _crop = const Rect.fromLTWH(0, 0, 1, 1);
  int _canvasRevision = 0;
  int _canvasPointers = 0;

  void _releaseCanvasPointer(PointerEvent _) => setState(() {
        if (_canvasPointers > 0) _canvasPointers--;
      });

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
    _source?.preview.dispose();
    _titleFocus.dispose();
    _titleController.dispose();
    _contentScroll.dispose();
    super.dispose();
  }

  void _selectTool(_RegistrationTool tool) {
    FocusScope.of(context).unfocus();
    setState(() => _tool = tool);
    if (_contentScroll.hasClients) _contentScroll.jumpTo(0);
    Scrollable.ensureVisible(_toolKeys[tool]!.currentContext!,
        alignment: .5, duration: const Duration(milliseconds: 180));
  }

  Future<void> _save() async {
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
    try {
      final selection = await showDialog<WallpaperSaveSelection>(
          context: context, builder: (_) => const WallpaperSaveDialog());
      if (selection == null || !mounted) return;
      setState(() => _exporting = true);
      edited = await ImageEditorService.export(
          sourcePath: widget.filePath, crop: _crop, filter: _filter);
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
      Navigator.pop(
          context,
          ImageRegistrationOptions(
            title: title,
            tags: registrationTags(_tags),
            editedFile: edited,
            ratio: !_ratio.automatic && _ratio.preset == CropPreset.free
                ? _ratio.copyWith(
                    freeAspectRatio: _crop.width *
                        _source!.size.width /
                        (_crop.height * _source!.size.height))
                : _ratio,
            wallpaperTarget: selection.target,
          ));
    } catch (_) {
      await edited?.dispose();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('이미지를 저장하지 못했습니다. 다시 시도해 주세요.')));
      }
    } finally {
      if (mounted) {
        setState(() {
          _saving = false;
          _exporting = false;
        });
      }
    }
  }

  void _reset() => setState(() {
        _crop = const Rect.fromLTWH(0, 0, 1, 1);
        _ratio = const ImageRatioSettings();
        _filter = PhotoFilter.original;
        _canvasRevision++;
      });

  @override
  Widget build(BuildContext context) => DeviceDisplayBuilder(builder: _build);

  Widget _build(BuildContext context, Size display) {
    final source = _source;
    final colors = Theme.of(context).colorScheme;
    return PopScope(
      canPop: !_saving,
      child: Scaffold(
        appBar: AppBar(title: const Text('새 이미지 등록'), actions: [
          IconButton(
              tooltip: '편집 초기화',
              onPressed: source == null || _saving ? null : _reset,
              icon: const Icon(Icons.restart_alt)),
        ]),
        body: SafeArea(
            child: Column(children: [
          Expanded(
              child: SingleChildScrollView(
            controller: _contentScroll,
            physics: _canvasPointers > 0
                ? const NeverScrollableScrollPhysics()
                : null,
            keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
            child: AppFormBody(
                child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (source != null) ...[
                  Listener(
                    onPointerDown: (_) => setState(() => _canvasPointers++),
                    onPointerUp: _releaseCanvasPointer,
                    onPointerCancel: _releaseCanvasPointer,
                    child: RegistrationImageCanvas(
                      key: ValueKey(_canvasRevision),
                      source: source,
                      crop: _crop,
                      filter: _filter,
                      cropping: _tool == _RegistrationTool.crop,
                      aspectRatio: _ratio.aspectRatio(source.size, display,
                          editing: true),
                      onCropChanged: (crop) => setState(() => _crop = crop),
                      enabled: !_saving,
                    ),
                  ),
                  const SizedBox(height: 8),
                  Text(
                      _tool == _RegistrationTool.crop
                          ? '모서리를 드래그해 자르기 · 두 손가락으로 확대·축소'
                          : '드래그로 전체 보기 · 두 손가락으로 확대·축소',
                      textAlign: TextAlign.center,
                      style: TextStyle(
                          color: colors.onSurfaceVariant, fontSize: 12)),
                ] else
                  SizedBox(
                      height: 180,
                      child: Center(
                        child: _error == null
                            ? const CircularProgressIndicator()
                            : Column(mainAxisSize: MainAxisSize.min, children: [
                                Text(_error!),
                                TextButton(
                                    onPressed: _load,
                                    child: const Text('다시 시도')),
                              ]),
                      )),
                const SizedBox(height: 16),
                AppPanel(
                    child: switch (_tool) {
                  _RegistrationTool.information => _information(),
                  _RegistrationTool.crop => ImageRatioSelector(
                      value: _ratio,
                      display: display,
                      allowFree: true,
                      onChanged: source == null || _saving
                          ? null
                          : (ratio) => setState(() => _ratio = ratio)),
                  _RegistrationTool.filter => _filters(),
                  _ => const SizedBox.shrink(),
                }),
              ],
            )),
          )),
          const Divider(),
          if (MediaQuery.viewInsetsOf(context).bottom == 0)
            SingleChildScrollView(
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
                          onPressed: _saving || tool.pending
                              ? null
                              : () => _selectTool(tool),
                          style: TextButton.styleFrom(
                            foregroundColor: _tool == tool
                                ? colors.primary
                                : colors.onSurfaceVariant,
                            backgroundColor:
                                _tool == tool ? colors.primaryContainer : null,
                            minimumSize: const Size(76, 64),
                            shape: RoundedRectangleBorder(
                                borderRadius: BorderRadius.circular(14)),
                          ),
                          child:
                              Column(mainAxisSize: MainAxisSize.min, children: [
                            Icon(tool.icon, size: 23),
                            const SizedBox(height: 4),
                            Text(tool.label,
                                style: const TextStyle(fontSize: 12)),
                            if (tool.pending)
                              const Text('준비 중',
                                  style: TextStyle(fontSize: 10)),
                          ]),
                        )),
                  ),
              ]),
            ),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
            child: SizedBox(
                width: double.infinity,
                child: FilledButton(
                  key: const ValueKey('save-registration'),
                  onPressed: source == null || _saving ? null : _save,
                  child: _exporting
                      ? const SizedBox.square(
                          dimension: 20,
                          child: CircularProgressIndicator(strokeWidth: 2))
                      : const Text('저장'),
                )),
          ),
        ])),
      ),
    );
  }

  Widget _information() => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const AppSectionTitle('사진 정보'),
          const SizedBox(height: 12),
          TextFormField(
            controller: _titleController,
            focusNode: _titleFocus,
            enabled: !_saving,
            maxLength: 160,
            textInputAction: TextInputAction.done,
            onChanged: (_) {
              if (_titleError != null) setState(() => _titleError = null);
            },
            decoration: InputDecoration(
                labelText: '이미지 이름', counterText: '', errorText: _titleError),
          ),
          const SizedBox(height: 16),
          const AppSectionTitle('태그', trailing: '여러 개 선택'),
          const SizedBox(height: 8),
          Wrap(spacing: 6, runSpacing: 4, children: [
            for (final tag in selectableGalleryTags)
              FilterChip(
                key: ValueKey('registration-tag-$tag'),
                label: Text(tag),
                selected: _tags.contains(tag),
                onSelected: _saving
                    ? null
                    : (selected) => setState(() {
                          selected ? _tags.add(tag) : _tags.remove(tag);
                        }),
              ),
          ]),
        ],
      );

  Widget _filters() => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const AppSectionTitle('필터'),
          const SizedBox(height: 12),
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
                          : () => setState(() => _filter = filter),
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
                                  child: RawImage(
                                      image: source.preview,
                                      width: 64,
                                      height: 64,
                                      fit: BoxFit.cover)),
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
