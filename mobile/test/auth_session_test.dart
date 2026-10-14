import 'dart:async';
import 'dart:convert';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:live_layer/services/api_client.dart';
import 'package:live_layer/services/auth_session_store.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  AuthSession session({bool expired = true}) => AuthSession(
      accessToken: 'h.${base64Url.encode(utf8.encode('{"sub":"account"}'))}.s',
      expiresAt: DateTime.now().toUtc().add(Duration(hours: expired ? -2 : 2)),
      refreshToken: 'refresh-secret',
      refreshExpiresAt: DateTime.now().toUtc().add(const Duration(days: 90)));
  http.Response renewed() =>
      http.Response(jsonEncode(session(expired: false).toJson()), 200);

  test(
      'secure storage retains expired access token and refresh credential until explicit logout',
      () async {
    FlutterSecureStorage.setMockInitialValues({});
    final store = AuthSessionStore();
    await store.save(session());
    expect((await store.read())!.refreshToken, 'refresh-secret');
    expect((await store.read())!.expiresAt.isBefore(DateTime.now()), isTrue);
    await store.clear();
    expect(await store.read(), isNull);
  });

  test('concurrent requests refresh once and persist renewed session',
      () async {
    final api = ApiClient(baseUrl: 'https://api.test')
      ..restoreSession(session());
    var refreshes = 0;
    var saves = 0;
    api.onSessionChanged = (_) async {
      saves++;
    };
    await http.runWithClient(() async {
      await Future.wait([api.validateSession(), api.validateSession()]);
    },
        () => MockClient((request) async {
              if (request.url.path.endsWith('/refresh')) {
                refreshes++;
                expect(request.headers['authorization'], isNull);
                await Future<void>.delayed(const Duration(milliseconds: 10));
                return renewed();
              }
              expect(request.headers['authorization'], startsWith('Bearer '));
              return http.Response('{}', 200);
            }));
    expect(refreshes, 1);
    expect(saves, 1);
  });

  test('temporary refresh failure preserves account and allows retry',
      () async {
    final api = ApiClient(baseUrl: 'https://api.test')
      ..restoreSession(session());
    final scope = api.storageScope;
    await http.runWithClient(() async {
      await expectLater(api.validateSession(), throwsA(isA<ApiException>()));
    }, () => MockClient((_) async => http.Response('unavailable', 503)));
    expect(api.storageScope, scope);
    await http.runWithClient(
        () => api.validateSession(),
        () => MockClient((request) async =>
            request.url.path.endsWith('/refresh')
                ? renewed()
                : http.Response('{}', 200)));
    expect(api.storageScope, scope);
  });

  test('401 renews an otherwise unexpired access token once', () async {
    final api = ApiClient(baseUrl: 'https://api.test')
      ..restoreSession(session(expired: false));
    var requests = 0;
    await http.runWithClient(
        () => api.validateSession(),
        () => MockClient((request) async {
              if (request.url.path.endsWith('/refresh')) return renewed();
              return http.Response('{}', ++requests == 1 ? 401 : 200);
            }));
    expect(requests, 2);
  });

  test('logout during refresh never restores or saves the previous session',
      () async {
    final api = ApiClient(baseUrl: 'https://api.test')
      ..restoreSession(session());
    final started = Completer<void>();
    final release = Completer<void>();
    var saves = 0;
    api.onSessionChanged = (_) async {
      saves++;
    };
    await http.runWithClient(() async {
      final pending = api.validateSession();
      await started.future;
      api.logout();
      release.complete();
      await pending;
    },
        () => MockClient((request) async {
              if (request.url.path.endsWith('/refresh')) {
                started.complete();
                await release.future;
                return renewed();
              }
              return http.Response('{}', 200);
            }));
    expect(() => api.storageScope, throwsStateError);
    expect(saves, 0);
  });
}
