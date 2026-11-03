import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/main.dart' as entry;
import 'package:live_layer/companion.dart' show testApp;

Future<void> reveal(WidgetTester tester, Finder target) async {
  await tester.drag(find.byType(ListView), const Offset(0, 10000));
  await tester.pumpAndSettle();
  await tester.scrollUntilVisible(target, 200,
      scrollable: find.byType(Scrollable).first);
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
      'server': 'https://api.example.invalid',
      'enabled': false,
      'displays': [
        {
          'name': 'Main display',
          'width': 1080,
          'height': 2400,
          'refreshRateHz': 120,
          'canApply': true
        },
      ],
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
            'account': 'web-user',
            'pendingLive': true
          };
        case 'activateLive':
          state = {...state, 'pendingLive': false};
        case 'disable':
          state = {...state, 'enabled': false};
        case 'logout':
          state = {
            ...state,
            'privacyAccepted': true,
            'loggedIn': false,
            'enabled': false
          };
      }
      return null;
    });
  });

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, null);
  });

  testWidgets('first launch requires an explicit consent before showing login',
      (tester) async {
    state = {...state, 'privacyAccepted': false};
    await entry.main();
    await tester.pumpAndSettle();
    expect(find.byType(TextField), findsNothing);
    expect(find.text('이 기기의 화면'), findsNothing);
    expect(tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
        isNull);
    await reveal(tester, find.byType(CheckboxListTile));
    await tester.pumpAndSettle();
    await tester.tap(find.byType(CheckboxListTile));
    await tester.pumpAndSettle();
    await reveal(tester, find.text('동의하고 계속'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('동의하고 계속'));
    await tester.pumpAndSettle();
    expect(
        calls.singleWhere((call) => call.method == 'acceptPrivacy').arguments,
        {'accepted': true});
    expect(find.byType(TextField), findsNWidgets(2));
    expect(calls.where((call) => call.method == 'login'), isEmpty);
    await tester.pumpWidget(const SizedBox.shrink());
  });

  testWidgets('login page hides inventory even if stale native data exists',
      (tester) async {
    await entry.main();
    await tester.pumpAndSettle();
    expect(find.text('이 기기의 화면'), findsNothing);
    expect(find.textContaining('1080'), findsNothing);
    expect(find.byType(SwitchListTile), findsNothing);
    expect(find.text('수집 정보 안내'), findsNothing);
    expect(find.text('기기 다시 연결'), findsNothing);
    expect(find.text('테스트 앱'), testApp ? findsOneWidget : findsNothing);
    await tester.pumpWidget(const SizedBox.shrink());
  });

  testWidgets(
      'entry point connects the account and controls native wallpaper sync',
      (tester) async {
    await entry.main();
    await tester.pumpAndSettle();
    expect(find.text('이 기기를 연결하세요'), findsOneWidget);
    expect(find.byType(TextField), findsNWidgets(2));
    expect(find.text('서버 주소'), findsNothing);
    await tester.enterText(find.byType(TextField).at(0), 'web-user');
    await tester.enterText(find.byType(TextField).at(1), 'Password1!');
    await reveal(tester, find.text('로그인하고 연결'));
    await tester.tap(find.text('로그인하고 연결'));
    await tester.pumpAndSettle();
    final login = calls.singleWhere((call) => call.method == 'login');
    expect(login.arguments, {
      'identifier': 'web-user',
      'password': 'Password1!',
      'remember': !testApp,
    });
    expect(find.text('web-user'), findsOneWidget);
    expect(find.text('테스트 앱'), testApp ? findsOneWidget : findsNothing);
    await reveal(tester, find.text('Live 2D 적용 확인'));
    await tester.tap(find.text('Live 2D 적용 확인'));
    await tester.pumpAndSettle();
    expect(calls.where((call) => call.method == 'activateLive'), hasLength(1));
    expect(find.text('Live 2D 적용 확인'), findsNothing);
    await reveal(tester, find.byType(SwitchListTile));
    await tester.tap(find.byType(SwitchListTile));
    await tester.pumpAndSettle();
    expect(calls.where((call) => call.method == 'disable'), hasLength(1));
    await reveal(tester, find.text('로그아웃'));
    await tester.tap(find.text('로그아웃'));
    await tester.pumpAndSettle();
    expect(find.text('로그인하고 연결'), findsOneWidget);
    expect(calls.where((call) => call.method == 'logout'), hasLength(1));
    expect(find.text('이 기기의 화면'), findsNothing);
    expect(find.textContaining('1080'), findsNothing);
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
    await reveal(tester, find.text('로그인하고 연결'));
    await tester.tap(find.text('로그인하고 연결'));
    await tester.pumpAndSettle();
    expect(find.text('연결 실패'), findsOneWidget);
    expect(tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
        isNotNull);
    await tester.pumpWidget(const SizedBox.shrink());
  });
  for (final size in [const Size(320, 640), const Size(1280, 800)]) {
    testWidgets('responsive layout stays usable at $size with enlarged text',
        (tester) async {
      tester.view.physicalSize = size;
      tester.view.devicePixelRatio = 1;
      tester.platformDispatcher.textScaleFactorTestValue = 1.5;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
      await entry.main();
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      await reveal(tester, find.text('로그인하고 연결'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('로그인하고 연결'));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      await reveal(tester, find.text('Live 2D 적용 확인'));
      expect(find.text('Live 2D 적용 확인'), findsOneWidget);
      await reveal(tester, find.text('로그아웃'));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
    });
  }
}
