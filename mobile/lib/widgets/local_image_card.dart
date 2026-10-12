import 'dart:io';

import 'package:flutter/material.dart';

import '../models/local_image.dart';
import '../theme/app_theme.dart';
import 'app_components.dart';

class NewImageCard extends StatelessWidget {
  const NewImageCard(
      {super.key,
      required this.isGrid,
      required this.creating,
      required this.onTap});
  final bool isGrid;
  final bool creating;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final icon = creating
        ? const SizedBox.square(
            dimension: 28, child: CircularProgressIndicator(strokeWidth: 2))
        : Icon(Icons.add, size: isGrid ? 42 : 32, color: AppTheme.muted);
    final label = Text(creating ? '등록 중...' : '새 이미지 등록',
        style: const TextStyle(fontWeight: FontWeight.w600));
    return Material(
      color: AppTheme.tint.withValues(alpha: 0.35),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(16),
        side: const BorderSide(color: AppTheme.border),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: isGrid
              ? Column(mainAxisAlignment: MainAxisAlignment.center, children: [
                  icon,
                  const SizedBox(height: 16),
                  label,
                ])
              : Row(children: [icon, const SizedBox(width: 20), label]),
        ),
      ),
    );
  }
}

class LocalImageCard extends StatelessWidget {
  const LocalImageCard({
    super.key,
    required this.image,
    required this.isGrid,
    required this.busy,
    required this.onTap,
    required this.onShare,
    required this.onUnshare,
    required this.onEdit,
    required this.onDelete,
  });
  final LocalImageItem image;
  final bool isGrid;
  final bool busy;
  final VoidCallback onTap;
  final VoidCallback onShare;
  final VoidCallback onUnshare;
  final VoidCallback onEdit;
  final VoidCallback onDelete;

  Widget _menu() => PopupMenuButton<String>(
        tooltip: '이미지 메뉴',
        enabled: !busy,
        icon: Icon(Icons.more_vert,
            size: 22, color: isGrid ? Colors.white : AppTheme.muted),
        onSelected: (value) {
          switch (value) {
            case 'share':
              image.isShared ? onUnshare() : onShare();
            case 'edit':
              onEdit();
            case 'delete':
              onDelete();
          }
        },
        itemBuilder: (_) => [
          _action(
              'share', image.isShared ? '공유 취소' : '공유', Icons.share_outlined),
          _action('edit', '수정', Icons.edit_outlined),
          _action('delete', '삭제', Icons.delete_outline, destructive: true),
        ],
      );

  PopupMenuItem<String> _action(String value, String title, IconData icon,
          {bool destructive = false}) =>
      PopupMenuItem(
        value: value,
        child: Row(children: [
          Icon(icon,
              size: 20,
              color: destructive ? const Color(0xFFD93025) : AppTheme.muted),
          const SizedBox(width: 14),
          Text(title,
              style: TextStyle(
                  color: destructive ? const Color(0xFFD93025) : AppTheme.ink)),
        ]),
      );

  Widget _thumbnail() => Image.file(
        File(image.path),
        fit: BoxFit.cover,
        cacheWidth: isGrid ? 480 : 200,
        errorBuilder: (_, __, ___) => const ColoredBox(
          color: AppTheme.tint,
          child:
              Center(child: Icon(Icons.image_outlined, color: AppTheme.muted)),
        ),
      );

  Widget _details() => Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(children: [
            Expanded(
                child: Text(
              image.title,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w700),
            )),
            if (image.isShared) ...[
              const SizedBox(width: 6),
              const SharedBadge(),
            ],
          ]),
          const SizedBox(height: 6),
          Row(children: [
            const Icon(Icons.sell_outlined, size: 14, color: AppTheme.muted),
            const SizedBox(width: 5),
            Expanded(
                child: Text(
              image.tags.join(' · '),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 12, color: AppTheme.muted),
            )),
          ]),
        ],
      );

  @override
  Widget build(BuildContext context) => Material(
        color: isGrid ? AppTheme.surface : Colors.transparent,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
          side: isGrid
              ? const BorderSide(color: AppTheme.border)
              : BorderSide.none,
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: busy ? null : onTap,
          child: Stack(children: [
            if (isGrid)
              Column(children: [
                Expanded(
                    child: Stack(fit: StackFit.expand, children: [
                  _thumbnail(),
                  Positioned(
                      top: 4,
                      right: 4,
                      child: Material(
                        color: Colors.black45,
                        shape: const CircleBorder(),
                        child: _menu(),
                      )),
                ])),
                Padding(padding: const EdgeInsets.all(10), child: _details()),
              ])
            else
              Padding(
                  padding: const EdgeInsets.symmetric(vertical: 10),
                  child: Row(children: [
                    ClipRRect(
                        borderRadius: BorderRadius.circular(12),
                        child: SizedBox.square(
                            dimension: 80, child: _thumbnail())),
                    const SizedBox(width: 14),
                    Expanded(child: _details()),
                    _menu(),
                  ])),
            if (busy)
              const Positioned.fill(
                  child: ColoredBox(
                color: Color(0x55FFFFFF),
                child: Center(
                    child: SizedBox.square(
                  dimension: 26,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )),
              )),
          ]),
        ),
      );
}
