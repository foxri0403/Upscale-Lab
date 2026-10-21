import 'package:flutter/material.dart';

import '../models/gallery_tags.dart';
import '../models/image_ratio.dart';
import 'device_display_builder.dart';
import 'image_ratio_selector.dart';

class ImageEditResult {
  const ImageEditResult(
      {required this.title, required this.tags, required this.ratio});
  final String title;
  final List<String> tags;
  final ImageRatioSettings ratio;
}

class ImageEditDialog extends StatefulWidget {
  const ImageEditDialog(
      {super.key,
      required this.title,
      required this.tags,
      this.ratio = const ImageRatioSettings()});
  final String title;
  final List<String> tags;
  final ImageRatioSettings ratio;

  @override
  State<ImageEditDialog> createState() => _ImageEditDialogState();
}

class _ImageEditDialogState extends State<ImageEditDialog> {
  final _formKey = GlobalKey<FormState>();
  late final _title = TextEditingController(text: widget.title);
  late final _tags = widget.tags.where(selectableGalleryTags.contains).toSet();
  late ImageRatioSettings _ratio = widget.ratio;

  @override
  void dispose() {
    _title.dispose();
    super.dispose();
  }

  void _save() {
    if (!_formKey.currentState!.validate()) return;
    Navigator.pop(
        context,
        ImageEditResult(
          title: _title.text.trim(),
          ratio: _ratio,
          tags: registrationTags(_tags),
        ));
  }

  @override
  Widget build(BuildContext context) => DeviceDisplayBuilder(
      builder: (context, display) => AlertDialog(
            title: const Text('이미지 수정'),
            insetPadding:
                const EdgeInsets.symmetric(horizontal: 20, vertical: 24),
            content: SizedBox(
              width: 400,
              child: SingleChildScrollView(
                child: Form(
                  key: _formKey,
                  child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        ImageRatioSelector(
                            value: _ratio,
                            display: display,
                            onChanged: (ratio) =>
                                setState(() => _ratio = ratio)),
                        const SizedBox(height: 16),
                        TextFormField(
                          controller: _title,
                          maxLength: 160,
                          textInputAction: TextInputAction.done,
                          decoration: const InputDecoration(labelText: '이름'),
                          validator: (value) =>
                              value == null || value.trim().isEmpty
                                  ? '이미지 이름을 입력해 주세요.'
                                  : null,
                        ),
                        const SizedBox(height: 12),
                        const Text('태그',
                            style: TextStyle(fontWeight: FontWeight.w600)),
                        const SizedBox(height: 8),
                        Wrap(
                            spacing: 6,
                            runSpacing: 4,
                            children: selectableGalleryTags
                                .map((tag) => FilterChip(
                                      label: Text(tag),
                                      selected: _tags.contains(tag),
                                      onSelected: (checked) => setState(() {
                                        if (!checked) {
                                          _tags.remove(tag);
                                        } else {
                                          _tags.add(tag);
                                        }
                                      }),
                                    ))
                                .toList()),
                      ]),
                ),
              ),
            ),
            actions: [
              TextButton(
                  onPressed: () => Navigator.pop(context),
                  child: const Text('취소')),
              FilledButton(onPressed: _save, child: const Text('저장')),
            ],
          ));
}
