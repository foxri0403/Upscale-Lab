import 'dart:async';

import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/services/local_image_store.dart';
import 'package:live_layer/services/wallpaper_service.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const wallpaperChannel = MethodChannel('live_layer/wallpaper');
  const storageChannel = MethodChannel('live_layer/storage');
  final messenger =
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
  const service = WallpaperService();

  tearDown(() {
    messenger.setMockMethodCallHandler(wallpaperChannel, null);
    messenger.setMockMethodCallHandler(storageChannel, null);
  });

  for (final target in WallpaperTarget.values) {
    test(
      'local, remote and clear expose the same ${target.name} contract',
      () async {
        final calls = <MethodCall>[];
        messenger.setMockMethodCallHandler(wallpaperChannel, (call) async {
          calls.add(call);
          return null;
        });

        await service.applyLocal(
          sourcePath: '/private/image.png',
          target: target,
        );
        await service.applyRemote(
          url: 'https://example.test/image.png',
          target: target,
        );
        await service.clear(target: target);

        expect(calls.map((call) => call.method), [
          'applyLocalStaticWallpaper',
          'applyStaticWallpaper',
          'clearWallpaper',
        ]);
        expect(
          calls.map((call) => (call.arguments as Map)['target']),
          everyElement(target.name),
        );
        expect((calls[0].arguments as Map)['sourcePath'], '/private/image.png');
        expect(
          (calls[1].arguments as Map)['url'],
          'https://example.test/image.png',
        );
      },
    );

    test('library apply uses image ownership for ${target.name}', () async {
      MethodCall? received;
      messenger.setMockMethodCallHandler(storageChannel, (call) async {
        received = call;
        return null;
      });
      const store = LocalImageStore(scope: 'server\naccount-a');
      await store.applyWallpaper('image-a', target: target);
      expect(received!.method, 'applyLocalWallpaper');
      expect(received!.arguments, {
        'scope': 'server\naccount-a',
        'id': 'image-a',
        'target': target.name,
      });
    });
  }

  test(
    'UI cannot report completion before the native operation finishes',
    () async {
      final completion = Completer<void>();
      messenger.setMockMethodCallHandler(wallpaperChannel, (_) async {
        await completion.future;
        return null;
      });
      var finished = false;
      final operation = service.clear(target: WallpaperTarget.both)
        ..then((_) => finished = true);
      await Future<void>.delayed(Duration.zero);
      expect(finished, isFalse);
      completion.complete();
      await operation;
      expect(finished, isTrue);
    },
  );

  test('native failures reach apply and removal UI callers', () async {
    messenger.setMockMethodCallHandler(wallpaperChannel, (_) async {
      throw PlatformException(
        code: 'unsupported_target',
        message: 'unsupported',
      );
    });
    final unsupported = isA<PlatformException>().having(
      (error) => error.code,
      'code',
      'unsupported_target',
    );
    await expectLater(
      service.applyLocal(
        sourcePath: '/image.png',
        target: WallpaperTarget.lock,
      ),
      throwsA(unsupported),
    );
    await expectLater(
      service.applyRemote(
        url: 'https://example.test/image.png',
        target: WallpaperTarget.both,
      ),
      throwsA(unsupported),
    );
    await expectLater(
      service.clear(target: WallpaperTarget.lock),
      throwsA(unsupported),
    );
  });
}
