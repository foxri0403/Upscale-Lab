import 'dart:io';
import 'dart:ui' as ui;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/live_2d_page.dart';
import 'package:live_layer/image_editor_page.dart';
import 'package:live_layer/models/image_editor.dart';
import 'package:live_layer/models/image_ratio.dart';
import 'package:live_layer/models/local_image.dart';
import 'package:live_layer/services/live_2d_service.dart';
import 'package:live_layer/services/local_image_store.dart';
import 'package:live_layer/services/wallpaper_service.dart';
import 'package:live_layer/unified_home.dart';
import 'package:live_layer/widgets/live_2d_preview.dart';
import 'package:live_layer/widgets/registration_image_canvas.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const wallpaper = MethodChannel('live_layer/wallpaper');
  const storage = MethodChannel('live_layer/storage');
  const tilt = MethodChannel('live_layer/tilt');
  final messenger =
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
  late Directory directory;
  late String source;
  late List<MethodCall> calls;
  late List<String> discarded;
  bool failSplit = false;
  bool failEdit = false;
  int activeFlags = 1;
  bool confirmActivation = true;
  late List<List<int>> exportedPlanes;
  Live2DDraft? selected;

  setUpAll(() async {
    directory = await Directory.systemTemp.createTemp('live2d-test-');
    final recorder = ui.PictureRecorder();
    ui.Canvas(recorder).drawColor(const Color(0xff336699), BlendMode.src);
    final picture = recorder.endRecording();
    final bitmap = await picture.toImage(32, 48);
    final png = await bitmap.toByteData(format: ui.ImageByteFormat.png);
    source = '${directory.path}/image.png';
    await File(source).writeAsBytes(png!.buffer.asUint8List());
    bitmap.dispose();
    picture.dispose();
  });
  tearDownAll(() => directory.delete(recursive: true));

  setUp(() {
    calls = [];
    discarded = [];
    failSplit = false;
    failEdit = false;
    activeFlags = 1;
    confirmActivation = true;
    exportedPlanes = [];
    selected = null;
    messenger.setMockMethodCallHandler(tilt, (_) async => null);
    messenger.setMockMethodCallHandler(wallpaper, (call) async {
      calls.add(call);
      switch (call.method) {
        case 'getActiveLive2DTarget':
          return activeFlags;
        case 'activateLive2DDraft':
          return confirmActivation;
        case 'getDisplaySize':
          return {'width': 360, 'height': 720};
        case 'splitLive2D':
          if (failSplit) {
            throw PlatformException(
                code: 'model_download', message: '모델 다운로드 후 다시 시도해 주세요.');
          }
          return {
            'draftId': 'draft-1',
            'backgroundPath': source,
            'foregroundPath': source,
            'width': 32,
            'height': 48
          };
        case 'editLive2D':
          final args = call.arguments as Map;
          exportedPlanes = [
            await File(args['backgroundPath'] as String).readAsBytes(),
            await File(args['foregroundPath'] as String).readAsBytes(),
          ];
          if (failEdit) {
            throw PlatformException(code: 'disk_full', message: '저장 공간 부족');
          }
          final codec = await ui
              .instantiateImageCodec(Uint8List.fromList(exportedPlanes.first));
          final image = (await codec.getNextFrame()).image;
          final size = [image.width, image.height];
          image.dispose();
          codec.dispose();
          return {
            'draftId': 'edited-draft',
            'backgroundPath': source,
            'foregroundPath': source,
            'width': size[0],
            'height': size[1],
          };
        case 'discardLive2D':
          discarded.add((call.arguments as Map)['draftId'] as String);
          return null;
        default:
          throw StateError('Unexpected wallpaper call ${call.method}');
      }
    });
    messenger.setMockMethodCallHandler(storage, (call) async {
      calls.add(call);
      if (call.method == 'getLocalLive2D') {
        return {'backgroundPath': source, 'foregroundPath': source};
      }
      if (call.method == 'activateLocalLive2D') {
        activeFlags = 3;
        return true;
      }
      if (call.method == 'applyLocalWallpaper' && activeFlags == 0) {
        throw PlatformException(
            code: 'live2d_activation_required', message: 'Live 2D 활성화가 필요합니다.');
      }
      if (call.method == 'attachLocalLive2D' ||
          call.method == 'applyLocalWallpaper') {
        return null;
      }
      throw StateError('Unexpected storage call ${call.method}');
    });
  });
  tearDown(() {
    messenger.setMockMethodCallHandler(wallpaper, null);
    messenger.setMockMethodCallHandler(storage, null);
    messenger.setMockMethodCallHandler(tilt, null);
  });

  Future<void> settleFiles(WidgetTester tester) async {
    await tester.runAsync(() async {
      await tester.pump();
      for (var i = 0; i < 200; i++) {
        await Future<void>.delayed(const Duration(milliseconds: 10));
        await tester.pump();
        if (find.byType(CircularProgressIndicator).evaluate().isEmpty) break;
      }
    });
    await tester.pumpAndSettle();
  }

  Future<void> open(WidgetTester tester, Widget page) async {
    await tester.pumpWidget(MaterialApp(
        home: Builder(
            builder: (context) => Scaffold(
                body: TextButton(
                    onPressed: () async {
                      final result = await Navigator.of(context)
                          .push<Live2DDraft>(
                              MaterialPageRoute(builder: (_) => page));
                      selected = result;
                    },
                    child: const Text('open'))))));
    await tester.tap(find.text('open'));
    await settleFiles(tester);
  }

  testWidgets(
      'model unavailable shows actionable retry and never accepts a fake split',
      (tester) async {
    failSplit = true;
    await open(tester, Live2DPage(sourcePath: source));
    expect(find.text('모델 다운로드 후 다시 시도해 주세요.'), findsOneWidget);
    expect(find.byKey(const ValueKey('use-live2d')), findsNothing);
    failSplit = false;
    await tester.tap(find.text('다시 시도'));
    await settleFiles(tester);
    expect(find.byType(Live2DPreview), findsOneWidget);
    await tester.tap(find.text('배경'));
    await tester.pumpAndSettle();
    expect(
        find.byKey(const ValueKey('live2d-moving-foreground')), findsNothing);
    await tester.tap(find.text('인물·사물'));
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('live2d-fixed-background')), findsNothing);
    await tester.tap(find.byKey(const ValueKey('use-live2d')));
    await tester.pumpAndSettle();
    expect(selected!.id, 'draft-1');
    expect(discarded, isEmpty);
    await Live2DService.discard(selected);
    expect(discarded, ['draft-1']);
  });

  testWidgets('leaving a split preview discards its temporary layers',
      (tester) async {
    await open(tester, Live2DPage(sourcePath: source));
    await tester.pageBack();
    await tester.pumpAndSettle();
    expect(selected, isNull);
    expect(discarded, ['draft-1']);
  });

  testWidgets('sensor tilt moves only foreground and remains bounded',
      (tester) async {
    await tester.pumpWidget(MaterialApp(
        home: SizedBox(
            width: 320,
            height: 480,
            child: Live2DPreview(
                backgroundPath: source, foregroundPath: source))));
    await settleFiles(tester);
    final background = tester
        .widget<Transform>(
            find.byKey(const ValueKey('live2d-fixed-background')))
        .transform
        .clone();
    await messenger.handlePlatformMessage(
        'live_layer/tilt',
        const StandardMethodCodec().encodeSuccessEnvelope([50.0, -50.0]),
        (_) {});
    await tester.pump();
    final foreground = tester
        .widget<Transform>(
            find.byKey(const ValueKey('live2d-moving-foreground')))
        .transform;
    final bounds = tester.getSize(find.byType(Live2DPreview));
    expect(foreground.getTranslation().x, closeTo(-bounds.width * .035, .001));
    expect(foreground.getTranslation().y, closeTo(-bounds.height * .035, .001));
    expect(
        tester
            .widget<Transform>(
                find.byKey(const ValueKey('live2d-fixed-background')))
            .transform,
        background);
    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pumpAndSettle();
  });

  testWidgets(
      'registration Live 2D button opens working separation and preserves save mode',
      (tester) async {
    ImageRegistrationOptions? options;
    await tester.pumpWidget(MaterialApp(
        home: Builder(
            builder: (context) => Scaffold(
                body: TextButton(
                    child: const Text('register'),
                    onPressed: () async {
                      options = await Navigator.of(context)
                          .push<ImageRegistrationOptions>(MaterialPageRoute(
                              builder: (_) => ImageFeaturePage(
                                  filePath: source, title: 'test')));
                    })))));
    await tester.tap(find.text('register'));
    await settleFiles(tester);
    final live = find.byKey(const ValueKey('registration-tool-parallax'));
    await tester.ensureVisible(live);
    expect(tester.widget<TextButton>(live).onPressed, isNotNull);
    expect(find.text('2.5D'), findsNothing);
    await tester.tap(live);
    await settleFiles(tester);
    await tester.tap(find.byKey(const ValueKey('use-live2d')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('save-registration')));
    await settleFiles(tester);
    expect(find.text('Live 2D 저장'), findsNothing);
    expect(options!.live2D!.id, 'draft-1');
    expect(options!.upscaleFactor, isNull);
    expect(options!.wallpaperTarget, isNull);
    expect(discarded, isEmpty);
    await options!.editedFile?.dispose();
    await Live2DService.discard(options!.live2D);
  });

  testWidgets('Live 2D keeps the crop and remains editable through final save',
      (tester) async {
    ImageRegistrationOptions? options;
    await tester.pumpWidget(MaterialApp(home: Builder(builder: (context) {
      return Scaffold(
          body: TextButton(
              onPressed: () async {
                options = await Navigator.of(context)
                    .push<ImageRegistrationOptions>(MaterialPageRoute(
                        builder: (_) =>
                            ImageFeaturePage(filePath: source, title: 'edit')));
              },
              child: const Text('register')));
    })));
    await tester.tap(find.text('register'));
    await settleFiles(tester);
    Future<void> tool(String name) async {
      final button = find.byKey(ValueKey('registration-tool-$name'));
      await tester.ensureVisible(button);
      await tester.tap(button);
      await tester.pumpAndSettle();
    }

    RegistrationImageCanvas canvas() => tester
        .widget<RegistrationImageCanvas>(find.byType(RegistrationImageCanvas));
    double cropAspect(Rect crop) => crop.width * 32 / (crop.height * 48);

    await tool('crop');
    await tester.tap(find.byTooltip('가로 방향'));
    await tester.pumpAndSettle();
    final wide = find.byKey(const ValueKey(CropPreset.wide));
    await tester.ensureVisible(wide);
    await tester.tap(wide);
    await tester.pumpAndSettle();
    final initialCrop = canvas().crop;
    expect(cropAspect(initialCrop), closeTo(16 / 9, .00001));
    await tool('parallax');
    await settleFiles(tester);
    expect(calls.singleWhere((c) => c.method == 'splitLive2D').arguments,
        {'sourcePath': source}); // Split the untouched source once.
    final aspect = tester.widget<AspectRatio>(
        find.byKey(const ValueKey('live2d-preview-aspect')));
    expect(aspect.aspectRatio, closeTo(16 / 9, .00001));
    expect(tester.getSize(find.byType(Live2DPreview)).aspectRatio,
        closeTo(16 / 9, .00001));
    await tester.tap(find.byKey(const ValueKey('use-live2d')));
    await tester.pumpAndSettle();
    expect(canvas().crop, initialCrop);
    expect(tester.getSize(find.byType(Live2DPreview)).aspectRatio,
        closeTo(16 / 9, .00001));

    await tool('crop');
    expect(canvas().crop, initialCrop);
    final square = find.byKey(const ValueKey(CropPreset.square));
    await tester.ensureVisible(square);
    await tester.tap(square);
    await tester.pumpAndSettle();
    final cropCanvas = find.byKey(const ValueKey('registration-crop-canvas'));
    await tester.timedDrag(
        cropCanvas, const Offset(0, -24), const Duration(milliseconds: 300));
    await tester.pumpAndSettle();
    final finalCrop = canvas().crop;
    expect(cropAspect(finalCrop), closeTo(1, .00001));
    await tool('filter');
    final monochrome = find.byKey(const ValueKey(PhotoFilter.monochrome));
    await tester.ensureVisible(monochrome);
    await tester.tap(monochrome);
    await tester.pumpAndSettle();
    var preview = tester.widget<Live2DPreview>(find.byType(Live2DPreview));
    expect(preview.filter, PhotoFilter.monochrome);
    expect(preview.crop, finalCrop);
    await tool('information');
    await tool('crop');
    expect(canvas().crop, finalCrop);
    await tool('parallax');
    expect(calls.where((c) => c.method == 'splitLive2D').length, 1);
    preview = tester.widget<Live2DPreview>(find.byType(Live2DPreview));
    expect(preview.crop, finalCrop);
    expect(preview.filter, PhotoFilter.monochrome);
    expect(discarded, isEmpty);

    // A failed layer export must retain the editable source for a retry.
    failEdit = true;
    await tester.tap(find.byKey(const ValueKey('save-registration')));
    await settleFiles(tester);
    expect(options, isNull);
    expect(discarded, isEmpty);
    expect(find.byType(Live2DPreview), findsOneWidget);
    failEdit = false;
    await tester.pump(const Duration(seconds: 5));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('save-registration')));
    await settleFiles(tester);
    expect(options!.live2D!.id, 'edited-draft');
    expect(options!.ratio.preset, CropPreset.square);
    expect(options!.ratio.automatic, isFalse);
    expect(options!.live2D!.width, options!.editedFile!.width);
    expect(options!.live2D!.height, options!.editedFile!.height);
    expect(discarded, ['draft-1']);
    final temporary =
        calls.lastWhere((c) => c.method == 'editLive2D').arguments as Map;
    for (final key in ['backgroundPath', 'foregroundPath']) {
      expect(
          await tester.runAsync(() => File(temporary[key] as String).exists()),
          isFalse);
    }
    for (final plane in exportedPlanes) {
      final codec = await tester
          .runAsync(() => ui.instantiateImageCodec(Uint8List.fromList(plane)));
      final image = (await tester.runAsync(() => codec!.getNextFrame()))!.image;
      expect(image.width, image.height);
      final data = await tester
          .runAsync(() => image.toByteData(format: ui.ImageByteFormat.rawRgba));
      final pixel = data!.buffer.asUint8List();
      expect((pixel[0] - pixel[1]).abs(), lessThanOrEqualTo(1));
      expect((pixel[1] - pixel[2]).abs(), lessThanOrEqualTo(1));
      image.dispose();
      codec!.dispose();
    }
    await tester.runAsync(() => options!.editedFile!.dispose());
    await Live2DService.discard(options!.live2D);
  });

  testWidgets('legacy editor retains Live 2D across ratio and filter editing',
      (tester) async {
    ImageEditorResult? result;
    await tester.pumpWidget(MaterialApp(home: Builder(builder: (context) {
      return Scaffold(
          body: TextButton(
              onPressed: () async {
                result = await Navigator.of(context).push<ImageEditorResult>(
                    MaterialPageRoute(
                        builder: (_) => ImageEditorPage(
                            filePath: source,
                            initialRatio: const ImageRatioSettings(
                                automatic: false, preset: CropPreset.square))));
              },
              child: const Text('edit')));
    })));
    await tester.tap(find.text('edit'));
    await settleFiles(tester);
    await tester.tap(find.text('Live 2D'));
    await settleFiles(tester);
    expect(tester.getSize(find.byType(Live2DPreview)).aspectRatio,
        closeTo(1, .00001));
    await tester.tap(find.byKey(const ValueKey('use-live2d')));
    await tester.pumpAndSettle();
    final crop = find.widgetWithText(TextButton, '자르기');
    await tester.ensureVisible(crop);
    await tester.tap(crop);
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('가로 방향'));
    await tester.pumpAndSettle();
    final wide = find.byKey(const ValueKey(CropPreset.wide));
    await tester.ensureVisible(wide);
    await tester.tap(wide);
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(TextButton, '필터'));
    await tester.pumpAndSettle();
    final sepia = find.byKey(const ValueKey(PhotoFilter.sepia));
    await tester.ensureVisible(sepia);
    await tester.tap(sepia);
    await tester.pumpAndSettle();
    final preview = tester.widget<Live2DPreview>(find.byType(Live2DPreview));
    expect(preview.filter, PhotoFilter.sepia);
    expect(preview.crop.width * 32 / (preview.crop.height * 48),
        closeTo(16 / 9, .00001));
    expect(discarded, isEmpty);
    final finish = find.text('완료하고 등록');
    await tester.ensureVisible(finish);
    await tester.tap(finish);
    await settleFiles(tester);
    expect(result!.live2D!.id, 'edited-draft');
    expect(result!.ratio.preset, CropPreset.wide);
    expect(result!.ratio.portrait, isFalse);
    expect(discarded, ['draft-1']);
    await tester.runAsync(() => result!.editedFile!.dispose());
    await Live2DService.discard(result!.live2D);
  });

  for (final confirmed in [false, true]) {
    testWidgets(
        'Live 2D activation is explicit and handles confirmation $confirmed',
        (tester) async {
      confirmActivation = confirmed;
      tester.view.physicalSize = const Size(320, 640);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await open(
          tester,
          MediaQuery(
              data: const MediaQueryData(textScaler: TextScaler.linear(1.6)),
              child: Live2DPage(
                  sourcePath: source,
                  crop: const Rect.fromLTWH(.25, 0, .5, 1),
                  filter: PhotoFilter.sepia)));
      expect(calls.any((c) => c.method == 'activateLive2DDraft'), isFalse);
      final activate = find.byKey(const ValueKey('activate-live2d'));
      await tester.ensureVisible(activate);
      await tester.pumpAndSettle();
      await tester.tap(activate);
      await settleFiles(tester);
      final request =
          calls.singleWhere((c) => c.method == 'activateLive2DDraft');
      expect(request.arguments, {'draftId': 'edited-draft'});
      expect(discarded, ['edited-draft']);
      expect(selected, isNull);
      expect(find.text('Live 2D를 활성화했습니다. 이후 같은 화면에는 미리보기 없이 적용됩니다.'),
          confirmed ? findsOneWidget : findsNothing);
      expect(tester.takeException(), isNull);
      await tester.pageBack();
      await tester.pumpAndSettle();
      expect(discarded, ['edited-draft', 'draft-1']);
    });
  }

  testWidgets(
      'inactive Apply never launches a picker and activation is a separate action',
      (tester) async {
    activeFlags = 0;
    final image = LocalImageItem(
        id: 'image-1',
        title: 'test',
        path: source,
        width: 32,
        height: 48,
        mode: 'live2d',
        tag: 'test',
        createdAt: DateTime.utc(2026));
    const store =
        LocalImageStore(scope: 'server\naccount-1', ownerId: 'account-1');
    await tester.pumpWidget(MaterialApp(
        home: HomeWallpaperPreviewPage(
            image: image,
            store: store,
            onEdit: () {},
            onShare: () {},
            onDelete: () {})));
    await settleFiles(tester);
    await tester.tap(find.text('배경화면 설정'));
    await tester.pumpAndSettle();
    expect(calls.any((c) => c.method == 'activateLocalLive2D'), isFalse);
    expect(find.text('배경화면을 적용했습니다.'), findsNothing);
    expect(find.text('Live 2D 활성화가 필요합니다.'), findsOneWidget);
    final activate = find.byKey(const ValueKey('activate-saved-live2d'));
    await tester.ensureVisible(activate);
    await tester.pumpAndSettle();
    await tester.tap(activate);
    await tester.pumpAndSettle();
    expect(
        calls.singleWhere((c) => c.method == 'activateLocalLive2D').arguments, {
      'scope': 'server\naccount-1',
      'ownerId': 'account-1',
      'id': 'image-1'
    });
    expect(
        tester
            .widget<DropdownButton<WallpaperTarget>>(
                find.byType(DropdownButton<WallpaperTarget>))
            .value,
        WallpaperTarget.both);
    await tester.pump(const Duration(seconds: 10));
    await tester.pumpAndSettle();
    await tester.tap(find.text('배경화면 설정'));
    await tester.pumpAndSettle();
    expect(
        (calls.lastWhere((c) => c.method == 'applyLocalWallpaper').arguments
            as Map)['target'],
        'both');
    expect(calls.where((c) => c.method == 'activateLocalLive2D').length, 1);
    expect(find.text('배경화면을 적용했습니다.'), findsOneWidget);
    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pumpAndSettle();
  });

  testWidgets(
      'saved Live 2D applies through the normal account and target flow',
      (tester) async {
    final image = LocalImageItem(
        id: 'image-1',
        title: 'test',
        path: source,
        width: 32,
        height: 48,
        mode: 'live2d',
        tag: 'test',
        createdAt: DateTime.utc(2026));
    const store =
        LocalImageStore(scope: 'server\naccount-1', ownerId: 'account-1');
    await tester.pumpWidget(MaterialApp(
        home: HomeWallpaperPreviewPage(
            image: image,
            store: store,
            onEdit: () {},
            onShare: () {},
            onDelete: () {})));
    await settleFiles(tester);
    expect(find.byType(Live2DPreview), findsOneWidget);
    expect(find.byType(DropdownButton<WallpaperTarget>), findsOneWidget);
    await tester.tap(find.text('배경화면 설정'));
    await tester.pumpAndSettle();
    final apply =
        calls.singleWhere((call) => call.method == 'applyLocalWallpaper');
    expect(apply.arguments, {
      'scope': 'server\naccount-1',
      'ownerId': 'account-1',
      'id': 'image-1',
      'target': 'home',
    });
    expect(calls.any((call) => call.method == 'activateLocalLive2D'), isFalse);
    expect(
        calls.any((call) => call.method == 'previewLocalWallpaper'), isFalse);
    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pumpAndSettle();
  });
}
