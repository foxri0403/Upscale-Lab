import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/theme/app_theme.dart';
import 'package:live_layer/widgets/app_components.dart';
import 'package:live_layer/widgets/local_image_card.dart';

void main() {
  test('light and dark themes have accessible surface and text roles', () {
    final light = AppTheme.light;
    final dark = AppTheme.dark;

    expect(light.brightness, Brightness.light);
    expect(dark.brightness, Brightness.dark);
    expect(light.colorScheme.surface, isNot(dark.colorScheme.surface));
    expect(light.colorScheme.onSurface, isNot(dark.colorScheme.onSurface));
    expect(dark.cardTheme.color, dark.colorScheme.surface);
    expect(dark.popupMenuTheme.color, dark.colorScheme.surfaceContainer);
    expect(dark.inputDecorationTheme.fillColor,
        dark.colorScheme.surfaceContainer);
    expect(AppTheme.overlayFor(Brightness.dark).statusBarIconBrightness,
        Brightness.light);
    expect(AppTheme.overlayFor(Brightness.light).statusBarIconBrightness,
        Brightness.dark);
  });

  testWidgets('panel and new image card use active theme colors', (tester) async {
    for (final theme in [AppTheme.light, AppTheme.dark]) {
      await tester.pumpWidget(MaterialApp(
        theme: theme,
        home: Scaffold(
          body: Column(children: [
            const AppPanel(child: Text('설정 테스트')),
            NewImageCard(isGrid: true, creating: false, onTap: () {}),
          ]),
        ),
      ));

      final panelMaterial = tester.widget<Material>(find.descendant(
        of: find.byType(AppPanel),
        matching: find.byType(Material),
      ).first);
      final cardMaterial = tester.widget<Material>(find.descendant(
        of: find.byType(NewImageCard),
        matching: find.byType(Material),
      ).first);
      expect(panelMaterial.color, theme.colorScheme.surface);
      expect(cardMaterial.color, theme.colorScheme.surfaceContainerHigh);
    }
  });
}
