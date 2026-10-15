import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

/// Android returns the full display, including system bars, even in split screen.
/// The Flutter view is a fallback for tests and platforms without the channel.
class DeviceDisplayBuilder extends StatefulWidget {
  const DeviceDisplayBuilder({super.key, required this.builder});

  final Widget Function(BuildContext context, Size display) builder;

  @override
  State<DeviceDisplayBuilder> createState() => _DeviceDisplayBuilderState();
}

class _DeviceDisplayBuilderState extends State<DeviceDisplayBuilder>
    with WidgetsBindingObserver {
  static const _channel = MethodChannel('live_layer/wallpaper');
  Size? _display;
  int _revision = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _refresh();
  }

  @override
  void didChangeMetrics() => _refresh();

  Future<void> _refresh() async {
    final revision = ++_revision;
    Size? display;
    try {
      final values =
          await _channel.invokeMapMethod<String, Object?>('getDisplaySize');
      final width = (values?['width'] as num?)?.toDouble();
      final height = (values?['height'] as num?)?.toDouble();
      if (width != null &&
          height != null &&
          width.isFinite &&
          height.isFinite &&
          width > 0 &&
          height > 0) {
        display = Size(width, height);
      }
    } on MissingPluginException {
      // Flutter view fallback.
    } on PlatformException {
      // Flutter view fallback.
    }
    if (mounted && revision == _revision) {
      setState(() => _display = display);
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final view = View.of(context);
    final size = _display ?? view.physicalSize;
    return widget.builder(
        context, size.isEmpty ? MediaQuery.sizeOf(context) : size);
  }
}
