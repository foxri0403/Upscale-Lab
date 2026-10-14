import 'package:flutter/material.dart';

import '../models/image_editor.dart';
import '../models/image_ratio.dart';
import '../theme/app_theme.dart';
import 'app_components.dart';

class ImageRatioSelector extends StatelessWidget {
  const ImageRatioSelector({
    super.key,
    required this.value,
    required this.display,
    required this.onChanged,
    this.allowFree = false,
  });

  final ImageRatioSettings value;
  final Size display;
  final ValueChanged<ImageRatioSettings>? onChanged;
  final bool allowFree;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const AppSectionTitle('비율'),
          const SizedBox(height: 8),
          SegmentedButton<bool>(
            segments: const [
              ButtonSegment(value: true, label: Text('자동')),
              ButtonSegment(value: false, label: Text('수동')),
            ],
            selected: {value.automatic},
            onSelectionChanged: onChanged == null
                ? null
                : (selected) =>
                    onChanged!(value.copyWith(automatic: selected.single)),
          ),
          const SizedBox(height: 8),
          if (value.automatic)
            Text(
              '기기 화면 비율 자동 적용 · ${display.width.round()}:${display.height.round()}',
              textAlign: TextAlign.center,
              style: const TextStyle(color: AppTheme.muted, fontSize: 12),
            )
          else ...[
            Row(mainAxisAlignment: MainAxisAlignment.center, children: [
              _orientation(true),
              const SizedBox(width: 12),
              _orientation(false),
            ]),
            SingleChildScrollView(
              key: const ValueKey('crop-ratios'),
              scrollDirection: Axis.horizontal,
              child: Row(children: [
                for (final preset in CropPreset.values)
                  if (preset != CropPreset.free ||
                      allowFree ||
                      value.preset == CropPreset.free)
                    Padding(
                      padding: const EdgeInsets.only(right: 6),
                      child: Semantics(
                        selected: value.preset == preset,
                        child: TextButton(
                          key: ValueKey(preset),
                          style: TextButton.styleFrom(
                            foregroundColor: value.preset == preset
                                ? AppTheme.primary
                                : AppTheme.muted,
                            backgroundColor: value.preset == preset
                                ? AppTheme.tint
                                : Colors.transparent,
                            shape: const StadiumBorder(),
                          ),
                          onPressed: onChanged == null
                              ? null
                              : () =>
                                  onChanged!(value.copyWith(preset: preset)),
                          child: Text(preset.label(value.portrait)),
                        ),
                      ),
                    ),
              ]),
            ),
          ],
        ],
      );

  Widget _orientation(bool portrait) => Semantics(
        selected: value.portrait == portrait,
        child: IconButton(
          tooltip: portrait ? '세로 방향' : '가로 방향',
          onPressed: onChanged == null
              ? null
              : () => onChanged!(value.copyWith(portrait: portrait)),
          icon: Icon(
              portrait
                  ? Icons.stay_current_portrait
                  : Icons.stay_current_landscape,
              color: value.portrait == portrait
                  ? AppTheme.primary
                  : AppTheme.muted),
        ),
      );
}
