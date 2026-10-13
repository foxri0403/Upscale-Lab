import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/main.dart';
import 'package:live_layer/image_editor_page.dart';
import 'package:live_layer/models/gallery.dart';
import 'package:live_layer/models/live_layer_models.dart';
import 'package:live_layer/models/image_editor.dart';
import 'package:live_layer/services/api_client.dart';
import 'package:live_layer/theme/app_theme.dart';
import 'package:live_layer/unified_home.dart';
import 'package:live_layer/widgets/app_components.dart';
import 'package:live_layer/widgets/local_image_card.dart';

class _UiApi extends ApiClient {
  _UiApi() : super(baseUrl: 'https://api.example.test');
  bool failEdit = false;
  @override
  String get storageScope => 'server\naccount';
  @override
  Future<List<GalleryPostItem>> getGallery(
          {String? search, String? tag, List<String>? tags}) async =>
      [];
  @override
  Future<double> getSensorSensitivity() async => 1;
  @override
  Future<List<String>> updateGalleryPost(
      {required String postId,
      required String title,
      required List<String> tags}) async {
    if (failEdit) throw StateError('offline');
    return tags;
  }
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const channel = MethodChannel('live_layer/storage');
  late Directory temp;
  late String imagePath;
  late List<Map<String, Object?>> images;
  late _UiApi api;
  late int localWrites;

  setUpAll(() async {
    temp = Directory.systemTemp.createTempSync('upscale-ui-test-');
    final recorder = ui.PictureRecorder();
    final canvas = Canvas(recorder);
    canvas.drawColor(AppTheme.tint, BlendMode.src);
    final picture = recorder.endRecording();
    final image = await picture.toImage(40, 40);
    final bytes = await image.toByteData(format: ui.ImageByteFormat.png);
    final file = File('${temp.path}/image.png')
      ..writeAsBytesSync(bytes!.buffer.asUint8List());
    imagePath = file.path;
    image.dispose();
    picture.dispose();
  });
  tearDownAll(() => temp.deleteSync(recursive: true));

