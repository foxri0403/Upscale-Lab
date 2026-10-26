import 'package:flutter/material.dart';
import '../models/wallpaper_save_selection.dart';
import '../services/wallpaper_service.dart';

class WallpaperRemoveDialog extends StatefulWidget {
  const WallpaperRemoveDialog({super.key});

  @override
  State<WallpaperRemoveDialog> createState() => _WallpaperRemoveDialogState();
}

class _WallpaperRemoveDialogState extends State<WallpaperRemoveDialog> {
  bool _home = false;
  bool _lock = false;

  @override
  Widget build(BuildContext context) {
    final target = WallpaperSaveSelection(home: _home, lock: _lock).target;
    return AlertDialog(
      title: const Text('배경화면 제거'),
      content: SingleChildScrollView(
          child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          CheckboxListTile(
            key: const ValueKey('remove-wallpaper-home'),
            contentPadding: EdgeInsets.zero,
            controlAffinity: ListTileControlAffinity.leading,
            title: const Text('홈 화면'),
            value: _home,
            onChanged: (value) => setState(() => _home = value ?? false),
          ),
          CheckboxListTile(
            key: const ValueKey('remove-wallpaper-lock'),
            contentPadding: EdgeInsets.zero,
            controlAffinity: ListTileControlAffinity.leading,
            title: const Text('잠금 화면'),
            value: _lock,
            onChanged: (value) => setState(() => _lock = value ?? false),
          ),
          const SizedBox(height: 12),
          const Text(
              '제거할 화면을 선택하세요. 등록된 이미지는 보관됩니다.\n잠금 화면만 제거하면 Android에서 홈 배경화면을 함께 표시할 수 있습니다.'),
        ],
      )),
      actions: [
        TextButton(
            onPressed: () => Navigator.pop(context), child: const Text('취소')),
        FilledButton(
          key: const ValueKey('confirm-wallpaper-remove'),
          onPressed: target == null
              ? null
              : () => Navigator.pop<WallpaperTarget>(context, target),
          child: const Text('삭제'),
        ),
      ],
    );
  }
}
