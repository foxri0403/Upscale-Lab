import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'image_editor_page.dart';
import 'models/gallery_tags.dart';
import 'models/image_ratio.dart';
import 'services/image_editor_service.dart';
import 'widgets/app_components.dart';
import 'widgets/device_display_builder.dart';
import 'widgets/image_ratio_selector.dart';

class ImageRegistrationOptions {
  const ImageRegistrationOptions({
    required this.title,
    required this.tags,
    this.editedFile,
    this.ratio = const ImageRatioSettings(),
  });
  final String title;
  final List<String> tags;
  final EditedImageFile? editedFile;
  final ImageRatioSettings ratio;
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
  final _formKey = GlobalKey<FormState>();
  late final _titleController = TextEditingController(text: widget.title);
  final _titleFocus = FocusNode();
  final Set<String> _tags = {};
  EditorImageSource? _source;
  String? _error;
  bool _editing = false;
  ImageRatioSettings _ratio = const ImageRatioSettings();

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
    super.dispose();
  }

  Future<void> _register({required bool edit}) async {
    if (!_formKey.currentState!.validate() || _source == null || _editing) {
      return;
    }
    FocusScope.of(context).unfocus();
    final title = _titleController.text.trim();
    final tags = registrationTags(_tags);
    ImageEditorResult? result;
    if (edit) {
      setState(() => _editing = true);
      result = await Navigator.push<ImageEditorResult>(
          context,
          MaterialPageRoute(
              builder: (_) => ImageEditorPage(
                  filePath: widget.filePath, initialRatio: _ratio)));
      if (!mounted) {
        await result?.editedFile?.dispose();
        return;
      }
      setState(() => _editing = false);
      if (result == null) return;
    }
    if (!mounted) return;
    Navigator.pop(
        context,
        ImageRegistrationOptions(
            title: title,
            tags: tags,
            editedFile: result?.editedFile,
            ratio: result?.ratio ?? _ratio));
  }

  @override
  Widget build(BuildContext context) => DeviceDisplayBuilder(builder: _build);

  Widget _build(BuildContext context, Size display) {
    final source = _source;
    final tileHeight =
        math.max(52.0, MediaQuery.textScalerOf(context).scale(14) * 2 + 20);
    return Scaffold(
      appBar: AppBar(title: Text('새 이미지 등록')),
      body: SafeArea(
        child: AppFormScrollView(
          keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
          child: Form(
            key: _formKey,
            child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (source != null)
                    SizedBox(
                        height: (MediaQuery.sizeOf(context).height * .4)
                            .clamp(180, 460),
                        child: Center(
                            child: ClipRRect(
                                borderRadius: BorderRadius.circular(16),
                                child: AspectRatio(
                                    aspectRatio: _ratio.aspectRatio(
                                        source.size, display)!,
                                    child: RawImage(
                                        key: const ValueKey(
                                            'registration-original-image'),
                                        image: source.preview,
                                        fit: BoxFit.cover)))))
                  else
                    SizedBox(
                        height: 180,
                        child: Center(
                            child: _error == null
                                ? const CircularProgressIndicator()
                                : Column(
                                    mainAxisSize: MainAxisSize.min,
                                    children: [
                                        Text(_error!),
                                        TextButton(
                                            onPressed: _load,
                                            child: Text('다시 시도')),
                                      ]))),
                  const SizedBox(height: 18),
                  ImageRatioSelector(
                      value: _ratio,
                      display: display,
                      onChanged: _editing
                          ? null
                          : (ratio) => setState(() => _ratio = ratio)),
                  const SizedBox(height: 18),
                  const AppSectionTitle('이미지 이름'),
                  const SizedBox(height: 10),
                  TextFormField(
                    controller: _titleController,
                    focusNode: _titleFocus,
                    maxLength: 160,
                    textInputAction: TextInputAction.done,
                    validator: (value) => value == null || value.trim().isEmpty
                        ? '이미지 이름을 입력해 주세요.'
                        : null,
                    decoration: InputDecoration(
                        counterText: '',
                        hintText: '이미지 이름',
                        suffixIcon: IconButton(
                            tooltip: '이름 변경',
                            icon: const Icon(Icons.edit_outlined),
                            onPressed: () => _titleFocus.requestFocus())),
                  ),
                  const SizedBox(height: 22),
                  const AppSectionTitle('태그'),
                  const SizedBox(height: 4),
                  Text('태그는 선택하지 않아도 됩니다.',
                      style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant, fontSize: 12)),
                  const SizedBox(height: 10),
                  for (var row = 0;
                      row < (selectableGalleryTags.length / 3).ceil();
                      row++)
                    Padding(
                        padding: const EdgeInsets.only(bottom: 8),
                        child: Row(children: [
                          for (var column = 0; column < 3; column++) ...[
                            if (column > 0) const SizedBox(width: 8),
                            Expanded(
                                child: SizedBox(
                                    height: tileHeight,
                                    child: row * 3 + column <
                                            selectableGalleryTags.length
                                        ? _tag(selectableGalleryTags[
                                            row * 3 + column])
                                        : null)),
                          ],
                        ])),
                  const SizedBox(height: 18),
                  FilledButton.icon(
                      onPressed: source == null || _editing
                          ? null
                          : () => _register(edit: true),
                      icon: const Icon(Icons.open_in_full),
                      label: Text('업스케일링')),
                  const SizedBox(height: 10),
                  OutlinedButton.icon(
                      onPressed: source == null || _editing
                          ? null
                          : () => _register(edit: false),
                      icon: const Icon(Icons.image_outlined),
                      label: Text('이미지 등록')),
                  const SizedBox(height: 8),
                  Text('원본 이미지와 비율 설정을 저장합니다.',
                      textAlign: TextAlign.center,
                      style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant, fontSize: 12)),
                ]),
          ),
        ),
      ),
    );
  }

  Widget _tag(String tag) {
    final selected = _tags.contains(tag);
    return Semantics(
        selected: selected,
        button: true,
        child: OutlinedButton(
          key: ValueKey('registration-tag-$tag'),
          onPressed: () => setState(() {
            selected ? _tags.remove(tag) : _tags.add(tag);
          }),
          style: OutlinedButton.styleFrom(
              padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 8),
              foregroundColor: selected ? Theme.of(context).colorScheme.primary : Theme.of(context).colorScheme.onSurface,
              backgroundColor: selected ? Theme.of(context).colorScheme.primaryContainer : Theme.of(context).colorScheme.surface,
              side: BorderSide(
                  color: selected ? Theme.of(context).colorScheme.primary : Theme.of(context).colorScheme.outlineVariant),
              shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(10))),
          child: Text(tag,
              maxLines: 2,
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 14)),
        ));
  }
}
