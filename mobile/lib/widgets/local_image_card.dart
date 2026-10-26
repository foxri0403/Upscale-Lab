import 'dart:io';

import 'package:flutter/material.dart';

import '../models/local_image.dart';
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
        : Icon(Icons.add, size: isGrid ? 42 : 32, color: Theme.of(context).colorScheme.onSurfaceVariant);
    final label = Text(creating ? '등록 중...' : '새 이미지 등록',
        style: const TextStyle(fontWeight: FontWeight.w600));
    return Material(
      color: Theme.of(context).colorScheme.surfaceContainerHigh,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.zero,
        side: BorderSide(color: Theme.of(context).colorScheme.outlineVariant),
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

  Widget _menu(BuildContext context) => PopupMenuButton<String>(
        tooltip: '이미지 메뉴',
        enabled: !busy,
        icon: Icon(Icons.more_vert,
            size: 22, color: isGrid ? Colors.white : Theme.of(context).colorScheme.onSurfaceVariant),
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
          _action(context,
              'share', image.isShared ? '공유 취소' : '공유', Icons.share_outlined),
          if (!image.isShared) _action(context, 'edit', '수정', Icons.edit_outlined),
          _action(context, 'delete', '삭제', Icons.delete_outline, destructive: true),
        ],
      );

  PopupMenuItem<String> _action(BuildContext context, String value, String title, IconData icon,
          {bool destructive = false}) =>
      PopupMenuItem(
        value: value,
        child: Row(children: [
          Icon(icon,
              size: 20,
              color: destructive ? Theme.of(context).colorScheme.error : Theme.of(context).colorScheme.onSurfaceVariant),
          const SizedBox(width: 14),
          Text(title,
              style: TextStyle(
                  color: destructive ? Theme.of(context).colorScheme.error : Theme.of(context).colorScheme.onSurface)),
        ]),
      );

  Widget _thumbnail(BuildContext context) => Image.file(
        File(image.path),
        fit: BoxFit.cover,
        cacheWidth: isGrid ? 480 : 200,
        errorBuilder: (_, __, ___) => ColoredBox(
          color: Theme.of(context).colorScheme.primaryContainer,
          child:
              Center(child: Icon(Icons.image_outlined, color: Theme.of(context).colorScheme.onSurfaceVariant)),
        ),
      );

  Widget _details(BuildContext context) => Column(
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
            Icon(Icons.sell_outlined, size: 14, color: Theme.of(context).colorScheme.onSurfaceVariant),
            const SizedBox(width: 5),
            Expanded(
                child: Text(
              image.tags.join(' · '),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 12, color: Theme.of(context).colorScheme.onSurfaceVariant),
            )),
          ]),
        ],
      );

  @override
  Widget build(BuildContext context) => Material(
        color: isGrid ? Theme.of(context).colorScheme.surface : Colors.transparent,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.zero,
          side: isGrid
              ? BorderSide(color: Theme.of(context).colorScheme.outlineVariant)
              : BorderSide.none,
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: busy ? null : onTap,
          child: Stack(children: [
            if (isGrid)
              Column(children: [
                AspectRatio(
                    aspectRatio: 1,
                    child: Stack(fit: StackFit.expand, children: [
                      _thumbnail(context),
                      Positioned(
                          top: 4,
                          right: 4,
                          child: Material(
                            color: Colors.black45,
                            shape: const CircleBorder(),
                            child: _menu(context),
                          )),
                    ])),
                Padding(padding: const EdgeInsets.all(10), child: _details(context)),
              ])
            else
              Padding(
                  padding: const EdgeInsets.symmetric(vertical: 10),
                  child: Row(children: [
                    SizedBox.square(dimension: 80, child: _thumbnail(context)),
                    const SizedBox(width: 14),
                    Expanded(child: _details(context)),
                    _menu(context),
                  ])),
            if (busy)
              Positioned.fill(
                  child: ColoredBox(
                color: Theme.of(context).colorScheme.surface.withValues(alpha: 0.65),
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
