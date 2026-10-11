import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:live_layer/services/api_client.dart';

void main() {
  final api = ApiClient(baseUrl: 'https://api.example.test');
  http.Response jsonResponse(Object body) => http.Response.bytes(
        utf8.encode(jsonEncode(body)),
        200,
        headers: {'content-type': 'application/json'},
      );

  test('tag changes send Korean tag and require cloud confirmation', () async {
    api.restoreSession(AuthSession(
      accessToken: 'test-token',
      expiresAt: DateTime.utc(2030),
    ));
    final client = MockClient((request) async {
      expect(request.method, 'PATCH');
      expect(request.url.path, '/api/gallery/post-id/tag');
      expect(request.headers['authorization'], 'Bearer test-token');
      expect(jsonDecode(request.body)['tag'], '동물');
      return jsonResponse({'id': 'post-id', 'tag': '동물'});
    });
    await http.runWithClient(() async {
      expect(await api.updateGalleryTag(postId: 'post-id', tag: '동물'), '동물');
    }, () => client);
  });

  for (final body in [
    {'id': 'post-id'},
    {'id': 'post-id', 'tag': '장르 설정되지 않음'},
    {'id': 'another-post', 'tag': '동물'},
  ]) {
    test('does not report a successful tag change for $body', () async {
      await http.runWithClient(() async {
        await expectLater(
          api.updateGalleryTag(postId: 'post-id', tag: '동물'),
          throwsStateError,
        );
      }, () => MockClient((_) async => jsonResponse(body)));
    });
  }

  test('sharing cannot silently discard the selected tag', () async {
    await http.runWithClient(() async {
      await expectLater(
        api.shareProject(projectId: 'project-id', title: '고양이', tag: '동물'),
        throwsStateError,
      );
    }, () => MockClient((_) async => jsonResponse({'id': 'post-id'})));
  });

  test('preview requests use GET and return a browser-readable signed URL',
      () async {
    await http.runWithClient(() async {
      expect(await api.getGalleryImageUrl('post-id'),
          'https://images.example.test/cat.png');
    },
        () => MockClient((request) async {
              expect(request.method, 'GET');
              expect(request.url.path, '/api/gallery/post-id/preview-url');
              return jsonResponse(
                  {'url': 'https://images.example.test/cat.png'});
            }));
  });

  test('rejects S3 storage identifiers returned as a preview URL', () async {
    await http.runWithClient(() async {
      await expectLater(api.getGalleryImageUrl('post-id'), throwsStateError);
    },
        () => MockClient(
            (_) async => jsonResponse({'url': 's3://private/cat.png'})));
  });

  test('search preserves Korean titles and exact genre filtering', () async {
    await http.runWithClient(() async {
      final posts = await api.getGallery(search: '고양이', tag: '동물');
      expect(posts.single.title, '고양이 이미지');
      expect(posts.single.tag, '동물');
    },
        () => MockClient((request) async {
              expect(
                  request.url.queryParameters, {'search': '고양이', 'tag': '동물'});
              return jsonResponse([
                {
                  'id': 'post-id',
                  'username': 'artist',
                  'title': '고양이 이미지',
                  'imageUrl': 'https://images.example.test/cat.png',
                  'tag': '동물',
                  'createdAt': '2026-10-11T00:00:00Z',
                },
              ]);
            }));
  });
}
