import 'package:flutter/material.dart';

import '../models/gallery_tags.dart';

Future<Set<String>?> showGalleryTagPicker(
  BuildContext context, {
  required Set<String> selectedTags,
}) {
  final selected = Set<String>.of(selectedTags);
  return showModalBottomSheet<Set<String>>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    builder: (sheetContext) => StatefulBuilder(
      builder: (sheetContext, update) {
        final allSelected = selectableGalleryTags.every(selected.contains);
        return FractionallySizedBox(
          heightFactor: 0.78,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(20, 12, 20, 16),
            child: Column(
              children: [
                Row(
                  children: [
                    const Expanded(
                      child: Text(
                        '태그 선택',
                        style: TextStyle(
                          fontSize: 18,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                    ),
                    IconButton(
                      tooltip: '태그 선택 닫기',
                      onPressed: () => Navigator.pop(sheetContext),
                      icon: const Icon(Icons.close),
                    ),
                  ],
                ),
                Expanded(
                  child: ListView(
                    children: [
                      CheckboxListTile(
                        title: const Text('전체 태그'),
                        value: allSelected,
                        onChanged: (value) => update(() {
                          selected.clear();
                          if (value == true) {
                            selected.addAll(selectableGalleryTags);
                          }
                        }),
                      ),
                      ...selectableGalleryTags.map(
                        (tag) => CheckboxListTile(
                          title: Text(tag),
                          value: selected.contains(tag),
                          onChanged: (value) => update(() {
                            if (value == true) {
                              selected.add(tag);
                            } else {
                              selected.remove(tag);
                            }
                          }),
                        ),
                      ),
                    ],
                  ),
                ),
                Row(
                  children: [
                    Expanded(
                      child: OutlinedButton(
                        onPressed: () => update(selected.clear),
                        child: const Text('초기화'),
                      ),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: FilledButton(
                        onPressed: () => Navigator.pop(sheetContext, selected),
                        child: const Text('적용하기'),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        );
      },
    ),
  );
}
