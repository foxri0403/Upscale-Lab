import 'dart:io';
import 'dart:async';
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
import 'package:live_layer/services/api_client.dart';
import 'package:live_layer/models/live_layer_models.dart';
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
  bool failActivation = false;
  int? pickedFlags;
  Completer<bool>? pendingActivation;
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
    failActivation = false;
    pickedFlags = null;
    pendingActivation = null;
    exportedPlanes = [];
    selected = null;
    messenger.setMockMethodCallHandler(tilt, (_) async => null);
    messenger.setMockMethodCallHandler(wallpaper, (call) async {
      calls.add(call);
      switch (call.method) {
        case 'getActiveLive2DTarget':
          return activeFlags;
        case 'setLive2DMotion':
          return null;
        case 'activateLive2DDraft':
          expect((call.arguments as Map)['showSettings'], isTrue);
          if (failActivation) {
            throw PlatformException(code: 'live2d_failed', message: '연결 실패');
          }
          final confirmed = await (pendingActivation?.future ??
              Future.value(confirmActivation));
          if (confirmed) {
            activeFlags = pickedFlags ?? (activeFlags == 0 ? 3 : activeFlags);
          }
          return confirmed;
        case 'cloneLive2DDraft':
          return {
            'draftId': 'cloned-draft',
            'backgroundPath': source,
            'foregroundPath': source,
            'width': 32,
            'height': 48
          };
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
        expect((call.arguments as Map)['showSettings'], isTrue);
        if (confirmActivation) activeFlags = pickedFlags ?? 1;
        return confirmActivation;
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
    await open(
        tester, Live2DPage(apiClient: CloudTestApi(), sourcePath: source));
    expect(find.text('모델 다운로드 후 다시 시도해 주세요.'), findsOneWidget);
    expect(find.byKey(const ValueKey('activate-live2d')), findsNothing);
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
    await tester.tap(find.byKey(const ValueKey('activate-live2d')));
    await settleFiles(tester);
    expect(calls.where((c) => c.method == 'activateLive2DDraft'), hasLength(1));
    expect(calls.where((c) => c.method == 'applyLocalWallpaper'), isEmpty);
    expect(selected!.id, 'draft-1');
    expect(discarded, isNot(contains('draft-1')));
    await Live2DService.discard(selected);
    expect(discarded.where((id) => id == 'draft-1').toList(), ['draft-1']);
  });

  testWidgets('leaving a split preview discards its temporary layers',
      (tester) async {
    await open(
        tester, Live2DPage(apiClient: CloudTestApi(), sourcePath: source));
    await tester.pageBack();
    await tester.pumpAndSettle();
    expect(selected, isNull);
    expect(discarded.where((id) => id == 'draft-1').toList(), ['draft-1']);
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
    await tester.pumpAndSettle(const Duration(milliseconds: 16));
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
                                  apiClient: CloudTestApi(),
                                  filePath: source,
                                  title: 'test')));
                    })))));
    await tester.tap(find.text('register'));
    await settleFiles(tester);
    final live = find.byKey(const ValueKey('registration-tool-parallax'));
    await tester.ensureVisible(live);
    expect(tester.widget<TextButton>(live).onPressed, isNotNull);
    expect(find.text('2.5D'), findsNothing);
    await tester.tap(live);
    await settleFiles(tester);
    await tester.tap(find.byKey(const ValueKey('activate-live2d')));
    await settleFiles(tester);
    expect(find.byType(ImageFeaturePage), findsOneWidget);
    expect(find.byType(Live2DPage), findsNothing);
    await tester.tap(find.byKey(const ValueKey('save-registration')));
    await settleFiles(tester);
    expect(find.text('Live 2D 저장'), findsNothing);
    expect(options!.live2D!.id, 'draft-1');
    expect(options!.upscaleFactor, isNull);
    expect(options!.wallpaperTarget, isNull);
    expect(discarded, isNot(contains('draft-1')));
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
                        builder: (_) => ImageFeaturePage(
                            apiClient: CloudTestApi(),
                            filePath: source,
                            title: 'edit')));
              },
              child: const Text('register')));
    })));
    await tester.tap(find.text('register'));
    await settleFiles(tester);
    Future<void> tool(String name) async {
      final button = find.byKey(ValueKey('registration-tool-$name'));
      await tester.ensureVisible(button);
      await tester.tap(button);
      await settleFiles(tester);
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
    expect(
        calls.singleWhere((c) => c.method == 'splitLive2D').arguments,
        containsPair('sourcePath',
            source)); // Import the cloud result for the untouched source once.
    expect(calls.singleWhere((c) => c.method == 'splitLive2D').arguments,
        containsPair('backgroundUrl', 'https://test.invalid/Background'));
    final aspect = tester.widget<AspectRatio>(
        find.byKey(const ValueKey('registration-preview-aspect')));
    expect(aspect.aspectRatio, closeTo(16 / 9, .00001));
    expect(tester.getSize(find.byType(Live2DPreview)).aspectRatio,
        closeTo(16 / 9, .00001));
    await tester.tap(find.byKey(const ValueKey('activate-live2d')));
    await settleFiles(tester);
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
    await tool('filter');
    expect(calls.where((c) => c.method == 'splitLive2D').length, 1);
    preview = tester.widget<Live2DPreview>(find.byType(Live2DPreview));
    expect(preview.crop, finalCrop);
    expect(preview.filter, PhotoFilter.monochrome);
    expect(discarded, isNot(contains('draft-1')));

    // A failed layer export must retain the editable source for a retry.
    failEdit = true;
    await tester.tap(find.byKey(const ValueKey('save-registration')));
    await settleFiles(tester);
    expect(options, isNull);
    expect(discarded, isNot(contains('draft-1')));
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
    expect(discarded.where((id) => id == 'draft-1').toList(), ['draft-1']);
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
                            apiClient: CloudTestApi(),
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
    await tester.tap(find.byKey(const ValueKey('activate-live2d')));
    await settleFiles(tester);
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
    expect(discarded, isNot(contains('draft-1')));
    final finish = find.text('완료하고 등록');
    await tester.ensureVisible(finish);
    await tester.tap(finish);
    await settleFiles(tester);
    expect(result!.live2D!.id, 'edited-draft');
    expect(result!.ratio.preset, CropPreset.wide);
    expect(result!.ratio.portrait, isFalse);
    expect(discarded.where((id) => id == 'draft-1').toList(), ['draft-1']);
    await tester.runAsync(() => result!.editedFile!.dispose());
    await Live2DService.discard(result!.live2D);
  });

  testWidgets('legacy Live 2D completes activation and returns editable layers',
      (tester) async {
    tester.view.physicalSize = const Size(320, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await open(
        tester,
        MediaQuery(
            data: const MediaQueryData(textScaler: TextScaler.linear(1.6)),
            child: Live2DPage(
                apiClient: CloudTestApi(),
                sourcePath: source,
                crop: const Rect.fromLTWH(.25, 0, .5, 1),
                filter: PhotoFilter.sepia)));
    final apply = find.byKey(const ValueKey('activate-live2d'));
    await tester.ensureVisible(apply);
    await tester.pumpAndSettle();
    expect(find.text('Live 2D 적용'), findsOneWidget);
    await tester.tap(apply);
    await settleFiles(tester);
    expect(selected!.id, 'edited-draft');
    expect(discarded, ['draft-1']);
    expect(calls.where((c) => c.method == 'activateLive2DDraft'), hasLength(1));
    await Live2DService.discard(selected);
    expect(tester.takeException(), isNull);
  });

  testWidgets(
      'existing layers stay in the editor without resegmentation or navigation',
      (tester) async {
    await open(
        tester,
        ImageFeaturePage(
            apiClient: CloudTestApi(),
            filePath: source,
            title: 'existing',
            loadLive2D: () async => Live2DDraft(
                id: 'owned-draft',
                backgroundPath: source,
                foregroundPath: source,
                width: 32,
                height: 48)));
    final live = find.byKey(const ValueKey('registration-tool-parallax'));
    await tester.ensureVisible(live);
    await tester.tap(live);
    await settleFiles(tester);
    expect(
        calls.where(
            (c) => c.method == 'splitLive2D' || c.method == 'cloneLive2DDraft'),
        isEmpty);
    await tester.tap(find.byKey(const ValueKey('activate-live2d')));
    await tester.pumpAndSettle();
    expect(find.byType(ImageFeaturePage), findsOneWidget);
    expect(find.byType(Live2DPage), findsNothing);
    expect(calls.where((c) => c.method == 'activateLive2DDraft'), hasLength(1));
    expect(discarded, isNot(contains('owned-draft')));
    await tester.pageBack();
    await tester.pumpAndSettle();
    expect(discarded, contains('owned-draft'));
  });

  Future<void> savedPreview(WidgetTester tester, {bool shared = false}) async {
    final image = LocalImageItem(
        id: 'image-1',
        title: 'test',
        path: source,
        width: 32,
        height: 48,
        mode: 'live2d',
        tag: 'test',
        cloudProjectId: shared ? 'project' : null,
        galleryPostId: shared ? 'post' : null,
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
  }

  for (final confirmed in [true, false]) {
    testWidgets(
        'existing home selection can reopen Android and change to both ($confirmed)',
        (tester) async {
      activeFlags = 1;
      pickedFlags = 3;
      confirmActivation = confirmed;
      ImageRegistrationOptions? saved;
      const store =
          LocalImageStore(scope: 'server\naccount-1', ownerId: 'account-1');
      await open(
          tester,
          ImageFeaturePage(
              apiClient: CloudTestApi(),
              filePath: source,
              title: 'change destination',
              loadLive2D: () async => Live2DDraft(
                  id: 'owned-draft',
                  backgroundPath: source,
                  foregroundPath: source,
                  width: 32,
                  height: 48),
              onSave: (options) async {
                saved = options;
                await store.registerWallpaper('image-1',
                    target: options.wallpaperTarget!);
              }));
      final live = find.byKey(const ValueKey('registration-tool-parallax'));
      await tester.ensureVisible(live);
      await tester.tap(live);
      await settleFiles(tester);
      await tester.tap(find.byKey(const ValueKey('activate-live2d')));
      await settleFiles(tester);
      expect(find.byType(ImageFeaturePage), findsOneWidget);
      final activation =
          calls.singleWhere((call) => call.method == 'activateLive2DDraft');
      expect(activation.arguments,
          {'draftId': 'owned-draft', 'showSettings': true});
      expect(activeFlags, confirmed ? 3 : 1);
      // The cancellation snackbar temporarily covers the bottom action bar.
      await tester.pump(const Duration(seconds: 5));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const ValueKey('register-wallpaper')));
      await settleFiles(tester);
      expect(saved!.wallpaperTarget,
          confirmed ? WallpaperTarget.both : WallpaperTarget.home);
      expect(calls.where((call) => call.method == 'activateLive2DDraft'),
          hasLength(1));
      expect(
          calls
              .singleWhere((call) => call.method == 'applyLocalWallpaper')
              .arguments,
          {
            'scope': 'server\naccount-1',
            'ownerId': 'account-1',
            'id': 'image-1',
            'target': confirmed ? 'both' : 'home'
          });
    });
  }

  for (final shared in [false, true]) {
    testWidgets(
        'saved preview can reopen destination settings while shared=$shared',
        (tester) async {
      activeFlags = 1;
      pickedFlags = 3;
      await savedPreview(tester, shared: shared);
      await tester.tap(find.byTooltip('이미지 메뉴'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Live 2D 적용 화면 변경'));
      await tester.pumpAndSettle();
      expect(find.byType(HomeWallpaperPreviewPage), findsOneWidget);
      expect(activeFlags, 3);
      expect(
          calls
              .singleWhere((call) => call.method == 'activateLocalLive2D')
              .arguments,
          {
            'scope': 'server\naccount-1',
            'ownerId': 'account-1',
            'id': 'image-1',
            'showSettings': true
          });
      await tester
          .tap(find.byKey(const ValueKey('preview-wallpaper-settings')));
      await tester.pumpAndSettle();
      expect(
          (calls
              .singleWhere((call) => call.method == 'applyLocalWallpaper')
              .arguments as Map)['target'],
          'both');
      expect(calls.where((call) => call.method == 'activateLocalLive2D'),
          hasLength(1));
      expect(
          calls.where((call) => call.method == 'saveLocalImageEdits'), isEmpty);
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pumpAndSettle();
    });
  }

  testWidgets(
      'Android confirmation waits in the editor and unlocks editing on return',
      (tester) async {
    activeFlags = 0;
    pendingActivation = Completer<bool>();
    await open(
        tester,
        ImageFeaturePage(
            apiClient: CloudTestApi(), filePath: source, title: 'waiting'));
    final live = find.byKey(const ValueKey('registration-tool-parallax'));
    await tester.ensureVisible(live);
    await tester.tap(live);
    await settleFiles(tester);
    await tester.tap(find.byKey(const ValueKey('activate-live2d')));
    await tester.runAsync(() async {
      for (var attempt = 0; attempt < 100; attempt++) {
        await Future<void>.delayed(const Duration(milliseconds: 10));
        await tester.pump();
        if (calls.any((c) => c.method == 'activateLive2DDraft')) break;
      }
    });
    expect(calls.where((c) => c.method == 'activateLive2DDraft'), hasLength(1));
    expect(
        tester
            .widget<OutlinedButton>(
                find.byKey(const ValueKey('save-registration')))
            .onPressed,
        isNull);
    expect(
        tester
            .widget<FilledButton>(
                find.byKey(const ValueKey('register-wallpaper')))
            .onPressed,
        isNull);
    expect(
        tester
            .widget<TextButton>(
                find.byKey(const ValueKey('registration-tool-filter')))
            .onPressed,
        isNull);
    expect(selected, isNull);
    pendingActivation!.complete(true);
    await settleFiles(tester);
    expect(activeFlags, 3);
    expect(find.byType(ImageFeaturePage), findsOneWidget);
    expect(find.byType(Live2DPage), findsNothing);
    final filter = find.byKey(const ValueKey('registration-tool-filter'));
    await tester.ensureVisible(filter);
    await tester.tap(filter);
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey(PhotoFilter.monochrome)), findsOneWidget);
    expect(discarded, isNot(contains('draft-1')));
    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pumpAndSettle();
  });

  for (final failure in ['cancel', 'error']) {
    testWidgets(
        'Android $failure preserves separation and permits another Apply',
        (tester) async {
      activeFlags = 0;
      confirmActivation = failure != 'cancel';
      failActivation = failure == 'error';
      await open(
          tester,
          ImageFeaturePage(
              apiClient: CloudTestApi(), filePath: source, title: 'retry'));
      final live = find.byKey(const ValueKey('registration-tool-parallax'));
      await tester.ensureVisible(live);
      await tester.tap(live);
      await settleFiles(tester);
      await tester.tap(find.byKey(const ValueKey('activate-live2d')));
      await settleFiles(tester);
      expect(find.byType(ImageFeaturePage), findsOneWidget);
      expect(find.text('Live 2D 적용됨 · 다른 편집을 계속할 수 있습니다.'), findsNothing);
      expect(discarded, isNot(contains('draft-1')));
      expect(activeFlags, 0);
      confirmActivation = true;
      failActivation = false;
      await tester.tap(find.byKey(const ValueKey('activate-live2d')));
      await settleFiles(tester);
      expect(find.text('Live 2D 적용됨 · 다른 편집을 계속할 수 있습니다.'), findsOneWidget);
      expect(calls.where((c) => c.method == 'splitLive2D'), hasLength(1));
      expect(
          calls.where((c) => c.method == 'activateLive2DDraft'), hasLength(2));
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pumpAndSettle();
    });
  }

  for (final flags in [1, 2, 3]) {
    testWidgets(
        'saved Live 2D reuses Android target $flags after reopening the app',
        (tester) async {
      activeFlags = flags;
      for (var launch = 0; launch < 2; launch++) {
        await savedPreview(tester);
        await tester
            .tap(find.byKey(const ValueKey('preview-wallpaper-settings')));
        await tester.pumpAndSettle();
        expect(find.byKey(const ValueKey('wallpaper-home')), findsNothing);
        expect(find.byType(AlertDialog), findsNothing);
        await tester.pumpWidget(const SizedBox.shrink());
        await tester.pumpAndSettle();
      }
      final applies = calls.where((c) => c.method == 'applyLocalWallpaper');
      expect(applies, hasLength(2));
      expect(applies.map((c) => (c.arguments as Map)['target']),
          everyElement(WallpaperTarget.values[flags - 1].name));
      expect(
          calls.where((c) =>
              c.method == 'activateLocalLive2D' ||
              c.method == 'activateLive2DDraft'),
          isEmpty);
    });
  }

  testWidgets(
      'registration never opens Android when the live connection was removed',
      (tester) async {
    activeFlags = 0;
    await open(
        tester,
        ImageFeaturePage(
            apiClient: CloudTestApi(),
            filePath: source,
            title: 'disconnected',
            loadLive2D: () async => Live2DDraft(
                id: 'saved',
                backgroundPath: source,
                foregroundPath: source,
                width: 32,
                height: 48)));
    await tester.tap(find.byKey(const ValueKey('register-wallpaper')));
    await settleFiles(tester);
    expect(
        find.text('Live 2D 적용에서 Android 배경화면 설정을 먼저 완료해 주세요.'), findsOneWidget);
    expect(find.byType(ImageFeaturePage), findsOneWidget);
    expect(
        calls.where((c) =>
            c.method == 'activateLocalLive2D' ||
            c.method == 'activateLive2DDraft'),
        isEmpty);
    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pumpAndSettle();
  });

  testWidgets(
      'saved preview shows only the image and registers both targets from the top right',
      (tester) async {
    activeFlags = 3;
    await savedPreview(tester);
    expect(find.byType(Live2DPreview), findsOneWidget);
    expect(find.byType(DropdownButton<WallpaperTarget>), findsNothing);
    expect(find.byType(FilledButton), findsNothing);
    expect(find.byKey(const ValueKey('activate-saved-live2d')), findsNothing);
    final settings = find.byKey(const ValueKey('preview-wallpaper-settings'));
    expect(tester.getCenter(settings).dy, lessThan(100));
    await tester.tap(settings);
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('confirm-wallpaper-apply')), findsNothing);
    expect(
        calls.singleWhere((c) => c.method == 'applyLocalWallpaper').arguments, {
      'scope': 'server\naccount-1',
      'ownerId': 'account-1',
      'id': 'image-1',
      'target': 'both'
    });
    expect(
        calls.where((c) =>
            c.method == 'activateLocalLive2D' ||
            c.method == 'previewLocalWallpaper'),
        isEmpty);
    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pumpAndSettle();
  });

  for (final confirmed in [false, true]) {
    testWidgets(
        'a removed connection requires explicit Live 2D Apply before reopening Android ($confirmed)',
        (tester) async {
      activeFlags = 0;
      confirmActivation = confirmed;
      await savedPreview(tester);
      expect(calls.where((c) => c.method == 'activateLocalLive2D'), isEmpty);
      await tester
          .tap(find.byKey(const ValueKey('preview-wallpaper-settings')));
      await tester.pumpAndSettle();
      expect(calls.where((c) => c.method == 'activateLocalLive2D'), isEmpty);
      expect(find.byKey(const ValueKey('wallpaper-home')), findsNothing);
      await tester.tap(find.widgetWithText(FilledButton, 'Live 2D 적용'));
      await tester.pumpAndSettle();
      expect(
          calls.singleWhere((c) => c.method == 'activateLocalLive2D').arguments,
          {
            'scope': 'server\naccount-1',
            'ownerId': 'account-1',
            'id': 'image-1',
            'showSettings': true,
          });
      expect(find.text(confirmed ? '배경화면을 등록했습니다.' : '배경화면 등록을 취소했습니다.'),
          findsOneWidget);
      expect(find.text(confirmed ? '배경화면 등록을 취소했습니다.' : '배경화면을 등록했습니다.'),
          findsNothing);
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pumpAndSettle();
    });
  }
}

