import 'dart:async';
import 'dart:io';
import 'package:flutter/material.dart';

import 'models/live_layer_models.dart';
import 'models/character_layer_diagnostics.dart';
import 'services/api_client.dart';
import 'services/live_2d_service.dart';
import 'widgets/character_live2d_preview.dart';

/// Character experimental preview: explicit server-side part splitting.
/// It does not replace the Android two-layer live wallpaper workflow.
class CharacterLive2DPage extends StatefulWidget {
  const CharacterLive2DPage({
    super.key,
    required this.apiClient,
    required this.sourcePath,
    required this.title,
    required this.width,
    required this.height,
  });

  final ApiClient apiClient;
  final String sourcePath;
  final String title;
  final int width;
  final int height;

  @override
  State<CharacterLive2DPage> createState() => _CharacterLive2DPageState();
}

class _CharacterLive2DPageState extends State<CharacterLive2DPage> {
  StreamSubscription<dynamic>? _tiltSubscription;
  LiveLayerProject? _project;
  String? _projectId;
  String? _error;
  String _message = '캐릭터 파츠 분리 준비';
  int _progress = 0;
  bool _busy = false;
  bool _manual = false;
  Offset _tilt = Offset.zero;
  Offset _laggedTilt = Offset.zero;
  Offset _manualTilt = Offset.zero;
  double _sensitivity = 1;
  double _headPivot = .34;
  bool _showOriginal = false;
  bool _enableMotion = true;
  String _visibleLayerType = '전체 합성';

  @override
  void initState() {
    super.initState();
    _tiltSubscription = Live2DService.tilt.listen((event) {
      if (!mounted || event is! List || event.length != 2) return;
      final x = event[0];
      final y = event[1];
      if (x is! num || y is! num || !x.isFinite || !y.isFinite) return;
      final current = Offset(x.toDouble().clamp(-1, 1).toDouble(),
          y.toDouble().clamp(-1, 1).toDouble());
      setState(() {
        _tilt = current;
        _laggedTilt = Offset.lerp(_laggedTilt, current, .16)!;
      });
    }, onError: (_) {
      if (mounted) setState(() => _tilt = Offset.zero);
    });
  }

  @override
  void dispose() {
    _tiltSubscription?.cancel();
    super.dispose();
  }

