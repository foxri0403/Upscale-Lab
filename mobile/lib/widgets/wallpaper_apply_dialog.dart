import 'package:flutter/material.dart';
import '../models/wallpaper_save_selection.dart';
import '../services/wallpaper_service.dart';

class WallpaperApplyDialog extends StatefulWidget {
  const WallpaperApplyDialog({super.key});

  @override
  State<WallpaperApplyDialog> createState() => _WallpaperApplyDialogState();
}

class _WallpaperApplyDialogState extends State<WallpaperApplyDialog> {
  bool _home = false;
  bool _lock = false;

  @override
  Widget build(BuildContext context) {
    final selection = WallpaperSaveSelection(home: _home, lock: _lock);
    final target = selection.target;
    return AlertDialog(
      title: const Text('배경화면 등록'),
      content: SingleChildScrollView(
          child: Column(mainAxisSize: MainAxisSize.min, children: [
        CheckboxListTile(
            key: const ValueKey('wallpaper-home'),
            contentPadding: EdgeInsets.zero,
            controlAffinity: ListTileControlAffinity.leading,
            title: const Text('홈 화면'),
            value: _home,
            onChanged: (value) => setState(() => _home = value ?? false)),
        CheckboxListTile(
            key: const ValueKey('wallpaper-lock'),
            contentPadding: EdgeInsets.zero,
            controlAffinity: ListTileControlAffinity.leading,
            title: const Text('잠금 화면'),
            value: _lock,
            onChanged: (value) => setState(() => _lock = value ?? false)),
        const SizedBox(height: 12),
        Text(target == null ? '배경화면을 등록할 화면을 선택하세요.' : selection.description),
      ])),
      actions: [
        TextButton(
            onPressed: () => Navigator.pop(context), child: const Text('취소')),
        FilledButton(
            key: const ValueKey('confirm-wallpaper-apply'),
            onPressed: target == null
                ? null
                : () => Navigator.pop<WallpaperTarget>(context, target),
            child: const Text('등록')),
      ],
    );
  }
}
