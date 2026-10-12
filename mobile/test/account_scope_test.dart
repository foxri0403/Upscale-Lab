import 'dart:convert';

import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/services/api_client.dart';
import 'package:live_layer/services/local_image_store.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  AuthSession session(String id, String nonce) => AuthSession(
      accessToken: 'header.${base64Url.encode(utf8.encode(jsonEncode({
            'sub': id,
            'nonce': nonce
          })))}.signature',
      expiresAt: DateTime.utc(2030));

  test('ownership follows server and user, not token refresh or device', () {
    final api = ApiClient(baseUrl: 'https://api.example.test/');
    api.restoreSession(session('a', '1'));
    final scopeA = api.storageScope;
    api.restoreSession(session('a', '2'));
    expect(api.storageScope, scopeA);
    api.restoreSession(session('b', '3'));
    expect(api.storageScope, isNot(scopeA));
    api.updateBaseUrl('https://other.example.test');
    expect(() => api.storageScope, throwsStateError);
    api.restoreSession(session('a', '4'));
    expect(api.storageScope, isNot(scopeA));
    api.logout();
    expect(() => api.storageScope, throwsStateError);
  });

  test('every private storage operation carries its captured account scope',
      () async {
    const channel = MethodChannel('live_layer/storage');
    final calls = <String>[];
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (call) async {
      expect((call.arguments as Map)['scope'], 'server\naccount-a');
      calls.add(call.method);
      if (call.method == 'listLocalImages') return [];
      return null;
    });
    addTearDown(() => TestDefaultBinaryMessengerBinding
        .instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, null));
    const store = LocalImageStore(scope: 'server\naccount-a');
    await store.getAll();
    await store.updateTags(id: 'a', tags: ['동물', '레트로']);
    await store.markShared(id: 'a', cloudProjectId: 'p', galleryPostId: 'g');
    await store.markUnshared('a');
    await store.delete('a');
    expect(calls, [
      'listLocalImages',
      'updateLocalImageTag',
      'markLocalImageShared',
      'markLocalImageUnshared',
      'deleteLocalImage'
    ]);
  });
}
