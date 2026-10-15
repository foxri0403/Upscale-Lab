import 'package:flutter/material.dart';

import '../models/wallpaper_save_selection.dart';

class WallpaperSaveDialog extends StatefulWidget {
  const WallpaperSaveDialog({super.key});

  @override
  State<WallpaperSaveDialog> createState() => _WallpaperSaveDialogState();
}

class _WallpaperSaveDialogState extends State<WallpaperSaveDialog> {
  bool _home = false;
  bool _lock = false;

  @override
  Widget build(BuildContext context) {
    final selection = WallpaperSaveSelection(home: _home, lock: _lock);
    return AlertDialog(
      title: const Text('배경화면 적용'),
      content: SingleChildScrollView(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          CheckboxListTile(
            key: const ValueKey('wallpaper-home'),
            contentPadding: EdgeInsets.zero,
            controlAffinity: ListTileControlAffinity.leading,
            title: const Text('홈 화면'),
            value: _home,
            onChanged: (value) => setState(() => _home = value ?? false),
          ),
          CheckboxListTile(
            key: const ValueKey('wallpaper-lock'),
            contentPadding: EdgeInsets.zero,
            controlAffinity: ListTileControlAffinity.leading,
            title: const Text('잠금 화면'),
            value: _lock,
            onChanged: (value) => setState(() => _lock = value ?? false),
          ),
          const SizedBox(height: 12),
          Text(selection.description,
              key: const ValueKey('wallpaper-selection-description'),
              style: TextStyle(
                  color: Theme.of(context).colorScheme.onSurfaceVariant)),
        ]),
      ),
      actions: [
        TextButton(
            onPressed: () => Navigator.pop(context), child: const Text('취소')),
        FilledButton(
            key: const ValueKey('confirm-image-save'),
            onPressed: () => Navigator.pop(
                context, WallpaperSaveSelection(home: _home, lock: _lock)),
            child: const Text('저장')),
      ],
    );
  }
}
