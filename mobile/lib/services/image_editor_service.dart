import 'dart:io';
import 'dart:math' as math;
import 'dart:typed_data';
import 'dart:ui' as ui;

import '../models/image_editor.dart';

class EditorImageSource {
  const EditorImageSource(this.preview, this.size);
  final ui.Image preview;
  final ui.Size size;
}

/// A temporary copy owned by the registration flow until native import finishes.
class EditedImageFile {
  const EditedImageFile(this.directory, this.width, this.height);
  final Directory directory;
  final int width;
  final int height;
  String get path => '${directory.path}${Platform.pathSeparator}edited.png';

  Future<void> dispose() async {
    try {
      if (await directory.exists()) await directory.delete(recursive: true);
    } on FileSystemException {
      // Cache cleanup must not turn a completed native import into a failure.
    }
  }
}

abstract final class ImageEditorService {
  static Future<EditorImageSource> loadPreview(String path) async {
    final buffer = await ui.ImmutableBuffer.fromFilePath(path);
    ui.ImageDescriptor? descriptor;
    ui.Codec? codec;
    try {
      descriptor = await ui.ImageDescriptor.encoded(buffer);
      final size =
          ui.Size(descriptor.width.toDouble(), descriptor.height.toDouble());
      final factor = math.min(1.0, 1600 / math.max(size.width, size.height));
      codec = await descriptor.instantiateCodec(
          targetWidth: math.max(1, (size.width * factor).round()),
          targetHeight: math.max(1, (size.height * factor).round()));
      final frame = await codec.getNextFrame();
      return EditorImageSource(frame.image, size);
    } finally {
      codec?.dispose();
      descriptor?.dispose();
      buffer.dispose();
    }
  }

  static bool hasEdits(ui.Rect crop, PhotoFilter filter) =>
      filter != PhotoFilter.original ||
      crop.left.abs() > .000001 ||
      crop.top.abs() > .000001 ||
      (crop.right - 1).abs() > .000001 ||
      (crop.bottom - 1).abs() > .000001;

  static Future<Uint8List> renderPng(
      ui.Image source, ui.Rect crop, PhotoFilter filter) async {
    final width = math.max(1, (crop.width * source.width).round());
    final height = math.max(1, (crop.height * source.height).round());
    final recorder = ui.PictureRecorder();
    final canvas = ui.Canvas(recorder);
    final paint = ui.Paint()
      ..filterQuality = ui.FilterQuality.high
      ..colorFilter = ui.ColorFilter.matrix(filter.matrix);
    canvas.drawImageRect(
        source,
        ui.Rect.fromLTRB(crop.left * source.width, crop.top * source.height,
            crop.right * source.width, crop.bottom * source.height),
        ui.Rect.fromLTWH(0, 0, width.toDouble(), height.toDouble()),
        paint);
    final picture = recorder.endRecording();
    ui.Image? output;
    try {
      output = await picture.toImage(width, height);
      final bytes = await output.toByteData(format: ui.ImageByteFormat.png);
      if (bytes == null) throw StateError('이미지를 저장하지 못했습니다.');
      return bytes.buffer.asUint8List(bytes.offsetInBytes, bytes.lengthInBytes);
    } finally {
      output?.dispose();
      picture.dispose();
    }
  }

  static Future<EditedImageFile?> export({
    required String sourcePath,
    required ui.Rect crop,
    required PhotoFilter filter,
  }) async {
    if (!hasEdits(crop, filter)) return null;
    final codec =
        await ui.instantiateImageCodec(await File(sourcePath).readAsBytes());
    ui.Image? source;
    EditedImageFile? result;
    try {
      source = (await codec.getNextFrame()).image;
      final bytes = await renderPng(source, crop, filter);
      result = EditedImageFile(
          await Directory.systemTemp.createTemp('upscale-lab-edit-'),
          math.max(1, (crop.width * source.width).round()),
          math.max(1, (crop.height * source.height).round()));
      await File(result.path).writeAsBytes(bytes, flush: true);
      return result;
    } catch (_) {
      await result?.dispose();
      rethrow;
    } finally {
      source?.dispose();
      codec.dispose();
    }
  }
}
