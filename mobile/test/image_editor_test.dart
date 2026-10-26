import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter_test/flutter_test.dart';
import 'package:live_layer/models/gallery_tags.dart';
import 'package:live_layer/models/image_editor.dart';
import 'package:live_layer/services/image_editor_service.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const sourceSize = ui.Size(400, 200);

  test('orientation changes labels and actual preset ratios in both directions',
      () {
    for (final portrait in [true, false, true, false]) {
      final wide =
          CropPreset.wide.aspectRatio(portrait: portrait, source: sourceSize)!;
      final standard = CropPreset.standard
          .aspectRatio(portrait: portrait, source: sourceSize)!;
      expect(CropPreset.wide.label(portrait), portrait ? '9:16' : '16:9');
      expect(CropPreset.standard.label(portrait), portrait ? '3:4' : '4:3');
      expect(wide, portrait ? 9 / 16 : 16 / 9);
      expect(standard, portrait ? 3 / 4 : 4 / 3);
      final geometry = CropGeometry(sourceSize)
        ..layout(const ui.Size(360, 360), standard);
      expect(geometry.sourceRect.size.aspectRatio, closeTo(standard, .000001));
      expect(
          CropPreset.original
              .aspectRatio(portrait: portrait, source: sourceSize),
          2);
      expect(
          CropPreset.square.aspectRatio(portrait: portrait, source: sourceSize),
          1);
      expect(
          CropPreset.free.aspectRatio(portrait: portrait, source: sourceSize),
          isNull);
    }
  });

  test('pinch and pan crop source pixels without allowing blank edges', () {
    final geometry = CropGeometry(sourceSize)
      ..layout(const ui.Size(360, 360), 2);
    expect(
        ImageEditorService.hasEdits(
            geometry.normalizedCrop, PhotoFilter.original),
        isFalse);
    final scale = geometry.scale * 2;
    geometry.transform(scale,
        geometry.frame.center - sourceSize.center(ui.Offset.zero) * scale);
    expect(geometry.sourceRect.width, closeTo(200, .000001));
    expect(geometry.sourceRect.height, closeTo(100, .000001));
    geometry.transform(scale, const ui.Offset(10000, -10000));
    expect(geometry.sourceRect.left, closeTo(0, .000001));
    expect(geometry.sourceRect.bottom, closeTo(200, .000001));
    geometry.transform(.00001, const ui.Offset(-10000, 10000));
    expect(geometry.sourceRect, const ui.Rect.fromLTWH(0, 0, 400, 200));
  });

  test('free crop handles resize independently; fixed handles preserve ratio',
      () {
    for (final aspect in <double?>[null, 3 / 4, 16 / 9]) {
      final geometry = CropGeometry(sourceSize)
        ..layout(const ui.Size(360, 360), aspect);
      final initial = geometry.sourceRect;
      geometry.resizeCorner(
          0, geometry.frame.topLeft + const ui.Offset(75, 20));
      expect(geometry.sourceRect.width, lessThan(initial.width));
      if (aspect != null) {
        expect(geometry.sourceRect.size.aspectRatio, closeTo(aspect, .000001));
      } else {
        expect(geometry.sourceRect.size.aspectRatio, isNot(closeTo(2, .001)));
      }
      for (var corner = 0; corner < 4; corner++) {
        geometry.resizeCorner(corner, const ui.Offset(-10000, 10000));
        expect(geometry.sourceRect.left, greaterThanOrEqualTo(0));
        expect(geometry.sourceRect.right, lessThanOrEqualTo(400));
        expect(geometry.sourceRect.top, greaterThanOrEqualTo(0));
        expect(geometry.sourceRect.bottom, lessThanOrEqualTo(200));
        expect(geometry.sourceRect.isEmpty, isFalse);
      }
    }
  });

  test('fractional compact viewports keep valid pan bounds without throwing', () {
    for (final height in [72.1, 92.7, 128.8, 190.2, 214.2, 231.6, 359.9]) {
      for (final ratio in [.5, .45, 16 / 9, 4 / 3, 1.0]) {
        final geometry = CropGeometry(const ui.Size(40, 40))
          ..layout(ui.Size(288, height), ratio, padding: 12);
        geometry.transform(geometry.scale, const ui.Offset(10000, -10000));
        final crop = geometry.normalizedCrop;
        expect(crop.isEmpty, isFalse);
        expect(crop.left, greaterThanOrEqualTo(0));
        expect(crop.top, greaterThanOrEqualTo(0));
        expect(crop.right, lessThanOrEqualTo(1));
        expect(crop.bottom, lessThanOrEqualTo(1));
      }
    }
  });

  test('resizing the viewport retains the selected source region', () {
    final geometry = CropGeometry(sourceSize)
      ..layout(const ui.Size(360, 360), 1);
    geometry.transform(geometry.scale * 2, geometry.offset);
    final before = geometry.normalizedCrop;
    geometry.layout(const ui.Size(540, 280), 1);
    expect(geometry.normalizedCrop.left, closeTo(before.left, .000001));
    expect(geometry.normalizedCrop.top, closeTo(before.top, .000001));
    expect(geometry.normalizedCrop.width, closeTo(before.width, .000001));
    expect(geometry.normalizedCrop.height, closeTo(before.height, .000001));
  });

  test('registration tags are sorted and empty selection supplies the fallback',
      () {
    expect(registrationTags({}), [unspecifiedGalleryTag]);
    expect(selectableGalleryTags, isNot(contains(unspecifiedGalleryTag)));
    expect(registrationTags({'SF', '풍경', '게임'}), ['게임', '풍경', 'SF']);
    expect(selectableGalleryTags.take(12).toList(),
        orderedEquals(selectableGalleryTags.take(12).toList()..sort()));
    expect(selectableGalleryTags.last, 'SF');
  });

  Future<ui.Image> fixture() async {
    final recorder = ui.PictureRecorder();
    final canvas = ui.Canvas(recorder);
    canvas.drawRect(const ui.Rect.fromLTWH(0, 0, 4, 4),
        ui.Paint()..color = const ui.Color(0xFFA04010));
    canvas.drawRect(const ui.Rect.fromLTWH(4, 0, 4, 4),
        ui.Paint()..color = const ui.Color(0xFF1040A0));
    final picture = recorder.endRecording();
    final image = await picture.toImage(8, 4);
    picture.dispose();
    return image;
  }

  test('export uses the crop region and bakes distinct filters into PNG pixels',
      () async {
    final source = await fixture();
    addTearDown(source.dispose);
    final colors = <PhotoFilter, List<int>>{};
    for (final filter in PhotoFilter.values) {
      final png = await ImageEditorService.renderPng(
          source, const ui.Rect.fromLTWH(.5, 0, .5, 1), filter);
      final codec = await ui.instantiateImageCodec(png);
      final image = (await codec.getNextFrame()).image;
      expect(image.width, 4);
      expect(image.height, 4);
      final rgba = (await image.toByteData(format: ui.ImageByteFormat.rawRgba))!
          .buffer
          .asUint8List();
      // Sample inside the crop, away from resampling at its boundary.
      final pixel = (image.width * 2 + 2) * 4;
      colors[filter] = rgba.sublist(pixel, pixel + 4);
      expect(rgba[pixel + 3], 255);
      image.dispose();
      codec.dispose();
    }
    expect(colors[PhotoFilter.original], [16, 64, 160, 255]);
    final mono = colors[PhotoFilter.monochrome]!;
    expect((mono[0] - mono[1]).abs(), lessThanOrEqualTo(1));
    expect((mono[1] - mono[2]).abs(), lessThanOrEqualTo(1));
    final sepia = colors[PhotoFilter.sepia]!;
    expect(sepia[0], greaterThan(sepia[1]));
    expect(sepia[1], greaterThan(sepia[2]));
    expect(colors.values.map((rgba) => rgba.join(',')).toSet().length,
        PhotoFilter.values.length);
  });

  test('layer crop and every filter preserve transparent and soft alpha pixels',
      () async {
    final recorder = ui.PictureRecorder();
    final canvas = ui.Canvas(recorder);
    canvas.drawRect(const ui.Rect.fromLTWH(0, 0, 4, 4),
        ui.Paint()..color = const ui.Color(0x80336699));
    final picture = recorder.endRecording();
    final source = await picture.toImage(8, 4);
    picture.dispose();
    addTearDown(source.dispose);
    for (final filter in PhotoFilter.values) {
      final png = await ImageEditorService.renderPng(
          source, const ui.Rect.fromLTWH(.25, 0, .5, 1), filter);
      final codec = await ui.instantiateImageCodec(png);
      final image = (await codec.getNextFrame()).image;
      expect(image.width, 4);
      expect(image.height, 4);
      final pixels = (await image.toByteData(format: ui.ImageByteFormat.rawRgba))!
          .buffer.asUint8List();
      expect(pixels[(4 * 2) * 4 + 3], 128, reason: filter.name);
      expect(pixels[(4 * 2 + 3) * 4 + 3], 0, reason: filter.name);
      image.dispose();
      codec.dispose();
    }
  });

  test('untouched registration keeps original bytes; edited copy is disposable',
      () async {
    final directory =
        await Directory.systemTemp.createTemp('upscale-editor-test-');
    addTearDown(() => directory.delete(recursive: true));
    final image = await fixture();
    final data = (await image.toByteData(format: ui.ImageByteFormat.png))!;
    image.dispose();
    final originalBytes = data.buffer.asUint8List();
    final original =
        await File('${directory.path}/source.png').writeAsBytes(originalBytes);
    final untouched = await ImageEditorService.export(
        sourcePath: original.path,
        crop: const ui.Rect.fromLTWH(0, 0, 1, 1),
        filter: PhotoFilter.original);
    expect(untouched, isNull);
    final edited = (await ImageEditorService.export(
        sourcePath: original.path,
        crop: const ui.Rect.fromLTWH(0, 0, .5, 1),
        filter: PhotoFilter.monochrome))!;
    expect(edited.width, 4);
    expect(edited.height, 4);
    expect(await File(edited.path).exists(), isTrue);
    expect(await original.readAsBytes(), originalBytes);
    final preview = await ImageEditorService.loadPreview(edited.path);
    expect(preview.size, const ui.Size(4, 4));
    preview.preview.dispose();
    await edited.dispose();
    expect(await edited.directory.exists(), isFalse);
    expect(await original.readAsBytes(), originalBytes);
  });

  test('preview is bounded while export retains source pixel resolution',
      () async {
    final directory =
        await Directory.systemTemp.createTemp('upscale-resolution-test-');
    addTearDown(() => directory.delete(recursive: true));
    final recorder = ui.PictureRecorder();
    ui.Canvas(recorder).drawColor(const ui.Color(0xFF3B78C2), ui.BlendMode.src);
    final picture = recorder.endRecording();
    final source = await picture.toImage(2000, 1000);
    final bytes = (await source.toByteData(format: ui.ImageByteFormat.png))!;
    source.dispose();
    picture.dispose();
    final file = await File('${directory.path}/large.png')
        .writeAsBytes(bytes.buffer.asUint8List());
    final preview = await ImageEditorService.loadPreview(file.path);
    expect(preview.size, const ui.Size(2000, 1000));
    expect(preview.preview.width, 1600);
    expect(preview.preview.height, 800);
    preview.preview.dispose();
    final result = (await ImageEditorService.export(
        sourcePath: file.path,
        crop: const ui.Rect.fromLTWH(.25, 0, .5, 1),
        filter: PhotoFilter.original))!;
    expect(result.width, 1000);
    expect(result.height, 1000);
    await result.dispose();
  });
}
