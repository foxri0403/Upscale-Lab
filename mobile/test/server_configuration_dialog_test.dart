import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/main.dart';
import 'package:live_layer/services/api_client.dart';

void main() {
  testWidgets('saving the server address closes the dialog cleanly',
      (tester) async {
    const configChannel = MethodChannel('live_layer/config');
    tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
      configChannel,
      (_) async => null,
    );
    addTearDown(
      () => tester.binding.defaultBinaryMessenger
          .setMockMethodCallHandler(configChannel, null),
    );
    final apiClient = ApiClient(baseUrl: 'https://old.example.com');

    await tester.pumpWidget(LiveLayerApp(apiClient: apiClient));
    await tester.tap(find.text('서버 주소 설정'));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byType(TextField).last,
      'https://new.example.com/',
    );
    await tester.tap(find.text('저장'));
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
    expect(apiClient.baseUrl, 'https://new.example.com');
  });
}