  setUp(() {
    api = _UiApi();
    localWrites = 0;
    images = List.generate(
        2,
        (index) => {
              'id': 'image-$index',
              'title': index == 0 ? '로컬 이미지 이름' : '공유 이미지 이름',
              'path': imagePath,
              'width': 40,
              'height': 40,
              'mode': 'original',
              'tag': '풍경',
              'tags': ['풍경'],
              'createdAt': '2026-10-13T00:00:00Z',
              if (index == 1) 'cloudProjectId': 'project-id',
              if (index == 1) 'galleryPostId': 'post-id',
            });
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (call) async {
      expect((call.arguments as Map)['scope'], api.storageScope);
      if (call.method == 'listLocalImages') return images;
      if (call.method == 'updateLocalImageDetails') {
        localWrites++;
        final args = call.arguments as Map;
        final image = images.singleWhere((item) => item['id'] == args['id']);
        image['title'] = args['title'];
        image['tags'] = args['tags'];
        image['tag'] = (args['tags'] as List).first;
        return null;
      }
      throw StateError('Unexpected method: ${call.method}');
    });
  });
  tearDown(() => TestDefaultBinaryMessengerBinding
      .instance.defaultBinaryMessenger
      .setMockMethodCallHandler(channel, null));

  Future<void> show(WidgetTester tester, Widget page,
      {Size size = const Size(412, 892),
      double scale = 1,
      double keyboard = 0}) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final app = MaterialApp(
      theme: AppTheme.light,
      builder: (context, child) => MediaQuery(
        data: MediaQuery.of(context).copyWith(
          textScaler: TextScaler.linear(scale),
          padding: EdgeInsets.only(top: 24, bottom: keyboard == 0 ? 48 : 0),
          viewPadding: const EdgeInsets.only(top: 24, bottom: 48),
          viewInsets: EdgeInsets.only(bottom: keyboard),
        ),
        child: child!,
      ),
      home: page,
    );
    if (page is ImageFeaturePage || page is ImageEditorPage) {
      // The registration page decodes a real file outside Flutter's fake clock.
      final loadedImage = find.byKey(ValueKey(page is ImageFeaturePage
          ? 'registration-original-image'
          : 'image-editor-canvas'));
      await tester.runAsync(() async {
        await tester.pumpWidget(app);
        for (var attempt = 0; attempt < 100; attempt++) {
          await Future<void>.delayed(const Duration(milliseconds: 10));
          await tester.pump();
          if (loadedImage.evaluate().isNotEmpty) {
            break;
          }
        }
      });
      expect(loadedImage, findsOneWidget);
    } else {
      await tester.pumpWidget(app);
    }
    await tester.pumpAndSettle();
  }

  Widget home() => UnifiedHomePage(apiClient: api, onLogout: (_) async {});
  Finder card(int index) => find.byKey(ValueKey('image-$index'));
  Future<void> edit(WidgetTester tester, int index) async {
    final menu = find.descendant(
        of: card(index), matching: find.byType(PopupMenuButton<String>));
    await tester.ensureVisible(menu);
    await tester.tap(menu);
    await tester.pumpAndSettle();
    await tester.tap(find.text('수정'));
    await tester.pumpAndSettle();
  }

  testWidgets(
      'layout switch preserves items and only shared titles have a badge',
      (tester) async {
    await show(tester, home());
    expect(find.byType(SharedBadge), findsOneWidget);
    expect(find.text('로컬 · 공유 안 됨'), findsNothing);
    expect(find.text('공유됨 · 클라우드'), findsNothing);
    expect(find.descendant(of: card(0), matching: find.byType(SharedBadge)),
        findsNothing);
    for (final layout in ['1열 목록', '2열 그리드']) {
      await tester.tap(find.byTooltip(layout));
      await tester.pumpAndSettle();
      expect(tester.widget<LocalImageCard>(card(0)).isGrid, layout == '2열 그리드');
      final title =
          find.descendant(of: card(1), matching: find.text('공유 이미지 이름'));
      final badge =
          find.descendant(of: card(1), matching: find.byType(SharedBadge));
      expect((tester.getCenter(title).dy - tester.getCenter(badge).dy).abs(),
          lessThan(1));
      expect(
          tester.getCenter(badge).dx, greaterThan(tester.getCenter(title).dx));
      expect(tester.takeException(), isNull);
    }
    final navigation = tester.getRect(find.byType(NavigationBar));
    expect(navigation.bottom, 892);
    // NavigationBar includes its own SafeArea above the actual Android inset.
    expect(tester.getCenter(find.text('홈')).dy, lessThan(892 - 48));
  });

  testWidgets(
      'edit changes name and tags together; cancel and blank names do not write',
      (tester) async {
    await show(tester, home());
    await edit(tester, 0);
    await tester.enterText(find.byType(TextFormField), '   ');
    await tester.tap(find.text('저장'));
    await tester.pumpAndSettle();
    expect(find.text('이미지 이름을 입력해 주세요.'), findsOneWidget);
    expect(localWrites, 0);
    await tester.tap(find.text('취소'));
    await tester.pumpAndSettle();
    expect(localWrites, 0);
    await edit(tester, 0);
    await tester.enterText(find.byType(TextFormField), '  새 이미지 이름  ');
    await tester.tap(find.widgetWithText(FilterChip, 'SF'));
    await tester.tap(find.text('저장'));
    await tester.pumpAndSettle();
    expect(images[0]['title'], '새 이미지 이름');
    expect((images[0]['tags'] as List).toSet(), {'풍경', 'SF'});
    expect(localWrites, 1);
    expect(tester.takeException(), isNull);
  });

  testWidgets('failed shared edit preserves local name and tags',
      (tester) async {
    api.failEdit = true;
    await show(tester, home());
    await edit(tester, 1);
    await tester.enterText(find.byType(TextFormField), '저장되면 안 되는 이름');
    await tester.tap(find.widgetWithText(FilterChip, 'SF'));
    await tester.tap(find.text('저장'));
    await tester.pumpAndSettle();
    expect(localWrites, 0);
    expect(images[1]['title'], '공유 이미지 이름');
    expect(images[1]['tags'], ['풍경']);
    expect(tester.takeException(), isNull);
  });

  testWidgets(
      'editor switches directional ratios and keeps conversion buttons pending',
      (tester) async {
    await show(tester, ImageEditorPage(filePath: imagePath));
    expect(find.text('홈 화면에 적용'), findsNothing);
    for (final portrait in [true, false, true]) {
      final orientation = find.byTooltip(portrait ? '세로 방향' : '가로 방향');
      await tester.ensureVisible(orientation);
      await tester.tap(orientation);
      await tester.pumpAndSettle();
      expect(find.text(portrait ? '9:16' : '16:9'), findsOneWidget);
      expect(find.text(portrait ? '3:4' : '4:3'), findsOneWidget);
      expect(find.text(portrait ? '16:9' : '9:16'), findsNothing);
    }
    for (final feature in ['업스케일', '2.5D 변환']) {
      final button = find.widgetWithText(OutlinedButton, feature);
      await tester.ensureVisible(button);
      await tester.tap(button);
      await tester.pumpAndSettle();
      expect(find.text('$feature 기능은 준비 중입니다.'), findsOneWidget);
    }
    final filters = find.widgetWithText(TextButton, '필터');
    await tester.ensureVisible(filters);
    await tester.tap(filters);
    await tester.pumpAndSettle();
    final monochrome = find.byKey(const ValueKey(PhotoFilter.monochrome));
    await tester.ensureVisible(monochrome);
    await tester.tap(monochrome);
    await tester.pumpAndSettle();
    expect(find.text('선명'), findsOneWidget);
    expect(find.text('차분'), findsOneWidget);
    expect(find.text('세피아'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('registration returns an edited copy with name and fallback tags',
      (tester) async {
    ImageRegistrationOptions? result;
    Future<void> settleFileWork(Finder ready) async {
      await tester.runAsync(() async {
        for (var attempt = 0; attempt < 200; attempt++) {
          await Future<void>.delayed(const Duration(milliseconds: 10));
          await tester.pump();
          if (ready.evaluate().isNotEmpty) break;
        }
      });
      expect(ready, findsOneWidget);
      await tester.pumpAndSettle();
    }

    await show(
        tester,
        Scaffold(
            body: Builder(
                builder: (context) => TextButton(
                    onPressed: () async {
                      result = await Navigator.push<ImageRegistrationOptions>(
                          context,
                          MaterialPageRoute(
                              builder: (_) => ImageFeaturePage(
                                  filePath: imagePath, title: '원본 이름')));
                    },
                    child: const Text('등록 열기')))));
    await tester.tap(find.text('등록 열기'));
    await settleFileWork(
        find.byKey(const ValueKey('registration-original-image')));
    expect(find.text(unspecifiedGalleryTag), findsNothing);
    await tester.enterText(find.byType(TextFormField), '  편집한 이미지  ');
    final openEditor = find.widgetWithText(FilledButton, '업스케일링');
    await tester.ensureVisible(openEditor);
    await tester.tap(openEditor);
    await settleFileWork(find.byKey(const ValueKey('image-editor-canvas')));
    final standard = find.byKey(const ValueKey(CropPreset.standard));
    await tester.ensureVisible(standard);
    await tester.tap(standard);
    await tester.pumpAndSettle();
    final filters = find.widgetWithText(TextButton, '필터');
    await tester.ensureVisible(filters);
    await tester.tap(filters);
    await tester.pumpAndSettle();
    final mono = find.byKey(const ValueKey(PhotoFilter.monochrome));
    await tester.ensureVisible(mono);
    await tester.tap(mono);
    final finish = find.widgetWithText(FilledButton, '완료하고 등록');
    await tester.ensureVisible(finish);
    await tester.tap(finish);
    await settleFileWork(find.text('등록 열기'));
    expect(result!.title, '편집한 이미지');
    expect(result!.tags, [unspecifiedGalleryTag]);
    final edited = result!.editedFile!;
    expect(edited.width, 30);
    expect(edited.height, 40);
    await tester.runAsync(() async {
      final codec =
          await ui.instantiateImageCodec(File(edited.path).readAsBytesSync());
      final image = (await codec.getNextFrame()).image;
      final pixel =
          (await image.toByteData(format: ui.ImageByteFormat.rawRgba))!
              .buffer
              .asUint8List();
      expect((pixel[0] - pixel[1]).abs(), lessThanOrEqualTo(1));
      expect((pixel[1] - pixel[2]).abs(), lessThanOrEqualTo(1));
      image.dispose();
      codec.dispose();
      await edited.dispose();
    });
    expect(File(imagePath).existsSync(), isTrue);
    expect(tester.takeException(), isNull);
  });

  final pages = <String, Widget Function()>{
    'login': () => LoginPage(apiClient: api),
    'find ID': () => FindIdPage(apiClient: api),
    'password reset': () => PasswordResetPage(apiClient: api),
    'sign up': () => SignUpPage(apiClient: api),
    'email verification': () =>
        EmailVerificationPage(apiClient: api, email: 'artist@example.test'),
    'image registration': () =>
        ImageFeaturePage(filePath: imagePath, title: '이미지'),
    'image editor': () => ImageEditorPage(filePath: imagePath),
    'wallpaper preview': () =>
        WallpaperPreviewPage(filePath: imagePath, finishLabel: '완료'),
    'project preview': () => PreviewPage(
        apiClient: api,
        project: const LiveLayerProject(
          id: 'project-id',
          title: '프로젝트',
          status: 'Uploaded',
          originalImageUrl: '',
          layers: [],
        )),
    'home': home,
    'search': () =>
        Scaffold(body: SafeArea(child: GallerySearchPage(apiClient: api))),
  };
  for (final entry in pages.entries) {
    for (final size in [const Size(320, 640), const Size(844, 390)]) {
      testWidgets('${entry.key} fits $size with large text and keyboard',
          (tester) async {
        await show(tester, entry.value(),
            size: size, scale: 1.5, keyboard: 160);
        expect(tester.takeException(), isNull);
      });
    }
  }
  testWidgets('small home supports both layouts, the editor, and settings',
      (tester) async {
    await show(tester, home(), size: const Size(320, 640), scale: 1.6);
    await tester.tap(find.byTooltip('1열 목록'));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    await edit(tester, 1);
    expect(find.text('이미지 수정'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.tap(find.text('취소'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('설정'));
    await tester.pumpAndSettle();
    expect(find.text('홈 배경화면 제거'), findsOneWidget);
    expect(find.text('로그아웃'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
