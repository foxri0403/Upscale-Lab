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

  test('download URL skips counting and completion reads the persisted count', () async {
    final requests = <http.Request>[];
    await http.runWithClient(() async {
      expect(await api.getGalleryDownloadUrl('post-id'), 'https://example.test/photo.png');
      expect(await api.recordGalleryDownload('post-id'), 12);
    }, () => MockClient((request) async {
      requests.add(request);
      expect(request.method, 'POST');
      if (request.url.path.endsWith('/download-url')) {
        expect(request.url.queryParameters['countDownload'], 'false');
        return jsonResponse({'url': 'https://example.test/photo.png'});
      }
      expect(request.url.path, '/api/gallery/post-id/download-complete');
      return jsonResponse({'downloadCount': 12});
    }));
    expect(requests, hasLength(2));
  });

  test('download completion rejects an invalid counter response', () async {
    await http.runWithClient(() async {
      await expectLater(api.recordGalleryDownload('post-id'), throwsStateError);
    }, () => MockClient((_) async => jsonResponse({'downloadCount': -1})));
  });

  test(
      'editing confirms both the name and complete tags returned by the server',
      () async {
    await http.runWithClient(() async {
      expect(
          await api.updateGalleryPost(
              postId: 'post-id', title: '새 이름', tags: ['풍경', 'SF']),
          ['SF', '풍경']);
    },
        () => MockClient((request) async {
              expect(request.method, 'PATCH');
              expect(request.url.path, '/api/gallery/post-id');
              expect(jsonDecode(request.body), {
                'title': '새 이름',
                'tags': ['풍경', 'SF']
              });
              return jsonResponse({
                'id': 'post-id',
                'title': '새 이름',
                'tags': ['SF', '풍경']
              });
            }));
    for (final response in [
      {
        'id': 'post-id',
        'title': 'old',
        'tags': ['풍경', 'SF']
      },
      {
        'id': 'post-id',
        'title': '새 이름',
        'tags': ['풍경']
      },
      {
        'id': 'wrong-id',
        'title': '새 이름',
        'tags': ['풍경', 'SF']
      },
    ]) {
      await http.runWithClient(() async {
        await expectLater(
            api.updateGalleryPost(
                postId: 'post-id', title: '새 이름', tags: ['풍경', 'SF']),
            throwsStateError);
      }, () => MockClient((_) async => jsonResponse(response)));
    }
  });

  for (final tags in [
    ['동물', '레트로'],
    ['풍경', '게임', 'SF']
  ]) {
    test('legacy server persists and confirms every tag in $tags', () async {
      Map<String, dynamic>? saved;
      await http.runWithClient(() async {
        expect(
            await api.shareProject(projectId: 'p', title: 'sample', tags: tags),
            'post-id');
        // Simulate a separate search client reading only persisted legacy fields.
        final reader = ApiClient(baseUrl: 'https://api.example.test');
        final posts = await reader.getGallery(tags: [tags.last]);
        expect(posts.single.tags, tags);
      },
          () => MockClient((request) async {
                if (request.method == 'POST') {
                  final body = jsonDecode(request.body) as Map<String, dynamic>;
                  expect(body['tags'], tags);
                  saved = {
                    'id': 'post-id',
                    'username': 'artist',
                    'title': body['title'],
                    'imageUrl': 'https://images.test/cat.png',
                    'tag': body['tag'],
                    'description': body['description'],
                    'createdAt': '2026-10-12T00:00:00Z'
                  };
                  // Old servers do not return or store a tags array.
                  return jsonResponse(saved!);
                }
                return jsonResponse([saved!]);
              }));
    });
  }

  test('legacy server discarding metadata still fails instead of losing tags',
      () async {
    await http.runWithClient(() async {
      await expectLater(
          api.shareProject(projectId: 'p', title: 'cat', tags: ['동물', '레트로']),
          throwsStateError);
    },
        () => MockClient(
            (_) async => jsonResponse({'id': 'post-id', 'tag': '동물'})));
  });

  test('multiple tags are confirmed as a complete set', () async {
    await http.runWithClient(() async {
      expect(
          await api.updateGalleryTags(postId: 'post-id', tags: ['동물', '레트로']),
          ['레트로', '동물']);
    },
        () => MockClient((request) async {
              expect(jsonDecode(request.body)['tags'], ['동물', '레트로']);
              return jsonResponse({
                'id': 'post-id',
                'tag': '레트로',
                'tags': ['레트로', '동물']
              });
            }));
    await http.runWithClient(() async {
      await expectLater(
          api.shareProject(projectId: 'p', title: 'cat', tags: ['동물', '레트로']),
          throwsStateError);
    },
        () => MockClient((_) async => jsonResponse({
              'id': 'post-id',
              'tag': '동물',
              'tags': ['동물']
            })));
  });

  test('multiple search tags match any secondary tag', () async {
    await http.runWithClient(() async {
      final posts = await api.getGallery(tags: ['레트로', '풍경']);
      expect(posts.single.tags, ['동물', '레트로']);
    },
        () => MockClient((request) async {
              expect(request.url.queryParametersAll['tags'], ['레트로', '풍경']);
              return jsonResponse([
                {
                  'id': '1',
                  'username': 'artist',
                  'title': 'cat',
                  'imageUrl': 'https://images.test/cat.png',
                  'tag': '동물',
                  'tags': ['동물', '레트로'],
                  'createdAt': '2026-10-12T00:00:00Z'
                },
                {
                  'id': '2',
                  'username': 'artist',
                  'title': 'dog',
                  'imageUrl': 'https://images.test/dog.png',
                  'tag': '동물',
                  'tags': ['동물'],
                  'createdAt': '2026-10-12T00:00:00Z'
                },
              ]);
            }));
  });

  test('import requests an original download and never uses the thumbnail',
      () async {
    await http.runWithClient(() async {
      expect(await api.getGalleryDownloadUrl('post-id'),
          'https://images.test/original.png');
    },
        () => MockClient((request) async {
              expect(request.method, 'POST');
              expect(request.url.path, '/api/gallery/post-id/download-url');
              return jsonResponse({'url': 'https://images.test/original.png'});
            }));
  });

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
