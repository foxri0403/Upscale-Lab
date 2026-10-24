import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/models/image_ratio.dart';
import 'package:live_layer/widgets/image_ratio_selector.dart';

void main() {
  for (final display in [const Size(360, 720), const Size(720, 360)]) {
    testWidgets('automatic exclusively highlights itself on $display',
        (tester) async {
      var ratio = const ImageRatioSettings();
      await tester.pumpWidget(MaterialApp(
          home: Scaffold(body: StatefulBuilder(builder: (context, setState) {
        return ImageRatioSelector(
            value: ratio,
            display: display,
            automaticOnly: true,
            onChanged: (value) => setState(() => ratio = value));
      }))));
      final colors =
          Theme.of(tester.element(find.byType(ImageRatioSelector))).colorScheme;
      Color? color(IconData icon) =>
          tester.widget<Icon>(find.byIcon(icon)).color;
      void expectAutomaticOnly() {
        expect(color(Icons.stay_current_portrait), colors.onSurfaceVariant);
        expect(color(Icons.stay_current_landscape), colors.onSurfaceVariant);
        final automatic = tester.widget<TextButton>(
            find.byKey(const ValueKey('ratio-mode-automatic')));
        expect(automatic.style!.foregroundColor!.resolve({}), colors.primary);
      }

      expectAutomaticOnly();
      for (final portrait in [false, true]) {
        await tester.tap(find.byTooltip(portrait ? '세로 방향' : '가로 방향'));
        await tester.pumpAndSettle();
        expect(
            color(portrait
                ? Icons.stay_current_portrait
                : Icons.stay_current_landscape),
            colors.primary);
        expect(
            color(portrait
                ? Icons.stay_current_landscape
                : Icons.stay_current_portrait),
            colors.onSurfaceVariant);
        await tester.tap(find.byKey(const ValueKey('ratio-mode-automatic')));
        await tester.pumpAndSettle();
        expectAutomaticOnly();
      }
    });
  }
}
