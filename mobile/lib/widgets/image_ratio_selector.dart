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
    this.automaticOnly = false,
  });

  final ImageRatioSettings value;
  final Size display;
  final ValueChanged<ImageRatioSettings>? onChanged;
  final bool allowFree;
  final bool automaticOnly;

  bool get _portrait => automaticOnly && value.automatic
      ? display.height >= display.width : value.portrait;

  bool _isSelected(CropPreset preset) =>
      (!automaticOnly || !value.automatic) && value.preset == preset;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (automaticOnly)
            Row(children: [
              const Expanded(child: AppSectionTitle('비율')),
              _orientation(context, true),
              _orientation(context, false),
              _automaticMode(context),
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
          if (value.automatic && !automaticOnly)
            Text(
              '기기 화면 비율 자동 적용 · ${display.width.round()}:${display.height.round()}',
              textAlign: TextAlign.center,
              style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant, fontSize: 12),
            )
          else ...[
            if (!automaticOnly)
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
                        selected: _isSelected(preset),
                        child: TextButton(
                          key: ValueKey(preset),
                          style: TextButton.styleFrom(
                            minimumSize: automaticOnly ? const Size(0, 32) : null,
                            padding: automaticOnly
                                ? const EdgeInsets.symmetric(horizontal: 10, vertical: 6)
                                : null,
                            tapTargetSize: automaticOnly ? MaterialTapTargetSize.shrinkWrap : null,
                            foregroundColor: _isSelected(preset)
                                ? Theme.of(context).colorScheme.primary
                                : Theme.of(context).colorScheme.onSurfaceVariant,
                            backgroundColor: _isSelected(preset)
                                ? Theme.of(context).colorScheme.primaryContainer
                                : Colors.transparent,
                            shape: const StadiumBorder(),
                          ),
                          onPressed: onChanged == null
                              ? null
                              : () =>
                                  onChanged!(value.copyWith(preset: preset,
                                      automatic: automaticOnly ? false : null,
                                      portrait: _portrait)),
                          child: Text(preset.label(_portrait)),
                        ),
                      ),
                    ),
              ]),
            ),
          ],
        ],
      );

  Widget _orientation(BuildContext context, bool portrait) => Semantics(
        selected: _portrait == portrait,
        child: IconButton(
          constraints: automaticOnly
              ? const BoxConstraints.tightFor(width: 32, height: 32) : null,
          padding: EdgeInsets.all(automaticOnly ? 4 : 8),
          tooltip: portrait ? '세로 방향' : '가로 방향',
          onPressed: onChanged == null
              ? null
              : () => onChanged!(value.copyWith(portrait: portrait,
                  automatic: automaticOnly ? false : null)),
          icon: Icon(
              portrait
                  ? Icons.stay_current_portrait
                  : Icons.stay_current_landscape,
              color: _portrait == portrait
                  ? Theme.of(context).colorScheme.primary
                  : Theme.of(context).colorScheme.onSurfaceVariant),
        ),
      );

  Widget _automaticMode(BuildContext context) => Semantics(
        selected: value.automatic,
        child: TextButton(
          key: const ValueKey('ratio-mode-automatic'),
          onPressed: onChanged == null
              ? null
              : () => onChanged!(value.copyWith(automatic: true)),
          style: TextButton.styleFrom(
            minimumSize: Size.zero,
            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
            tapTargetSize: MaterialTapTargetSize.shrinkWrap,
            backgroundColor: Colors.transparent,
            foregroundColor: value.automatic
                ? Theme.of(context).colorScheme.primary
                : Theme.of(context).colorScheme.onSurfaceVariant,
          ),
          child: Text('자동',
              style: TextStyle(fontWeight: value.automatic
                  ? FontWeight.w700 : FontWeight.w400)),
        ),
      );
}
