import 'dart:convert';

import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/services/api_client.dart';
import 'package:live_layer/services/local_image_store.dart';
import 'package:live_layer/models/library_order.dart';

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
    final stableId = api.userId;
    api.restoreSession(session('a', '2'));
    expect(api.storageScope, scopeA);
    api.restoreSession(session('b', '3'));
    expect(api.storageScope, isNot(scopeA));
    api.updateBaseUrl('https://other.example.test');
    expect(() => api.storageScope, throwsStateError);
    api.restoreSession(session('a', '4'));
    expect(api.storageScope, isNot(scopeA));
    expect(
        api.userId, stableId); // Owner ID is stable when the API host changes.
    api.logout();
    expect(() => api.storageScope, throwsStateError);
  });

  test('every gallery operation carries its captured account and owner ID',
      () async {
    const channel = MethodChannel('live_layer/storage');
    final calls = <String>[];
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (call) async {
      expect((call.arguments as Map)['scope'], 'server\naccount-a');
      expect((call.arguments as Map)['ownerId'], 'account-a');
      calls.add(call.method);
      if (call.method == 'listLocalImages') return [];
      return null;
    });
    addTearDown(() => TestDefaultBinaryMessengerBinding
        .instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, null));
    const store =
        LocalImageStore(scope: 'server\naccount-a', ownerId: 'account-a');
    await store.getAll();
    await store.getOrder();
    await store.saveOrder(const LibraryOrder(ids: ['a']));
    await store.previewWallpaper('a');
    await store.updateTags(id: 'a', tags: ['동물', '레트로']);
    await store.updateDetails(id: 'a', title: '새 이름', tags: ['동물', '풍경']);
    await store.markShared(id: 'a', cloudProjectId: 'p', galleryPostId: 'g');
    await store.markUnshared('a');
    await store.delete('a');
    expect(calls, [
      'listLocalImages',
      'getLibraryOrder',
      'saveLibraryOrder',
      'previewLocalWallpaper',
      'updateLocalImageTag',
      'updateLocalImageDetails',
      'markLocalImageShared',
      'markLocalImageUnshared',
      'deleteLocalImage'
    ]);
  });
}