// Exercise each UI through the cloud boundary while the platform mock verifies import/apply.
class CloudTestApi extends ApiClient {
  CloudTestApi() : super(baseUrl: 'https://test.invalid');
  @override
  String get userId => 'account-1';
  @override
  Future<LiveLayerProject> createProject(
          {required String title,
          required String filePath,
          required int width,
          required int height}) async =>
      getProject('project');
  @override
  Future<void> startProcessing(String id) async {}
  @override
  Future<void> deleteProject(String id) async {}
  @override
  Future<Map<String, dynamic>> waitForProcessing(String id,
          {Duration interval = const Duration(seconds: 2),
          int maxAttempts = 480}) async =>
      {'resultWidth': 32, 'resultHeight': 48};
  @override
  Future<LiveLayerProject> getProject(String id) async => LiveLayerProject(
          id: id,
          title: 'test',
          status: 'Completed',
          originalImageUrl: '',
          layers: [
            for (final role in ['Background', 'Body'])
              LiveLayer(
                  id: role,
                  layerType: role,
                  imageUrl: 'https://test.invalid/$role',
                  layerOrder: role == 'Background' ? 0 : 1,
                  depth: 0,
                  positionX: 0,
                  positionY: 0,
                  rotation: 0,
                  scale: 1,
                  movementX: 1,
                  movementY: 1)
          ]);
}
