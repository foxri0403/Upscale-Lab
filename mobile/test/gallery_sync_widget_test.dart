import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/models/gallery.dart';
import 'package:live_layer/services/api_client.dart';
import 'package:live_layer/unified_home.dart';

const apiScope = 'https://api.example.test\naccount-a';

class _GalleryApi extends ApiClient {
  _GalleryApi() : super(baseUrl: 'https://api.example.test');

  final tagResult = Completer<List<String>>();
  final requestedTags = <String>[];
  int previewRequests = 0;

  @override
  String get storageScope => apiScope;

  @override
  Future<List<String>> updateGalleryTags(
      {required String postId, required List<String> tags}) {
    expect(postId, 'post-id');
    requestedTags.addAll(tags);
    return tagResult.future;
  }

  @override
  Future<List<GalleryPostItem>> getGallery(
          {String? search, String? tag, List<String>? tags}) async =>
      [
        GalleryPostItem(
          id: 'post-id',
          username: 'artist',
          title: '고양이 이미지',
          imageUrl: 's3://private/cat.png',
          tag: '동물',
          createdAt: DateTime.utc(2026),
        ),
      ];

  @override
  Future<String> getGalleryImageUrl(String postId) async {
    expect(postId, 'post-id');
    previewRequests++;
    return 'https://images.example.test/cat.png';
  }
}

void main() {
  for (final originalTag in [unspecifiedGalleryTag, '동물']) {
    testWidgets(
        'shared $originalTag waits for cloud confirmation before saving locally',
        (tester) async {
      const channel = MethodChannel('live_layer/storage');
      final image = <String, Object?>{
        'id': 'local-id',
        'title': '고양이 이미지',
        'path': '/missing/cat.png',
        'width': 100,
        'height': 100,
        'mode': 'original',
        'tag': originalTag,
        'createdAt': '2026-10-11T00:00:00Z',
        'cloudProjectId': 'project-id',
        'galleryPostId': 'post-id',
      };
      var localWrites = 0;
      tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(channel,
          (call) async {
        if (call.method == 'listLocalImages') return [image];
        if (call.method == 'updateLocalImageTag') {
          final arguments = call.arguments as Map;
          expect(arguments['id'], 'local-id');
          image['tag'] = (arguments['tags'] as List).first;
          image['tags'] = arguments['tags'];
          expect(arguments['scope'], apiScope);
          localWrites++;
          return null;
        }
        throw StateError('Unexpected storage call: ${call.method}');
      });
      addTearDown(() => tester.binding.defaultBinaryMessenger
          .setMockMethodCallHandler(channel, null));
      final api = _GalleryApi();
      await tester.pumpWidget(MaterialApp(
          home: UnifiedHomePage(apiClient: api, onLogout: (_) async {})));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text(originalTag));
      await tester.pumpAndSettle();
      await tester.tap(find.text(originalTag));
      await tester.pumpAndSettle();
      if (originalTag != '동물') {
        await tester.tap(find.widgetWithText(CheckboxListTile, '동물'));
      }
      await tester.tap(find.text('태그 저장'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 300));

      expect(api.requestedTags, ['동물']);
      expect(localWrites, 0);
      api.tagResult.complete(['동물']);
      await tester.pumpAndSettle();

      expect(localWrites, 1);
      expect(image['tag'], '동물');
      expect(find.text('동물'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets('search resolves S3 image identifiers and retries with a new URL',
      (tester) async {
    final api = _GalleryApi();
    await tester.pumpWidget(
        MaterialApp(home: Scaffold(body: GallerySearchPage(apiClient: api))));
    await tester.pumpAndSettle();

    expect(api.previewRequests, 1);
    final image = tester.widget<Image>(find.byType(Image));
    expect(((image.image as ResizeImage).imageProvider as NetworkImage).url,
        'https://images.example.test/cat.png');
    // Widget tests reject real network image requests; exercise the retry button.
    await tester.tap(find.text('다시 불러오기'));
    await tester.pumpAndSettle();
    expect(api.previewRequests, 2);
    expect(find.text('artist · 동물'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
