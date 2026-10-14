import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

class AppSectionTitle extends StatelessWidget {
  const AppSectionTitle(this.title, {super.key, this.trailing});
  final String title;
  final String? trailing;

  @override
  Widget build(BuildContext context) => Row(children: [
        Flexible(
            child: Text(title,
                style: const TextStyle(
                    fontSize: 17, fontWeight: FontWeight.w700))),
        if (trailing != null) ...[
          const SizedBox(width: 10),
          Text(trailing!,
              style: const TextStyle(color: AppTheme.muted, fontSize: 12)),
        ],
      ]);
}

class AppPanel extends StatelessWidget {
  const AppPanel(
      {super.key,
      required this.child,
      this.padding = const EdgeInsets.all(16)});
  final Widget child;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) => Material(
        color: AppTheme.surface,
        clipBehavior: Clip.antiAlias,
        shape: RoundedRectangleBorder(
          side: const BorderSide(color: AppTheme.border),
          borderRadius: BorderRadius.circular(16),
        ),
        child: Padding(padding: padding, child: child),
      );
}

class AppFormBody extends StatelessWidget {
  const AppFormBody({super.key, required this.child});
  final Widget child;

  @override
  Widget build(BuildContext context) => Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 560),
          child: child,
        ),
      );
}

class AppFormScrollView extends StatelessWidget {
  const AppFormScrollView({
    super.key,
    required this.child,
    this.padding,
    this.keyboardDismissBehavior = ScrollViewKeyboardDismissBehavior.manual,
  });
  final Widget child;
  final EdgeInsetsGeometry? padding;
  final ScrollViewKeyboardDismissBehavior keyboardDismissBehavior;

  @override
  Widget build(BuildContext context) => SingleChildScrollView(
        padding: padding,
        keyboardDismissBehavior: keyboardDismissBehavior,
        child: AppFormBody(child: child),
      );
}

class LibraryLayoutToggle extends StatelessWidget {
  const LibraryLayoutToggle(
      {super.key, required this.isGrid, required this.onChanged});
  final bool isGrid;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) => Container(
        decoration: BoxDecoration(
          color: const Color(0xFFF0F3FA),
          borderRadius: BorderRadius.circular(14),
        ),
        padding: const EdgeInsets.all(3),
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          _button(false, Icons.format_list_bulleted, '1열 목록'),
          _button(true, Icons.grid_view_rounded, '2열 그리드'),
        ]),
      );

  Widget _button(bool grid, IconData icon, String label) => Semantics(
        selected: isGrid == grid,
        child: IconButton(
          tooltip: label,
          onPressed: () => onChanged(grid),
          style: IconButton.styleFrom(
            backgroundColor:
                isGrid == grid ? AppTheme.tint : Colors.transparent,
            foregroundColor: isGrid == grid ? AppTheme.primary : AppTheme.muted,
            shape:
                RoundedRectangleBorder(borderRadius: BorderRadius.circular(11)),
          ),
          icon: Icon(icon, size: 22),
        ),
      );
}

class SharedBadge extends StatelessWidget {
  const SharedBadge({super.key});

  @override
  Widget build(BuildContext context) => Semantics(
        label: '공유된 이미지',
        child: const Row(mainAxisSize: MainAxisSize.min, children: [
          Text('공유', style: TextStyle(color: AppTheme.primary, fontSize: 11)),
          SizedBox(width: 4),
          DecoratedBox(
            decoration:
                BoxDecoration(color: AppTheme.primary, shape: BoxShape.circle),
            child: Padding(
              padding: EdgeInsets.all(2),
              child: Icon(Icons.check_rounded, size: 11, color: Colors.white),
            ),
          ),
        ]),
      );
}
