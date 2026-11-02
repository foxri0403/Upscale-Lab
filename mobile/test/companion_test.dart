import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/main.dart' as entry;

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const channel = MethodChannel('upscale/companion');
  final calls = <MethodCall>[];
  late Map<String, dynamic> state;

  setUp(() {
    calls.clear();
    state = {
      'loggedIn': false,
      'server': 'https://api.example.invalid',
      'enabled': false,
      'displays': [
        {'name': 'Main display', 'width': 1080, 'height': 2400,
          'refreshRateHz': 120, 'canApply': true},
      ],
    };
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (call) async {
      calls.add(call);
      switch (call.method) {
        case 'status':
          return state;
        case 'login':
          state = {...state, 'loggedIn': true, 'enabled': true,
            'account': 'web-user', 'pendingLive': true};
        case 'activateLive':
          state = {...state, 'pendingLive': false};
        case 'disable':
          state = {...state, 'enabled': false};
        case 'logout':
          state = {...state, 'loggedIn': false, 'enabled': false};
      }
      return null;
    });
  });

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, null);
  });

  testWidgets('entry point connects the account and controls native wallpaper sync', (tester) async {
    await entry.main();
    await tester.pumpAndSettle();
    expect(find.text('웹에서 선택하고, 이 기기에 적용하세요'), findsOneWidget);
    await tester.enterText(find.byType(TextField).at(1), 'web-user');
    await tester.enterText(find.byType(TextField).at(2), 'Password1!');
    await tester.ensureVisible(find.text('로그인하고 기기 연결'));
    await tester.tap(find.text('로그인하고 기기 연결'));
    await tester.pumpAndSettle();
    final login = calls.singleWhere((call) => call.method == 'login');
    expect(login.arguments, {
      'server': 'https://api.example.invalid', 'identifier': 'web-user',
      'password': 'Password1!', 'remember': true,
    });
    expect(find.text('web-user'), findsOneWidget);
    await tester.ensureVisible(find.text('Live 2D 적용 확인'));
    await tester.tap(find.text('Live 2D 적용 확인'));
    await tester.pumpAndSettle();
    expect(calls.where((call) => call.method == 'activateLive'), hasLength(1));
    expect(find.text('Live 2D 적용 확인'), findsNothing);
    await tester.ensureVisible(find.byType(SwitchListTile));
    await tester.tap(find.byType(SwitchListTile));
    await tester.pumpAndSettle();
    expect(calls.where((call) => call.method == 'disable'), hasLength(1));
    await tester.ensureVisible(find.text('로그아웃'));
    await tester.tap(find.text('로그아웃'));
    await tester.pumpAndSettle();
    expect(find.text('로그인하고 기기 연결'), findsOneWidget);
    expect(calls.where((call) => call.method == 'logout'), hasLength(1));
    await tester.pumpWidget(const SizedBox.shrink());
  });

  testWidgets('native failures stay visible and allow retry', (tester) async {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (call) async {
      if (call.method == 'status') return state;
      throw PlatformException(code: 'companion_failed', message: '연결 실패');
    });
    await entry.main();
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('로그인하고 기기 연결'));
    await tester.tap(find.text('로그인하고 기기 연결'));
    await tester.pumpAndSettle();
    expect(find.text('연결 실패'), findsOneWidget);
    expect(tester.widget<FilledButton>(find.byType(FilledButton)).onPressed, isNotNull);
    await tester.pumpWidget(const SizedBox.shrink());
  });
}
