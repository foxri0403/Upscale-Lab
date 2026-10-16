import 'package:flutter/material.dart';

import '../models/image_editor.dart';
import '../models/image_ratio.dart';
import 'app_components.dart';

class ImageRatioSelector extends StatelessWidget {
  const ImageRatioSelector({
    super.key,
    required this.value,
    required this.display,
    required this.onChanged,
    this.allowFree = false,
    this.compactHeader = false,
  });

  final ImageRatioSettings value;
  final Size display;
  final ValueChanged<ImageRatioSettings>? onChanged;
  final bool allowFree;
  final bool compactHeader;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (compactHeader)
            Row(children: [
              const Expanded(child: AppSectionTitle('비율')),
              _mode(context, true),
              _mode(context, false),
            ])
          else ...[
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
          ],
          const SizedBox(height: 8),
          if (value.automatic)
            Text(
              '기기 화면 비율 자동 적용 · ${display.width.round()}:${display.height.round()}',
              textAlign: TextAlign.center,
              style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant, fontSize: 12),
            )
          else ...[
            Row(mainAxisAlignment: MainAxisAlignment.center, children: [
              _orientation(context, true),
              const SizedBox(width: 12),
              _orientation(context, false),
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
                                ? Theme.of(context).colorScheme.primary
                                : Theme.of(context).colorScheme.onSurfaceVariant,
                            backgroundColor: value.preset == preset
                                ? Theme.of(context).colorScheme.primaryContainer
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

  Widget _orientation(BuildContext context, bool portrait) => Semantics(
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
                  ? Theme.of(context).colorScheme.primary
                  : Theme.of(context).colorScheme.onSurfaceVariant),
        ),
      );

  Widget _mode(BuildContext context, bool automatic) => Semantics(
        selected: value.automatic == automatic,
        child: TextButton(
          key: ValueKey('ratio-mode-${automatic ? 'automatic' : 'manual'}'),
          onPressed: onChanged == null
              ? null
              : () => onChanged!(value.copyWith(automatic: automatic)),
          style: TextButton.styleFrom(
            minimumSize: Size.zero,
            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
            tapTargetSize: MaterialTapTargetSize.shrinkWrap,
            backgroundColor: Colors.transparent,
            foregroundColor: value.automatic == automatic
                ? Theme.of(context).colorScheme.primary
                : Theme.of(context).colorScheme.onSurfaceVariant,
          ),
          child: Text(automatic ? '자동' : '수동',
              style: TextStyle(fontWeight: value.automatic == automatic
                  ? FontWeight.w700 : FontWeight.w400)),
        ),
      );
}