  Future<void> _split() async {
    if (_busy || _projectId != null) return;
    setState(() {
      _busy = true;
      _error = null;
      _message = '서버에 캐릭터 이미지 업로드 중…';
      _progress = 0;
    });
    try {
      final project = await widget.apiClient.createProject(
        title: widget.title,
        filePath: widget.sourcePath,
        width: widget.width,
        height: widget.height,
      );
      if (!mounted) return;
      setState(() {
        // Keep ID even if processing fails, so user can delete the upload.
        _projectId = project.id;
        _message = '캐릭터 세부 파츠 분리 요청 중…';
      });
      await widget.apiClient.startProcessing(project.id);
      await _monitor(project.id);
    } catch (error) {
      if (mounted) {
        setState(() {
          _message = '처리 중단';
          _error = '캐릭터 파츠 분리에 실패했습니다. $error';
        });
      }
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  Future<void> _monitor(String id) async {
    // Keep polling bound to this exact project, never pick another image.
    for (var attempt = 0; attempt < 240 && mounted && _projectId == id; attempt++) {
      final state = await widget.apiClient.getProcessingStatus(id);
      if (!mounted || _projectId != id) return;
      final status = state['status']?.toString().toLowerCase() ?? '';
      final progress = (state['progress'] as num?)?.toInt() ?? 0;
      setState(() {
        _progress = progress.clamp(0, 100).toInt();
        _message = status == 'processing' || status == '1'
            ? '눈·얼굴·머리카락 레이어 처리 중…'
            : status == 'queued' || status == '0'
                ? 'GPU 처리 대기 중…'
                : '처리 상태: $status';
      });
      if (status == 'completed' || status == '2') {
        final project = await widget.apiClient.getProject(id);
        if (project.layers.isEmpty) {
          throw StateError('서버에서 반환된 파츠 레이어가 없습니다.');
        }
        if (!mounted || _projectId != id) return;
        setState(() {
          _project = project;
          _showOriginal = false;
          _visibleLayerType = '전체 합성';
          _progress = 100;
          _message = '레이어 ${project.layers.length}개 분리 완료';
        });
        return;
      }
      if (status == 'failed' || status == '3') {
        throw StateError(state['errorMessage']?.toString() ?? '서버에서 파츠 분리에 실패했습니다.');
      }
      await Future<void>.delayed(const Duration(seconds: 3));
    }
    if (mounted && _projectId == id) {
      throw StateError('처리가 오래 걸립니다. 결과 다시 확인을 눌러 상태를 확인하세요.');
    }
  }

  Future<void> _retryStatus() async {
    final id = _projectId;
    if (id == null || _busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await _monitor(id);
    } catch (error) {
      if (mounted) setState(() => _error = '$error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _delete() async {
    final id = _projectId;
    if (id == null || _busy) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('서버 작업 삭제'),
        content: const Text('이 캐릭터의 서버 업로드 파일과 분리 결과를 삭제합니다. 휴대폰의 원본 사진은 유지됩니다.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('취소')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('삭제')),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    setState(() => _busy = true);
    try {
      await widget.apiClient.deleteProject(id);
      if (!mounted) return;
      setState(() {
        _project = null;
        _projectId = null;
        _visibleLayerType = '전체 합성';
        _showOriginal = false;
        _progress = 0;
        _message = '서버 작업을 삭제했습니다.';
        _error = null;
      });
    } catch (error) {
      if (mounted) setState(() => _error = '서버 작업을 삭제하지 못했습니다. $error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final project = _project;
    final tilt = _manual ? _manualTilt : _tilt;
    final delayed = _manual ? _manualTilt : _laggedTilt;
    final inspected = project == null ? null : CharacterLayerDiagnostics.inspect(project.layers);
    final layerTypes = project == null
        ? <String>[]
        : project.layers.map((layer) => layer.layerType).toSet().toList();
    layerTypes.sort();
    final aspect = (project?.originalWidth ?? 0) > 0 &&
            (project?.originalHeight ?? 0) > 0
        ? project!.originalWidth / project.originalHeight
        : widget.width / widget.height;
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(title: const Text('캐릭터 Live 2D')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Text('캐릭터 · 얼굴, 눈, 머리카락 파츠별 움직임',
                style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            Text('파츠 분리 시작을 누르면 선택한 이미지를 서버에 업로드합니다. 세부 파츠 분리 서버가 설정되어 있어야 합니다.',
                style: TextStyle(color: scheme.onSurfaceVariant)),
            const SizedBox(height: 12),
            if (_projectId == null)
              FilledButton.icon(
                key: const ValueKey('character-split-start'),
                onPressed: _busy ? null : _split,
                icon: const Icon(Icons.layers_outlined),
                label: const Text('캐릭터 파츠 분리 시작'),
              )
            else ...[
              OutlinedButton(
                key: const ValueKey('character-recheck'),
                onPressed: _busy ? null : _retryStatus,
                child: const Text('현재 이미지 결과 다시 확인'),
              ),
              OutlinedButton.icon(
                key: const ValueKey('character-delete-job'),
                onPressed: _busy ? null : _delete,
                icon: const Icon(Icons.delete_outline),
                label: const Text('서버 작업 삭제'),
              ),
            ],
            if (_busy || _progress > 0) ...[
              const SizedBox(height: 8),
              LinearProgressIndicator(value: _progress > 0 ? _progress / 100 : null),
            ],
            const SizedBox(height: 8),
            Text(_message, textAlign: TextAlign.center),
            if (_error != null) ...[
              const SizedBox(height: 8),
              Text(_error!, style: TextStyle(color: scheme.error)),
            ],
            if (project != null) ...[
              const SizedBox(height: 16),
              Text('실제 이미지 분리 결과 · ${project.layers.length}개 레이어',
                  style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: 8),
              // A two-layer foreground/background result is not a character rig.
              if (inspected?.hasCharacterParts != true) ...[
                Text(
                  '얼굴·눈 등 세부 파츠가 확인되지 않았습니다. '
                  '배경·사물 2레이어 결과라면 캐릭터 고개/눈 개별 움직임은 만들 수 없습니다.',
                  style: TextStyle(color: scheme.error),
                ),
                const SizedBox(height: 8),
              ] else if (inspected?.hasEyes != true || inspected?.hasHair != true) ...[
                Text(
                  '분리된 파츠에 ${inspected?.hasEyes != true ? '눈 ' : ''}${inspected?.hasHair != true ? '머리카락 ' : ''}레이어가 없습니다. '
                  '해당 부위의 독립 움직임은 표시되지 않습니다.',
                  style: TextStyle(color: scheme.onSurfaceVariant),
                ),
                const SizedBox(height: 8),
              ],
              SwitchListTile(
                key: const ValueKey('character-compare-original'),
                title: const Text('원본 사진 보기'),
                subtitle: const Text('분리·변형 전 사용자가 선택한 사진과 비교'),
                value: _showOriginal,
                onChanged: (v) => setState(() => _showOriginal = v),
              ),
              if (!_showOriginal) ...[
                SwitchListTile(
                  key: const ValueKey('character-enable-motion'),
                  title: const Text('자이로 움직임 적용'),
                  subtitle: const Text('끄면 실제 분리된 레이어를 정지 상태로 확인'),
                  value: _enableMotion,
                  onChanged: (v) => setState(() => _enableMotion = v),
                ),
                DropdownButtonFormField<String>(
                  key: const ValueKey('character-inspect-layer'),
                  initialValue: layerTypes.contains(_visibleLayerType)
                      ? _visibleLayerType : '전체 합성',
                  decoration: const InputDecoration(labelText: '분리된 파츠 확인'),
                  items: [
                    const DropdownMenuItem(value: '전체 합성', child: Text('전체 합성')),
                    for (final type in layerTypes)
                      DropdownMenuItem(value: type, child: Text(type)),
                  ],
                  onChanged: (v) => setState(() => _visibleLayerType = v ?? '전체 합성'),
                ),
              ],
              const SizedBox(height: 12),
              Center(child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 440),
                child: AspectRatio(
                  aspectRatio: aspect > 0 ? aspect : 3 / 4,
                  child: _showOriginal
                      ? Image.file(
                          File(widget.sourcePath),
                          fit: BoxFit.contain,
                          errorBuilder: (_, __, ___) => const Center(
                            child: Text('원본 이미지를 표시할 수 없습니다.'),
                          ),
                        )
                      : CharacterLive2DPreview(
                          key: const ValueKey('character-motion-preview'),
                          layers: project.layers,
                          tilt: _enableMotion ? tilt : Offset.zero,
                          laggedTilt: _enableMotion ? delayed : Offset.zero,
                          sensitivity: _sensitivity,
                          headAnchorY: _headPivot,
                          showOnlyType: _visibleLayerType == '전체 합성'
                              ? null : _visibleLayerType,
                        ),
                ),
              )),
              const SizedBox(height: 12),
              const Text('선택한 실제 사진의 분리 결과입니다. 휴대폰을 기울여 움직임을 확인하세요.',
                  textAlign: TextAlign.center),
              SwitchListTile(
                title: const Text('수동 기울기 조절'),
                value: _manual,
                onChanged: (value) => setState(() => _manual = value),
              ),
              if (_manual) ...[
                const Text('좌우 기울기'),
                Slider(value: _manualTilt.dx, min: -1, max: 1,
                    onChanged: (v) => setState(() => _manualTilt = Offset(v, _manualTilt.dy))),
                const Text('상하 기울기'),
                Slider(value: _manualTilt.dy, min: -1, max: 1,
                    onChanged: (v) => setState(() => _manualTilt = Offset(_manualTilt.dx, v))),
              ],
              Text('움직임 강도 ${_sensitivity.toStringAsFixed(1)}배'),
              Slider(value: _sensitivity, min: 0, max: 1.5,
                  onChanged: (v) => setState(() => _sensitivity = v)),
              Text('고개 회전 중심 높이 ${(100 * _headPivot).round()}%'),
              Slider(value: _headPivot, min: .15, max: .65,
                  onChanged: (v) => setState(() => _headPivot = v)),
              const SizedBox(height: 8),
              Text('캐릭터는 현재 세부 파츠 움직임 미리보기 단계입니다. 사물용 Live 2D 배경화면 적용 기능은 기존대로 유지됩니다.',
                  style: TextStyle(color: scheme.onSurfaceVariant)),
            ] else ...[
              const SizedBox(height: 24),
              Text('선택한 사진 · 분리 전 원본',
                  style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: 8),
              Center(child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 440),
                child: AspectRatio(
                  aspectRatio: aspect > 0 ? aspect : 3 / 4,
                  child: Image.file(
                    File(widget.sourcePath),
                    fit: BoxFit.contain,
                    errorBuilder: (_, __, ___) => const Center(
                      child: Text('원본 이미지를 표시할 수 없습니다.'),
                    ),
                  ),
                ),
              )),
              const SizedBox(height: 8),
              const Center(child: Text('세부 파츠가 반환되면 이 사진의 레이어로 미리보기가 바뀝니다.')),
            ],
          ],
        ),
      ),
    );
  }
}
