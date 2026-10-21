import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/models/gallery.dart';
import 'package:live_layer/models/live_layer_models.dart';
import 'package:live_layer/services/api_client.dart';
import 'package:live_layer/unified_home.dart';

class _FakeApi extends ApiClient {
  _FakeApi() : super(baseUrl: 'https://api.example.test');

  final deletedPosts = <String>[];
  final deletedProjects = <String>[];
  int shareAttempts = 0;
  bool shareResponseUnknown = false;

  @override
  Future<LiveLayerProject> createProject({
    required String title,
    required String filePath,
    required int width,
    required int height,
  }) async =>
      const LiveLayerProject(
        id: 'project-1',
        title: '테스트 이미지',
        status: 'ready',
        originalImageUrl: '',
        layers: [],
      );

  @override
  Future<String> shareProject({
    required String projectId,
    required String title,
    String? tag,
    List<String>? tags,
  }) async {
    shareAttempts++;
    if (shareResponseUnknown) throw StateError('Timeout after post request');
    return 'post-1';
  }

  @override
  String get userId => 'account-1';

  @override
  String get storageScope => 'https://api.example.test\naccount-1';

  @override
  Future<List<GalleryPostItem>> getGallery(
          {String? search, String? tag, List<String>? tags}) async =>
      [];

  @override
  Future<void> deleteGalleryPost(String id) async => deletedPosts.add(id);

  @override
  Future<void> deleteProject(String id) async => deletedProjects.add(id);
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const storageChannel = MethodChannel('live_layer/storage');
  late _FakeApi api;
  late List<Map<String, Object?>> localImages;
  late List<String> localDeletes;
  late List<String> localUnshares;
  late bool failLocalShareMarker;

  setUp(() {
    api = _FakeApi();
    localDeletes = [];
    localUnshares = [];
    failLocalShareMarker = false;
    localImages = [
      {
        'id': 'image-1',
        'title': '테스트 이미지',
        'path': '/nonexistent/upscale-test-image.png',
        'width': 256,
        'height': 256,
        'mode': 'original',
        'tag': '풍경',
        'tags': ['풍경'],
        'createdAt': '2026-10-15T00:00:00Z',
        'cloudProjectId': 'project-1',
        'galleryPostId': 'post-1',
      }
    ];
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(storageChannel, (call) async {
      final args = Map<Object?, Object?>.from(call.arguments as Map);
      expect(args['ownerId'], 'account-1');
      switch (call.method) {
        case 'listLocalImages':
          return localImages;
        case 'getLibraryOrder':
          return <String, Object?>{};
        case 'deleteLocalImage':
          final id = args['id'] as String;
          localDeletes.add(id);
          localImages.removeWhere((item) => item['id'] == id);
          return null;
        case 'markLocalImageShared':
          if (failLocalShareMarker) {
            throw PlatformException(code: 'gallery_write_failed');
          }
          return null;
        case 'markLocalImageUnshared':
          final id = args['id'] as String;
          localUnshares.add(id);
          for (final item in localImages.where((item) => item['id'] == id)) {
            item['cloudProjectId'] = null;
            item['galleryPostId'] = null;
          }
          return null;
        default:
          throw StateError('Unexpected storage call: ${call.method}');
      }
    });
  });

  tearDown(() => TestDefaultBinaryMessengerBinding
      .instance.defaultBinaryMessenger
      .setMockMethodCallHandler(storageChannel, null));

  Future<void> openImageMenu(WidgetTester tester) async {
    await tester.pumpWidget(MaterialApp(
        home: UnifiedHomePage(apiClient: api, onLogout: (_) async {})));
    await tester.pumpAndSettle();
    final menu = find.descendant(
        of: find.byKey(const ValueKey('image-1')),
        matching: find.byType(PopupMenuButton<String>));
    expect(menu, findsOneWidget);
    await tester.ensureVisible(menu);
    await tester.tap(menu);
    await tester.pumpAndSettle();
  }

  testWidgets('deleting a shared local image never deletes the cloud post',
      (tester) async {
    await openImageMenu(tester);
    await tester.tap(find.text('삭제').last);
    await tester.pumpAndSettle();
    expect(find.textContaining('공유한 이미지는 탐색과 클라우드에 그대로 남습니다'), findsOneWidget);
    await tester.tap(find.widgetWithText(FilledButton, '삭제'));
    await tester.pumpAndSettle();

    expect(localDeletes, ['image-1']);
    expect(api.deletedPosts, isEmpty);
    expect(api.deletedProjects, isEmpty);
    expect(localImages, isEmpty);
    expect(tester.takeException(), isNull);
  });

  testWidgets('deleting a private local image does not call the cloud API',
      (tester) async {
    localImages.single.remove('cloudProjectId');
    localImages.single.remove('galleryPostId');
    await openImageMenu(tester);
    await tester.tap(find.text('삭제').last);
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, '삭제'));
    await tester.pumpAndSettle();

    expect(localDeletes, ['image-1']);
    expect(api.deletedPosts, isEmpty);
    expect(api.deletedProjects, isEmpty);
    expect(tester.takeException(), isNull);
  });

  testWidgets(
      'failure to save shared marker never removes published cloud post',
      (tester) async {
    localImages.single.remove('cloudProjectId');
    localImages.single.remove('galleryPostId');
    failLocalShareMarker = true;
    await openImageMenu(tester);
    await tester.tap(find.text('공유').last);
    await tester.pumpAndSettle();

    expect(api.shareAttempts, 1);
    expect(api.deletedProjects, isEmpty);
    expect(api.deletedPosts, isEmpty);
    expect(localDeletes, isEmpty);
    expect(tester.takeException(), isNull);
  });

  testWidgets('uncertain share response does not delete cloud project',
      (tester) async {
    localImages.single.remove('cloudProjectId');
    localImages.single.remove('galleryPostId');
    api.shareResponseUnknown = true;
    await openImageMenu(tester);
    await tester.tap(find.text('공유').last);
    await tester.pumpAndSettle();

    expect(api.shareAttempts, 1);
    expect(api.deletedProjects, isEmpty);
    expect(api.deletedPosts, isEmpty);
    expect(localDeletes, isEmpty);
    expect(tester.takeException(), isNull);
  });

  testWidgets('explicit unshare still deletes the shared cloud copy',
      (tester) async {
    await openImageMenu(tester);
    await tester.tap(find.text('공유 취소').last);
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, '공유 취소'));
    await tester.pumpAndSettle();

    expect(api.deletedPosts, ['post-1']);
    expect(api.deletedProjects, ['project-1']);
    expect(localDeletes, isEmpty);
    expect(localUnshares, ['image-1']);
    expect(localImages, hasLength(1));
    expect(tester.takeException(), isNull);
  });
}
