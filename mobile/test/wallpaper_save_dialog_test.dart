import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/models/wallpaper_save_selection.dart';
import 'package:live_layer/services/wallpaper_service.dart';
import 'package:live_layer/theme/app_theme.dart';
import 'package:live_layer/widgets/wallpaper_save_dialog.dart';

void main() {
  for (final home in [false, true]) {
    for (final lock in [false, true]) {
      final target = home && lock
          ? WallpaperTarget.both
          : home
              ? WallpaperTarget.home
              : lock
                  ? WallpaperTarget.lock
                  : null;
      testWidgets('save returns $target from independent checkboxes',
          (tester) async {
        WallpaperSaveSelection? result;
        await tester.pumpWidget(MaterialApp(
          theme: AppTheme.light,
          home: Scaffold(
              body: Builder(
                  builder: (context) => TextButton(
                        onPressed: () async {
                          result = await showDialog<WallpaperSaveSelection>(
                              context: context,
                              builder: (_) => const WallpaperSaveDialog());
                        },
                        child: const Text('열기'),
                      ))),
        ));
        await tester.tap(find.text('열기'));
        await tester.pumpAndSettle();
        expect(find.byType(CheckboxListTile), findsNWidgets(2));
        expect(find.text('배경화면을 바꾸지 않고 이미지만 저장합니다.'), findsOneWidget);
        if (home) {
          await tester.tap(find.byKey(const ValueKey('wallpaper-home')));
        }
        if (lock) {
          await tester.tap(find.byKey(const ValueKey('wallpaper-lock')));
        }
        await tester.pumpAndSettle();
        expect(
            find.text(
                WallpaperSaveSelection(home: home, lock: lock).description),
            findsOneWidget);
        expect(
            tester
                .widget<CheckboxListTile>(
                    find.byKey(const ValueKey('wallpaper-home')))
                .value,
            home);
        expect(
            tester
                .widget<CheckboxListTile>(
                    find.byKey(const ValueKey('wallpaper-lock')))
                .value,
            lock);
        await tester.tap(find.byKey(const ValueKey('confirm-image-save')));
        await tester.pumpAndSettle();
        expect(result, isNotNull);
        expect(result!.target, target);
        expect(tester.takeException(), isNull);
      });
    }
  }

  testWidgets('both selections can be cleared and cancel returns no save',
      (tester) async {
    WallpaperSaveSelection? result;
    var completed = false;
    await tester.pumpWidget(MaterialApp(
        home: Scaffold(
            body: Builder(
      builder: (context) => TextButton(
          onPressed: () async {
            result = await showDialog<WallpaperSaveSelection>(
                context: context, builder: (_) => const WallpaperSaveDialog());
            completed = true;
          },
          child: const Text('열기')),
    ))));
    await tester.tap(find.text('열기'));
    await tester.pumpAndSettle();
    for (final key in [
      'wallpaper-home',
      'wallpaper-lock',
      'wallpaper-home',
      'wallpaper-lock'
    ]) {
      await tester.tap(find.byKey(ValueKey(key)));
      await tester.pumpAndSettle();
    }
    expect(find.text('배경화면을 바꾸지 않고 이미지만 저장합니다.'), findsOneWidget);
    await tester.tap(find.text('취소'));
    await tester.pumpAndSettle();
    expect(completed, isTrue);
    expect(result, isNull);
  });
}
