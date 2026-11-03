import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/main.dart' as entry;

Future<void> reveal(WidgetTester tester, Finder target) async {
  final dialog = find.byType(Dialog);
  final scroll = dialog.evaluate().isNotEmpty
      ? find.descendant(of: dialog, matching: find.byType(Scrollable)).first
      : find.byType(Scrollable).first;
  await tester.drag(scroll, const Offset(0, 10000));
  await tester.pumpAndSettle();
  await tester.scrollUntilVisible(target, 180, scrollable: scroll);
  await tester.pumpAndSettle();
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const channel = MethodChannel('upscale/companion');
  final calls = <MethodCall>[];
  late Map<String, dynamic> state;
  setUp(() {
    calls.clear();
    state = {
      'privacyAccepted': true,
      'loggedIn': false,
      'enabled': false,
      'saveIdentifier': false,
      'savedIdentifier': '',
      'liveEnabled': false,
      'alarmsEnabled': true,
      'needsLiveSetup': false,
      'notificationPermissionGranted': true,
      'displays': [
        {
          'name': 'Main display',
          'width': 1080,
          'height': 2400,
          'refreshRateHz': 120,
          'canApply': true
        }
      ]
    };
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (call) async {
      calls.add(call);
      switch (call.method) {
        case 'status':
          return state;
        case 'acceptPrivacy':
          state = {...state, 'privacyAccepted': true};
        case 'login':
          state = {
            ...state,
            'loggedIn': true,
            'enabled': true,
            'username': 'web-user',
            'account': 'user@example.invalid',
            'plan': 'Pro',
            'saveIdentifier': call.arguments['saveIdentifier'],
            'savedIdentifier': call.arguments['saveIdentifier'] == true
                ? call.arguments['identifier']
                : ''
          };
        case 'saveIdentifier':
          state = {
            ...state,
            'saveIdentifier': call.arguments['enabled'],
            'savedIdentifier': call.arguments['enabled'] == true
                ? call.arguments['identifier']
                : ''
          };
        case 'setAlarms':
          state = {...state, 'alarmsEnabled': call.arguments['enabled']};
        case 'setLive':
          state = {
            ...state,
            'liveEnabled': call.arguments['enabled'],
            'needsLiveSetup': call.arguments['enabled']
          };
        case 'activateLive':
          state = {...state, 'needsLiveSetup': false};
          return {'openLivePicker': false};
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
  testWidgets('consent is required before login or screen inventory',
      (tester) async {
    state = {...state, 'privacyAccepted': false};
    await entry.main();
    await tester.pumpAndSettle();
    expect(find.byType(TextField), findsNothing);
    expect(find.text('연결된 화면'), findsNothing);
    expect(tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
        isNull);
    await reveal(tester, find.byType(CheckboxListTile));
    await tester.tap(find.byType(CheckboxListTile));
    await tester.pumpAndSettle();
    await reveal(tester, find.text('동의하고 계속'));
    await tester.tap(find.text('동의하고 계속'));
    await tester.pumpAndSettle();
    expect(calls.singleWhere((c) => c.method == 'acceptPrivacy').arguments,
        {'accepted': true});
    expect(find.byType(TextField), findsNWidgets(2));
    await tester.pumpWidget(const SizedBox.shrink());
  });
  testWidgets(
      'saved ID pre-fills independently from automatic login and can be cleared',
      (tester) async {
    state = {...state, 'saveIdentifier': true, 'savedIdentifier': 'saved-user'};
    await entry.main();
    await tester.pumpAndSettle();
    expect(
        tester.widget<TextField>(find.byType(TextField).first).controller!.text,
        'saved-user');
    expect(find.text('연결된 화면'), findsNothing);
    expect(find.byType(SwitchListTile), findsNothing);
    await reveal(tester, find.byType(Checkbox).first);
    await tester.tap(find.byType(Checkbox).first);
    await tester.pumpAndSettle();
    expect(calls.where((c) => c.method == 'saveIdentifier').last.arguments,
        {'enabled': false, 'identifier': 'saved-user'});
    await tester.pumpWidget(const SizedBox.shrink());
  });
  testWidgets(
      'login opens top-aligned inventory and read-only account popup with ordered settings',
      (tester) async {
    await entry.main();
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField).first, 'web-user');
    await tester.enterText(find.byType(TextField).last, 'Password1!');
    await reveal(tester, find.text('로그인'));
    await tester.tap(find.text('로그인'));
    await tester.pumpAndSettle();
    expect(calls.singleWhere((c) => c.method == 'login').arguments, {
      'identifier': 'web-user',
      'password': 'Password1!',
      'remember': true,
      'saveIdentifier': false
    });
    expect(find.text('배경화면 동기화'), findsNothing);
    expect(find.text('내 Android'), findsNothing);
    expect(find.text('Live 2D 승인 필요'), findsNothing);
    expect(find.text('기기 다시 연결'), findsNothing);
    expect(find.text('로그아웃'), findsNothing);
    expect(
        tester.getTopLeft(find.text('연결된 화면')).dy -
            tester.getBottomLeft(find.byType(Image).first).dy,
        lessThanOrEqualTo(45));
    expect(tester.getTopLeft(find.text('잠금 화면')).dx,
        lessThan(tester.getTopLeft(find.text('홈 화면')).dx));
    await tester.tap(find.byTooltip('계정 정보'));
    await tester.pumpAndSettle();
    expect(find.byType(Dialog), findsOneWidget);
    expect(find.text('web-user'), findsOneWidget);
    expect(find.text('Pro'), findsOneWidget);
    expect(find.byType(TextField), findsNothing);
    final alarms = find.widgetWithText(SwitchListTile, '알람'),
        live = find.widgetWithText(SwitchListTile, 'Live 2D 활성화');
    expect(tester.getTopLeft(alarms).dy, lessThan(tester.getTopLeft(live).dy));
    await reveal(tester, alarms);
    await tester.tap(alarms);
    await tester.pumpAndSettle();
    expect(calls.singleWhere((c) => c.method == 'setAlarms').arguments,
        {'enabled': false});
    await reveal(tester, live);
    await tester.tap(live);
    await tester.pumpAndSettle();
    expect(calls.singleWhere((c) => c.method == 'setLive').arguments,
        {'enabled': true});
    expect(calls.where((c) => c.method == 'activateLive'), hasLength(1));
    await reveal(tester, find.text('로그아웃'));
    await tester.tap(find.text('로그아웃'));
    await tester.pumpAndSettle();
    expect(find.byType(Dialog), findsNothing);
    expect(find.text('로그인'), findsOneWidget);
    expect(find.textContaining('1080'), findsNothing);
    await tester.pumpWidget(const SizedBox.shrink());
  });
  testWidgets('failed login clears password and remains on login screen',
      (tester) async {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (call) async {
      if (call.method == 'status') return state;
      throw PlatformException(code: 'failed', message: '연결 실패');
    });
    await entry.main();
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField).last, 'secret');
    await reveal(tester, find.text('로그인'));
    await tester.tap(find.text('로그인'));
    await tester.pumpAndSettle();
    expect(find.text('연결 실패'), findsOneWidget);
    expect(
        tester.widget<TextField>(find.byType(TextField).last).controller!.text,
        '');
    expect(find.text('연결된 화면'), findsNothing);
    await tester.pumpWidget(const SizedBox.shrink());
  });
  for (final size in [const Size(320, 640), const Size(1280, 800)]) {
    testWidgets('popup and screen list remain usable at $size with larger text',
        (tester) async {
      tester.view.physicalSize = size;
      tester.view.devicePixelRatio = 1;
      tester.platformDispatcher.textScaleFactorTestValue = 1.5;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
      await entry.main();
      await tester.pumpAndSettle();
      await reveal(tester, find.text('로그인'));
      await tester.tap(find.text('로그인'));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      await tester.tap(find.byTooltip('계정 정보'));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      await reveal(tester, find.text('로그아웃'));
      expect(tester.takeException(), isNull);
      await reveal(tester, find.byTooltip('닫기'));
      await tester.tap(find.byTooltip('닫기'));
      await tester.pumpAndSettle();
      await tester.pumpWidget(const SizedBox.shrink());
    });
  }
}
