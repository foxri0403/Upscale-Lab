import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/services/wallpaper_service.dart';
import 'package:live_layer/widgets/wallpaper_apply_dialog.dart';

void main() {
  for (final target in WallpaperTarget.values) {
    testWidgets('registration chooses $target with independent checkboxes',
        (tester) async {
      WallpaperTarget? selected;
      await tester.pumpWidget(MaterialApp(
          home: Builder(
              builder: (context) => Scaffold(
                  body: TextButton(
                      onPressed: () async {
                        selected = await showDialog<WallpaperTarget>(
                            context: context,
                            builder: (_) => const WallpaperApplyDialog());
                      },
                      child: const Text('열기'))))));
      await tester.tap(find.text('열기'));
      await tester.pumpAndSettle();
      final confirm = find.byKey(const ValueKey('confirm-wallpaper-apply'));
      expect(tester.widget<FilledButton>(confirm).onPressed, isNull);
      if (target != WallpaperTarget.lock) {
        await tester.tap(find.byKey(const ValueKey('wallpaper-home')));
      }
      if (target != WallpaperTarget.home) {
        await tester.tap(find.byKey(const ValueKey('wallpaper-lock')));
      }
      await tester.pumpAndSettle();
      await tester.tap(confirm);
      await tester.pumpAndSettle();
      expect(selected, target);
    });
  }

  testWidgets('no selection, deselection and cancel never register a wallpaper',
      (tester) async {
    WallpaperTarget? selected;
    await tester.pumpWidget(MaterialApp(
        home: Builder(
            builder: (context) => Scaffold(
                body: TextButton(
                    onPressed: () async {
                      selected = await showDialog<WallpaperTarget>(
                          context: context,
                          builder: (_) => const WallpaperApplyDialog());
                    },
                    child: const Text('열기'))))));
    await tester.tap(find.text('열기'));
    await tester.pumpAndSettle();
    for (final key in ['home', 'lock', 'home', 'lock']) {
      await tester.tap(find.byKey(ValueKey('wallpaper-$key')));
      await tester.pumpAndSettle();
    }
    expect(
        tester
            .widget<FilledButton>(
                find.byKey(const ValueKey('confirm-wallpaper-apply')))
            .onPressed,
        isNull);
    await tester.tap(find.text('취소'));
    await tester.pumpAndSettle();
    expect(selected, isNull);
  });
}
