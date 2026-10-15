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
import 'package:live_layer/models/image_ratio.dart';
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
  late Map<String, Object?> libraryOrder;
  late String? previewedId;
  Size? detectedDisplay;
  Map<Object?, Object?>? appliedWallpaper;
  bool nativePreviewAvailable = true;
  var imports = 0;
  var failWallpaperApply = false;
  Map<Object?, Object?>? appliedLocalWallpaper;
  const pickerChannel = MethodChannel('plugins.flutter.io/image_picker');
  const wallpaperChannel = MethodChannel('live_layer/wallpaper');

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
    libraryOrder = {'sort': 'manual', 'ids': <String>[]};
    previewedId = null;
    detectedDisplay = null;
    appliedWallpaper = null;
    nativePreviewAvailable = true;
    imports = 0;
    failWallpaperApply = false;
    appliedLocalWallpaper = null;
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(pickerChannel, (call) async {
      expect(call.method, 'pickImage');
      return imagePath;
    });
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(wallpaperChannel, (call) async {
      if (call.method == 'getDisplaySize') {
        return detectedDisplay == null
            ? null
            : {
                'width': detectedDisplay!.width,
                'height': detectedDisplay!.height,
              };
      }
      if (call.method == 'applyLocalStaticWallpaper') {
        appliedWallpaper = Map<Object?, Object?>.from(call.arguments as Map);
        return null;
      }
      throw StateError('Unexpected wallpaper method: ${call.method}');
    });
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
      if (call.method == 'importLocalImage') {
        imports++;
        final args = call.arguments as Map;
        final saved = <String, Object?>{
          'id': 'saved-image',
          'path': args['sourcePath'],
          'title': args['title'],
          'tags': args['tags'],
          'width': args['width'],
          'height': args['height'],
          'mode': args['mode'],
          'ratio': args['ratio'],
          'createdAt': '2026-10-15T00:00:00Z',
        };
        images.add(saved);
        return saved;
      }
      if (call.method == 'applyLocalWallpaper') {
        expect(imports, 1);
        appliedLocalWallpaper =
            Map<Object?, Object?>.from(call.arguments as Map);
        if (failWallpaperApply) throw PlatformException(code: 'APPLY_FAILED');
        return null;
      }
      if (call.method == 'getLibraryOrder') return libraryOrder;
      if (call.method == 'saveLibraryOrder') {
        libraryOrder = Map<String, Object?>.from(call.arguments as Map)
          ..remove('scope');
        return null;
      }
      if (call.method == 'previewLocalWallpaper') {
        previewedId = (call.arguments as Map)['id'] as String;
        return nativePreviewAvailable;
      }
      if (call.method == 'updateLocalImageDetails') {
        localWrites++;
        final args = call.arguments as Map;
        final image = images.singleWhere((item) => item['id'] == args['id']);
        image['title'] = args['title'];
        image['tags'] = args['tags'];
        image['tag'] = (args['tags'] as List).first;
        if (args['ratio'] != null) image['ratio'] = args['ratio'];
        return null;
      }
      throw StateError('Unexpected method: ${call.method}');
    });
  });
  tearDown(() => TestDefaultBinaryMessengerBinding
      .instance.defaultBinaryMessenger
      .setMockMethodCallHandler(pickerChannel, null));
  tearDown(() => TestDefaultBinaryMessengerBinding
      .instance.defaultBinaryMessenger
      .setMockMethodCallHandler(channel, null));
  tearDown(() => TestDefaultBinaryMessengerBinding
      .instance.defaultBinaryMessenger
      .setMockMethodCallHandler(wallpaperChannel, null));

  Future<void> settleFileWork(WidgetTester tester, Finder ready) async {
    await tester.runAsync(() async {
      for (var attempt = 0; attempt < 200; attempt++) {
        await Future<void>.delayed(const Duration(milliseconds: 10));
        await tester.pump(const Duration(milliseconds: 16));
        if (ready.evaluate().isNotEmpty) break;
      }
    });
    expect(ready, findsWidgets,
        reason:
            'Visible labels: ${tester.allWidgets.whereType<Text>().map((text) => text.data).toList()}');
    await tester.pumpAndSettle();
    expect(ready, findsOneWidget);
  }

  Future<void> selectManual(WidgetTester tester) async {
    final manual = find.text('수동');
    await tester.ensureVisible(manual);
    await tester.tap(manual);
    await tester.pumpAndSettle();
  }

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
    if (page is ImageFeaturePage ||
        page is ImageEditorPage ||
        page is WallpaperPreviewPage) {
      // The registration page decodes a real file outside Flutter's fake clock.
      final loadedImage = find.byKey(ValueKey(page is ImageFeaturePage
          ? 'registration-original-image'
          : page is ImageEditorPage
              ? 'image-editor-canvas'
              : 'wallpaper-preview-image'));
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
      'sort then long press preserves displayed order and persists drag across home recreation',
      (tester) async {
    images[0]['createdAt'] = '2026-10-12T00:00:00Z';
    await show(tester, home());
    expect(find.text('내 이미지 (2)'), findsOneWidget);
    await tester.tap(find.byTooltip('이미지 정렬'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('오래된순'));
    await tester.pumpAndSettle();
    expect(
        tester.getTopLeft(card(0)).dy, lessThan(tester.getTopLeft(card(1)).dy));
    final gesture = await tester.startGesture(tester.getCenter(card(0)));
    await tester.pump(const Duration(milliseconds: 600));
    expect(libraryOrder['sort'], 'manual');
    expect(libraryOrder['ids'], ['image-0', 'image-1']);
    await gesture.moveTo(
        tester.getCenter(find.byKey(const ValueKey('reorder-image-1'))));
    await tester.pump();
    await gesture.up();
    await tester.pumpAndSettle();
    expect(libraryOrder['ids'], ['image-1', 'image-0']);
    await tester.pumpWidget(const SizedBox());
    await show(tester, home());
    expect(find.text('사용자순'), findsOneWidget);
    expect(
        tester.getTopLeft(card(1)).dy, lessThan(tester.getTopLeft(card(0)).dy));
    expect(tester.takeException(), isNull);
  });

  testWidgets(
      'registered image opens preview with saved manual ratio and applies it',
      (tester) async {
    nativePreviewAvailable = false;
    images[0]['ratio'] = const ImageRatioSettings(
            automatic: false, preset: CropPreset.standard, portrait: false)
        .toMap();
    await show(tester, home());
    await tester.tap(card(0));
    await settleFileWork(
        tester, find.byKey(const ValueKey('wallpaper-preview-image')));
    expect(find.text('배경화면 미리보기'), findsOneWidget);
    expect(find.text('4:3'), findsOneWidget);
    final aspect = tester.widget<AspectRatio>(find.ancestor(
        of: find.byKey(const ValueKey('wallpaper-preview-image')),
        matching: find.byType(AspectRatio)));
    expect(aspect.aspectRatio, 4 / 3);
    await tester.ensureVisible(find.text('홈 화면에 적용'));
    await tester.tap(find.text('홈 화면에 적용'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(ListTile, '홈 화면에 적용'));
    await tester.pumpAndSettle();
    expect(appliedWallpaper!['sourcePath'], imagePath);
    expect((appliedWallpaper!['ratio'] as Map)['mode'], 'manual');
    expect((appliedWallpaper!['ratio'] as Map)['preset'], 'standard');
    expect(appliedWallpaper!['target'], 'home');
    expect(previewedId, 'image-0');
    expect(find.text('메인으로 돌아가기'), findsNothing);
  });

  testWidgets(
      'registered image retains the Android launcher preview when available',
      (tester) async {
    await show(tester, home());
    await tester.tap(card(0));
    await tester.pumpAndSettle();
    expect(previewedId, 'image-0');
    expect(find.text('배경화면 미리보기'), findsNothing);
  });

  testWidgets('automatic preview uses full native display and follows rotation',
      (tester) async {
    detectedDisplay = const Size(1080, 2400);
    await show(tester, WallpaperPreviewPage(filePath: imagePath),
        size: const Size(320, 640), keyboard: 160);
    double previewAspect() => tester
        .widget<AspectRatio>(find.ancestor(
            of: find.byKey(const ValueKey('wallpaper-preview-image')),
            matching: find.byType(AspectRatio)))
        .aspectRatio;
    expect(previewAspect(), 1080 / 2400);
    expect(find.text('맞춤 형태'), findsNothing);
    expect(find.byTooltip('세로 방향'), findsNothing);
    detectedDisplay = const Size(2400, 1080);
    tester.view.physicalSize = const Size(640, 320);
    await tester.pumpAndSettle();
    expect(previewAspect(), 2400 / 1080);
    await tester.ensureVisible(find.text('홈 화면에 적용'));
    await tester.tap(find.text('홈 화면에 적용'));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.widgetWithText(ListTile, '잠금 화면에 적용'));
    await tester.tap(find.widgetWithText(ListTile, '잠금 화면에 적용'));
    await tester.pumpAndSettle();
    expect((appliedWallpaper!['ratio'] as Map)['mode'], 'automatic');
    expect(appliedWallpaper!['target'], 'lock');
    expect(tester.takeException(), isNull);
  });

  testWidgets('automatic ratio is sent with each selected wallpaper target',
      (tester) async {
    await show(tester, WallpaperPreviewPage(filePath: imagePath));
    for (final entry in {
      'home': '홈 화면에 적용',
      'lock': '잠금 화면에 적용',
      'both': '전체 적용',
    }.entries) {
      final apply = find.byType(FilledButton);
      await tester.ensureVisible(apply);
      await tester.tap(apply);
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(ListTile, entry.value));
      await tester.pumpAndSettle();
      expect(appliedWallpaper!['target'], entry.key);
      expect((appliedWallpaper!['ratio'] as Map)['mode'], 'automatic');
    }
    expect(tester.takeException(), isNull);
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
    await selectManual(tester);
    final square = find.byKey(const ValueKey(CropPreset.square));
    await tester.ensureVisible(square);
    await tester.tap(square);
    await tester.tap(find.widgetWithText(FilterChip, 'SF'));
    await tester.tap(find.text('저장'));
    await tester.pumpAndSettle();
    expect(images[0]['title'], '새 이미지 이름');
    expect((images[0]['tags'] as List).toSet(), {'풍경', 'SF'});
    expect(localWrites, 1);
    expect((images[0]['ratio'] as Map)['mode'], 'manual');
    expect((images[0]['ratio'] as Map)['preset'], 'square');
    await edit(tester, 0);
    expect(
        tester
            .widget<SegmentedButton<bool>>(find.byType(SegmentedButton<bool>))
            .selected,
        {false});
    await tester.tap(find.text('취소'));
    await tester.pumpAndSettle();
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
    expect(find.text('맞춤 형태'), findsNothing);
    await selectManual(tester);
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
    final openEditor = find.byKey(const ValueKey('registration-tool-crop'));
    await tester.ensureVisible(openEditor);
    await tester.tap(openEditor);
    await tester.pumpAndSettle();
    await selectManual(tester);
    final standard = find.byKey(const ValueKey(CropPreset.standard));
    await tester.ensureVisible(standard);
    await tester.tap(standard);
    await tester.pumpAndSettle();
    final filters = find.byKey(const ValueKey('registration-tool-filter'));
    await tester.ensureVisible(filters);
    await tester.tap(filters);
    await tester.pumpAndSettle();
    final mono = find.byKey(const ValueKey(PhotoFilter.monochrome));
    await tester.ensureVisible(mono);
    await tester.tap(mono);
    final finish = find.byKey(const ValueKey('save-registration'));
    await tester.ensureVisible(finish);
    await tester.tap(finish);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('wallpaper-home')));
    await tester.tap(find.byKey(const ValueKey('confirm-image-save')));
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

  testWidgets(
      'save preserves untouched bytes and exports an explicitly selected crop',
      (tester) async {
    final original = File(imagePath).readAsBytesSync();
    for (final automatic in [true, false]) {
      ImageRegistrationOptions? result;
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
          tester, find.byKey(const ValueKey('registration-original-image')));
      expect(find.text('맞춤 형태'), findsNothing);
      if (!automatic) {
        await tester.tap(find.byKey(const ValueKey('registration-tool-crop')));
        await tester.pumpAndSettle();
        await selectManual(tester);
        final standard = find.byKey(const ValueKey(CropPreset.standard));
        await tester.ensureVisible(standard);
        await tester.tap(standard);
        await tester.pumpAndSettle();
      }
      final register = find.byKey(const ValueKey('save-registration'));
      await tester.ensureVisible(register);
      await tester.tap(register);
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const ValueKey('confirm-image-save')));
      await settleFileWork(tester, find.text('등록 열기'));
      expect(result!.ratio.automatic, automatic);
      expect(result!.ratio.preset,
          automatic ? CropPreset.original : CropPreset.standard);
      expect(result!.wallpaperTarget, isNull);
      if (automatic) {
        expect(result!.editedFile, isNull);
      } else {
        expect(result!.editedFile!.width, 30);
        expect(result!.editedFile!.height, 40);
        await tester.runAsync(() => result!.editedFile!.dispose());
      }
      expect(File(imagePath).readAsBytesSync(), original);
      expect(tester.takeException(), isNull);
    }
  });

  testWidgets('automatic editor exports the full device ratio', (tester) async {
    detectedDisplay = const Size(1080, 2400);
    ImageEditorResult? result;
    await show(
        tester,
        Scaffold(
            body: Builder(
                builder: (context) => TextButton(
                    onPressed: () async {
                      result = await Navigator.push<ImageEditorResult>(
                          context,
                          MaterialPageRoute(
                              builder: (_) =>
                                  ImageEditorPage(filePath: imagePath)));
                    },
                    child: const Text('편집 열기')))));
    await tester.tap(find.text('편집 열기'));
    await settleFileWork(
        tester, find.byKey(const ValueKey('image-editor-canvas')));
    expect(find.byKey(const ValueKey(CropPreset.wide)), findsNothing);
    final finish = find.widgetWithText(FilledButton, '완료하고 등록');
    await tester.ensureVisible(finish);
    await tester.tap(finish);
    await settleFileWork(tester, find.text('편집 열기'));
    expect(result!.ratio.automatic, isTrue);
    expect(result!.editedFile!.width, 18);
    expect(result!.editedFile!.height, 40);
    await tester.runAsync(() => result!.editedFile!.dispose());
    expect(File(imagePath).existsSync(), isTrue);
    expect(tester.takeException(), isNull);
  });

  for (final screens in <String, List<String>>{
    'none': [],
    'home': ['home'],
    'lock': ['lock'],
    'both': ['home', 'lock'],
  }.entries) {
    testWidgets('registration imports before applying ${screens.key}',
        (tester) async {
      await show(tester, home());
      await tester.tap(find.byType(NewImageCard));
      await settleFileWork(
          tester, find.byKey(const ValueKey('registration-original-image')));
      expect(find.text('화면 적용'), findsNothing);
      for (final tool in ['upscale', 'parallax']) {
        expect(
            tester
                .widget<TextButton>(
                    find.byKey(ValueKey('registration-tool-$tool')))
                .onPressed,
            isNull);
      }
      await tester.tap(find.byKey(const ValueKey('save-registration')));
      await tester.pumpAndSettle();
      for (final screen in screens.value) {
        await tester.tap(find.byKey(ValueKey('wallpaper-$screen')));
        await tester.pumpAndSettle();
      }
      await tester.tap(find.byKey(const ValueKey('confirm-image-save')));
      await settleFileWork(
          tester,
          find.text(screens.key == 'none'
              ? '이미지를 저장했습니다. 배경화면은 변경하지 않았습니다.'
              : '이미지를 저장하고 배경화면을 적용했습니다.'));
      expect(imports, 1);
      expect(images.last['ratio'], const ImageRatioSettings().toMap());
      if (screens.key == 'none') {
        expect(appliedLocalWallpaper, isNull);
      } else {
        expect(appliedLocalWallpaper!['target'], screens.key);
        expect(appliedLocalWallpaper!['id'], 'saved-image');
        expect(appliedLocalWallpaper!['scope'], api.storageScope);
      }
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets('cancel keeps edits and apply failure preserves one saved image',
      (tester) async {
    failWallpaperApply = true;
    await show(tester, home());
    await tester.tap(find.byType(NewImageCard));
    await settleFileWork(
        tester, find.byKey(const ValueKey('registration-original-image')));
    await tester.enterText(find.byType(TextFormField), '유지할 이름');
    await tester.tap(find.byKey(const ValueKey('save-registration')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('취소'));
    await tester.pumpAndSettle();
    expect(imports, 0);
    expect(appliedLocalWallpaper, isNull);
    expect(
        tester
            .widget<TextFormField>(find.byType(TextFormField))
            .controller!
            .text,
        '유지할 이름');
    await tester.tap(find.byKey(const ValueKey('save-registration')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('wallpaper-home')));
    await tester.tap(find.byKey(const ValueKey('confirm-image-save')));
    await settleFileWork(
        tester, find.textContaining('이미지는 저장했지만 배경화면을 적용하지 못했습니다.'));
    expect(imports, 1);
    expect(images.last['title'], '유지할 이름');
    expect(find.textContaining('이미지는 저장했지만 배경화면을 적용하지 못했습니다.'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('portrait preview fills width and pans without changing source',
      (tester) async {
    final path = '${temp.path}/portrait.png';
    await tester.runAsync(() async {
      final recorder = ui.PictureRecorder();
      Canvas(recorder).drawColor(AppTheme.primary, BlendMode.src);
      final picture = recorder.endRecording();
      final portrait = await picture.toImage(80, 320);
      final data = await portrait.toByteData(format: ui.ImageByteFormat.png);
      File(path).writeAsBytesSync(data!.buffer.asUint8List());
      portrait.dispose();
      picture.dispose();
    });
    final original = File(path).readAsBytesSync();
    await show(tester, ImageFeaturePage(filePath: path, title: '세로 사진'));
    final finder = find.byKey(const ValueKey('registration-image-viewer'));
    final viewer = tester.widget<InteractiveViewer>(finder);
    final content = tester.getSize(
        find.descendant(of: finder, matching: find.byType(CustomPaint)).first);
    expect(content.width, closeTo(tester.getSize(finder).width, .01));
    expect(content.height / content.width, closeTo(4, .01));
    expect(content.height, greaterThan(tester.getSize(finder).height));
    final gesture = await tester.startGesture(tester.getCenter(finder));
    await tester.pump();
    await gesture.moveBy(const Offset(0, -180));
    await tester.pump();
    await gesture.up();
    await tester.pumpAndSettle();
    expect(
        viewer.transformationController!.value.getTranslation().y, lessThan(0));
    expect(File(path).readAsBytesSync(), original);
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
