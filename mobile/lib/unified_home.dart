import 'dart:io';
import 'dart:async';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:image_picker/image_picker.dart';

import 'image_registration_page.dart';
import 'live_2d_page.dart';
import 'services/live_2d_service.dart';
import 'widgets/live_2d_preview.dart';
import 'gallery_preview_page.dart';
import 'models/gallery.dart';
import 'models/local_image.dart';
import 'models/library_order.dart';
import 'models/image_ratio.dart';
import 'services/api_client.dart';
import 'services/local_image_store.dart';
import 'services/wallpaper_service.dart';
import 'theme/app_theme.dart';
import 'widgets/app_components.dart';
import 'widgets/device_display_builder.dart';
import 'widgets/gallery_tag_picker.dart';
import 'widgets/wallpaper_remove_dialog.dart';
import 'widgets/local_image_card.dart';

export 'image_registration_page.dart';
export 'gallery_preview_page.dart';

class GallerySearchSelection {
  String search = '';
  final Set<String> tags = {};
}

class UnifiedHomePage extends StatefulWidget {
  const UnifiedHomePage({
    super.key,
    required this.apiClient,
    required this.onLogout,
  });

  final ApiClient apiClient;
  final Future<void> Function(BuildContext context) onLogout;

  @override
  State<UnifiedHomePage> createState() => _UnifiedHomePageState();
}

class _UnifiedHomePageState extends State<UnifiedHomePage> {
  late final _localImageStore = LocalImageStore(
      scope: widget.apiClient.storageScope, ownerId: widget.apiClient.userId);
  late Future<List<LocalImageItem>> _images = _loadLibrary();
  LibraryOrder _order = const LibraryOrder();
  bool _orderLoaded = false;
  Future<void> _orderWrites = Future.value();
  final _libraryScroll = ScrollController();
  final _libraryViewport = GlobalKey();
  Timer? _dragScroll;
  double _scrollVelocity = 0;

  Future<List<LocalImageItem>> _loadLibrary() async {
    final images = await _localImageStore.getAll();
    if (!_orderLoaded) {
      _order = await _localImageStore.getOrder();
      _orderLoaded = true;
    }
    return images;
  }

  void _saveOrder() {
    final order = _order;
    _orderWrites = _orderWrites
        .then((_) => _localImageStore.saveOrder(order))
        .catchError((Object error) {
      _message('이미지 순서를 저장하지 못했습니다. 다시 시도해 주세요.');
    });
  }

  void _beginDrag(List<LocalImageItem> displayed) {
    setState(() => _order = _order.beginDrag(displayed));
    _saveOrder();
    _dragScroll?.cancel();
    _dragScroll = Timer.periodic(const Duration(milliseconds: 20), (_) {
      if (!_libraryScroll.hasClients || _scrollVelocity == 0) return;
      final position = _libraryScroll.position;
      _libraryScroll.jumpTo((_libraryScroll.offset + _scrollVelocity)
          .clamp(position.minScrollExtent, position.maxScrollExtent));
    });
  }

  void _updateDrag(DragUpdateDetails details) {
    final box =
        _libraryViewport.currentContext?.findRenderObject() as RenderBox?;
    if (box == null) return;
    final y = box.globalToLocal(details.globalPosition).dy;
    _scrollVelocity = y < 72 ? -10 : (y > box.size.height - 72 ? 10 : 0);
  }

  void _endDrag() {
    _dragScroll?.cancel();
    _scrollVelocity = 0;
  }

  @override
  void dispose() {
    _endDrag();
    _libraryScroll.dispose();
    super.dispose();
  }

  final Set<String> _busyIds = {};
  bool _creating = false;
  bool _isGrid = true;
  bool _removingWallpaper = false;
  bool _migratingImages = false;

  Future<void> _migrateGalleryImages() async {
    setState(() => _migratingImages = true);
    try {
      final result = await _localImageStore.migrateGalleryImages();
      if (!mounted) return;
      _reload();
      final remaining = (result['remaining'] as num?)?.toInt() ?? 0;
      final failed = (result['failed'] as num?)?.toInt() ?? 0;
      _message(remaining == 0 && failed == 0
          ? '기존 이미지를 앱 저장소로 이전했습니다. 앱에서 만든 갤러리 복사본은 제거했습니다.'
          : '앱 저장소로 복사했습니다. 갤러리에 남은 복사본 ${remaining + failed}개는 권한 확인 후 다시 이전해 주세요.');
    } on PlatformException catch (error) {
      _message(error.message ?? '기존 이미지를 이전하지 못했습니다.');
    } finally {
      if (mounted) setState(() => _migratingImages = false);
    }
  }

  int _selectedIndex = 0;
  int _galleryRevision = 0;
  final _gallerySelection = GallerySearchSelection();

  void _reload() => setState(() {
        _images = _loadLibrary();
      });

  Future<void> _previewHomeWallpaper(LocalImageItem image) async {
    await Navigator.of(context).push<void>(MaterialPageRoute(
      builder: (_) => HomeWallpaperPreviewPage(
        image: image,
        store: _localImageStore,
        onEdit: () => _editImage(image),
        onShare: () => image.isShared ? _unshare(image) : _share(image),
        onDelete: () => _delete(image),
      ),
    ));
    if (mounted) _reload();
  }

  void _message(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }

  Future<void> _createImage() async {
    final picked = await ImagePicker().pickImage(source: ImageSource.gallery);
    if (picked == null || !mounted) return;

    setState(() => _creating = true);
    ImageRegistrationOptions? options;
    String? downloadedUpscalePath;
    try {
      options = await Navigator.of(context).push<ImageRegistrationOptions>(
        MaterialPageRoute(
          builder: (_) => ImageFeaturePage(
            filePath: picked.path,
            title: picked.name,
          ),
        ),
      );
      if (options == null || !mounted) return;

      final edited = options.editedFile;
      final sourcePath = edited?.path ?? picked.path;
      final codec =
          await ui.instantiateImageCodec(await File(sourcePath).readAsBytes());
      late int width;
      late int height;
      try {
        final frame = await codec.getNextFrame();
        width = frame.image.width;
        height = frame.image.height;
        frame.image.dispose();
      } finally {
        codec.dispose();
      }
      if (!mounted) return;

      // Save the original only when upscaling is not requested.
      final scale = options.upscaleFactor;
      final target = options.wallpaperTarget;
      if (scale == null) {
        final saved = await _localImageStore.import(
          sourcePath: sourcePath,
          fileName: edited == null ? picked.name : 'edited.png',
          title: options.title,
          tags: options.tags,
          width: width,
          height: height,
          mode: 'original',
          ratio: options.ratio,
        );
        if (!mounted) return;
        if (options.live2D != null) {
          try {
            await _localImageStore.attachLive2D(saved.id, options.live2D!.id);
          } catch (error) {
            if (mounted) _reload();
            _message('원본은 저장했지만 Live 2D 레이어를 저장하지 못했습니다.\n$error');
            return;
          }
        }
        if (!mounted) return;
        _reload();

        if (target != null) {
          try {
            await _localImageStore.applyWallpaper(saved.id, target: target);
            _message('이미지를 저장하고 배경화면을 적용했습니다.');
          } catch (error) {
            _message('이미지는 저장했지만 배경화면을 적용하지 못했습니다.\n$error');
          }
        } else {
          _message(options.live2D != null
              ? 'Live 2D 이미지를 저장했습니다.'
              : '이미지를 저장했습니다. 배경화면은 변경하지 않았습니다.');
        }
      }

      if (scale != null) {
        try {
          _message('AI $scale배 업스케일링을 처리하고 있습니다...');
          final project = await widget.apiClient.createProject(
            title: options.title,
            filePath: sourcePath,
            width: width,
            height: height,
          );
          await widget.apiClient.startUpscaling(project.id, scale: scale);
          final status = await widget.apiClient.waitForUpscaling(project.id);
          final jobId = status['jobId']?.toString();
          if (jobId == null || jobId.isEmpty) {
            throw StateError('완료된 업스케일링 작업의 jobId가 없습니다.');
          }
          final url =
              await widget.apiClient.getUpscaledDownloadUrl(project.id, jobId);
          downloadedUpscalePath = await _localImageStore.downloadShared(
            url: url,
            fileName: 'upscaled_${scale}x.png',
          );

          // Read actual output dimensions, rather than assuming the scale.
          final resultCodec = await ui.instantiateImageCodec(
              await File(downloadedUpscalePath).readAsBytes());
          late int resultWidth;
          late int resultHeight;
          try {
            final frame = await resultCodec.getNextFrame();
            resultWidth = frame.image.width;
            resultHeight = frame.image.height;
            frame.image.dispose();
          } finally {
            resultCodec.dispose();
          }
          if (resultWidth != width * scale || resultHeight != height * scale) {
            throw StateError('AI 결과 해상도가 예상과 다릅니다: '
                '$resultWidth x $resultHeight (예상: ${width * scale} x ${height * scale}).');
          }

          await _localImageStore.import(
            sourcePath: downloadedUpscalePath,
            fileName: 'upscaled_${scale}x.png',
            title: '${options.title}_AI_${scale}x',
            tags: options.tags,
            width: resultWidth,
            height: resultHeight,
            mode: 'original',
            ratio: options.ratio,
          );
          if (!mounted) return;
          _reload();
          _message('AI $scale배 업스케일링 이미지가 저장됐습니다. '
              '($resultWidth x $resultHeight)');
        } catch (error) {
          _message('AI 업스케일링에 실패했습니다. 원본 이미지는 추가 저장하지 않았습니다.\n$error');
        }
      }
    } on PlatformException catch (error) {
      _message(error.message ?? '$error');
    } catch (error) {
      _message('이미지를 등록하지 못했습니다.\n$error');
    } finally {
      if (downloadedUpscalePath != null) {
        try {
          await File(downloadedUpscalePath).delete();
        } catch (_) {
          // Temporary download may already have been cleared by Android.
        }
      }
      await options?.editedFile?.dispose();
      await Live2DService.discard(options?.live2D);
      if (mounted) setState(() => _creating = false);
    }
  }

  Future<void> _share(LocalImageItem image) async {
    if (image.isShared || _busyIds.contains(image.id)) return;
    setState(() => _busyIds.add(image.id));
    String? projectId;
    String? publishedPostId;
    bool shareRequested = false;
    try {
      final project = await widget.apiClient.createProject(
        title: image.title,
        filePath: image.path,
        width: image.width,
        height: image.height,
      );
      projectId = project.id;
      shareRequested = true;
      final postId = await widget.apiClient.shareProject(
        projectId: project.id,
        title: image.title,
        tags: image.tags,
      );
      publishedPostId = postId;
      await _localImageStore.markShared(
        id: image.id,
        cloudProjectId: project.id,
        galleryPostId: postId,
      );
      if (!mounted) return;
      setState(() => _galleryRevision++);
      _reload();
      _message('클라우드 업로드와 공유를 완료했습니다.');
    } catch (error) {
      // Once sharing was requested, a timeout cannot prove no post was created.
      if (projectId != null && !shareRequested) {
        try {
          await widget.apiClient.deleteProject(projectId);
        } catch (_) {
          // Keep the original share error; rollback is best effort.
        }
      }
      if (publishedPostId != null) {
        _message('클라우드 공유는 완료됐지만 기기의 공유 상태를 저장하지 못했습니다. '
            '공유 게시물은 유지됩니다. 중복 공유 전에 탐색에서 게시 여부를 확인해 주세요.\n$error');
      } else if (shareRequested) {
        _message('공유 결과를 확인하지 못했습니다. 서버의 게시물을 자동 삭제하지 않았습니다. '
            '중복 공유 전에 탐색에서 게시 여부를 확인해 주세요.\n$error');
      } else {
        _message('공유를 완료하지 못했습니다. 로컬 이미지는 유지됩니다.\n$error');
      }
    } finally {
      if (mounted) setState(() => _busyIds.remove(image.id));
    }
  }

  Future<void> _unshare(LocalImageItem image) async {
    if (!image.isShared || _busyIds.contains(image.id)) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('공유 취소'),
        content: Text('검색 화면과 클라우드에서 공유 사본을 삭제할까요? 로컬 이미지는 유지됩니다.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: Text('돌아가기'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: Text('공유 취소'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;

    setState(() => _busyIds.add(image.id));
    try {
      await widget.apiClient.deleteGalleryPost(image.galleryPostId!);
      await widget.apiClient.deleteProject(image.cloudProjectId!);
      await _localImageStore.markUnshared(image.id);
      if (!mounted) return;
      setState(() => _galleryRevision++);
      _reload();
      _message('공유를 취소했습니다. 로컬 이미지는 유지됩니다.');
    } catch (error) {
      _message('공유를 취소하지 못했습니다.\n$error');
    } finally {
      if (mounted) setState(() => _busyIds.remove(image.id));
    }
  }

  Future<void> _editImage(LocalImageItem image) async {
    if (_busyIds.contains(image.id)) return;
    if (image.isShared) {
      _message('공유 중인 이미지는 수정할 수 없습니다. 먼저 공유를 취소해 주세요.');
      return;
    }
    final store = _localImageStore;
    setState(() => _busyIds.add(image.id));
    try {
      await Navigator.of(context).push<void>(MaterialPageRoute(builder: (_) => ImageFeaturePage(
          filePath: image.path, title: image.title, initialTags: image.tags, initialRatio: image.ratio,
          loadLive2D: image.isLive2D ? () => store.editableLive2D(image.id) : null,
          onSave: (options) => _saveImageEdit(image, options, store))));
    } catch (error) {
      _message('이미지를 수정하지 못했습니다.\n$error');
    } finally {
      if (mounted) {
        setState(() => _busyIds.remove(image.id));
        _reload();
      }
    }
  }

  Future<void> _saveImageEdit(LocalImageItem image, ImageRegistrationOptions options, LocalImageStore store) async {
    var source = options.editedFile?.path ?? image.path;
    var width = options.editedFile?.width ?? image.width;
    var height = options.editedFile?.height ?? image.height;
    String? downloaded;
    try {
      if (options.upscaleFactor case final scale?) {
        final project = await widget.apiClient.createProject(title: options.title,
            filePath: source, width: width, height: height);
        await widget.apiClient.startUpscaling(project.id, scale: scale);
        final status = await widget.apiClient.waitForUpscaling(project.id);
        final jobId = status['jobId']?.toString();
        if (jobId == null || jobId.isEmpty) throw StateError('업스케일링 작업 ID가 없습니다.');
        final url = await widget.apiClient.getUpscaledDownloadUrl(project.id, jobId);
        downloaded = await store.downloadShared(url: url, fileName: 'upscaled_${scale}x.png');
        final codec = await ui.instantiateImageCodec(await File(downloaded).readAsBytes());
        try {
          final frame = await codec.getNextFrame();
          try {
            if (frame.image.width != width * scale || frame.image.height != height * scale) {
              throw StateError('AI 결과 해상도가 예상과 다릅니다.');
            }
            width = frame.image.width;
            height = frame.image.height;
          } finally { frame.image.dispose(); }
        } finally { codec.dispose(); }
        source = downloaded;
      }
      final saved = await store.saveEdits(id: image.id, sourcePath: source,
          title: options.title, tags: options.tags, width: width, height: height,
          ratio: options.ratio, draftId: options.live2D?.id);
      if (options.wallpaperTarget case final target?) {
        try {
          await store.applyWallpaper(saved.id, target: target);
        } catch (error) {
          _message('수정은 저장했지만 배경화면을 적용하지 못했습니다.\n$error');
          return;
        }
      }
      _message('이미지 수정을 저장했습니다.');
    } finally {
      if (downloaded != null) {
        try { await File(downloaded).delete(); } catch (_) {}
      }
    }
  }

  Future<void> _removeWallpaper() async {
    if (_removingWallpaper) return;
    final target = await showDialog<WallpaperTarget>(
      context: context,
      builder: (_) => const WallpaperRemoveDialog(),
    );
    if (target == null || !mounted) return;

    setState(() => _removingWallpaper = true);
    try {
      await const WallpaperService().clear(target: target);
      _message('선택한 배경화면을 제거했습니다. 등록된 이미지는 유지됩니다.');
    } on PlatformException catch (error) {
      _message(error.message ?? '$error');
    } finally {
      if (mounted) setState(() => _removingWallpaper = false);
    }
  }

  Future<void> _delete(LocalImageItem image) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('이미지 삭제'),
        content: Text(
          image.isShared
              ? '이 기기에 저장된 이미지만 삭제할까요?\n공유한 이미지는 탐색과 클라우드에 그대로 남습니다.\n공유 게시물을 삭제하려면 별도의 공유 취소를 이용해 주세요.'
              : '이 기기에 저장된 이미지를 삭제할까요? 클라우드에는 영향을 주지 않습니다.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: Text('취소'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: Text('삭제'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;

    setState(() => _busyIds.add(image.id));
    try {
      await _localImageStore.delete(image.id);
      if (!mounted) return;
      _reload();
      _message(image.isShared
          ? '기기에 저장된 이미지만 삭제했습니다. 공유 게시물은 유지됩니다.'
          : '기기에 저장된 이미지를 삭제했습니다.');
    } catch (error) {
      _message('기기의 이미지를 삭제하지 못했습니다. 공유 게시물은 변경되지 않았습니다.\n$error');
    } finally {
      if (mounted) setState(() => _busyIds.remove(image.id));
    }
  }

  Widget _home() => FutureBuilder<List<LocalImageItem>>(
        future: _images,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) {
            return Center(
                child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(mainAxisSize: MainAxisSize.min, children: [
                Icon(Icons.image_not_supported_outlined,
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                    size: 36),
                const SizedBox(height: 12),
                Text(
                  snapshot.error is PlatformException &&
                          (snapshot.error as PlatformException).code ==
                              'gallery_permission_denied'
                      ? '자동 복원을 위해 사진 접근 권한을\n모든 사진 허용으로 변경해 주세요.'
                      : '이미지를 불러오지 못했습니다.',
                  textAlign: TextAlign.center,
                ),
                TextButton(onPressed: _reload, child: Text('다시 불러오기')),
              ]),
            ));
          }
          final images =
              _order.arrange(snapshot.data ?? const <LocalImageItem>[]);
          Widget item(int index) {
            if (index == 0) {
              return NewImageCard(
                isGrid: _isGrid,
                creating: _creating,
                onTap: _creating ? null : _createImage,
              );
            }
            final image = images[index - 1];
            final card = LocalImageCard(
              key: ValueKey(image.id),
              image: image,
              isGrid: _isGrid,
              busy: _busyIds.contains(image.id),
              onTap: () => _previewHomeWallpaper(image),
              onShare: () => _share(image),
              onUnshare: () => _unshare(image),
              onEdit: () => _editImage(image),
              onDelete: () => _delete(image),
            );
            return DragTarget<String>(
              key: ValueKey('reorder-${image.id}'),
              onWillAcceptWithDetails: (details) => details.data != image.id,
              onAcceptWithDetails: (details) {
                setState(() => _order = _order.move(details.data, image.id));
                _saveOrder();
              },
              builder: (context, candidates, rejected) => DecoratedBox(
                decoration: BoxDecoration(
                    border: candidates.isEmpty
                        ? null
                        : Border.all(
                            color: Theme.of(context).colorScheme.primary,
                            width: 2)),
                child: LongPressDraggable<String>(
                  data: image.id,
                  maxSimultaneousDrags: _busyIds.contains(image.id) ? 0 : 1,
                  onDragStarted: () => _beginDrag(images),
                  onDragUpdate: _updateDrag,
                  onDragEnd: (_) => _endDrag(),
                  feedback: Material(
                      elevation: 8,
                      child: SizedBox(
                          width: _isGrid ? 160 : 260,
                          height: _isGrid ? 228 : 104,
                          child: card)),
                  childWhenDragging: Opacity(opacity: 0.25, child: card),
                  child: card,
                ),
              ),
            );
          }

          return LayoutBuilder(builder: (context, constraints) {
            final columns = constraints.maxWidth >= 1100
                ? 5
                : constraints.maxWidth >= 850
                    ? 4
                    : constraints.maxWidth >= 600
                        ? 3
                        : 2;
            final cellWidth =
                (constraints.maxWidth - 40 - 12 * (columns - 1)) / columns;
            final scale = MediaQuery.textScalerOf(context).scale(14) / 14;
            return RefreshIndicator(
              onRefresh: () async {
                final refreshed = _loadLibrary();
                setState(() => _images = refreshed);
                await refreshed;
              },
              child: CustomScrollView(
                key: _libraryViewport,
                controller: _libraryScroll,
                physics: const AlwaysScrollableScrollPhysics(),
                slivers: [
                  SliverToBoxAdapter(
                      child: Padding(
                    padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
                    child: Row(children: [
                      Expanded(
                          child: Text('내 이미지 (${images.length})',
                              style: TextStyle(
                                  fontSize: 18, fontWeight: FontWeight.w700))),
                      PopupMenuButton<LibrarySort>(
                        tooltip: '이미지 정렬',
                        initialValue: _order.sort,
                        onSelected: (sort) {
                          setState(() => _order =
                              LibraryOrder(sort: sort, ids: _order.ids));
                          _saveOrder();
                        },
                        itemBuilder: (_) => LibrarySort.values
                            .map((sort) => PopupMenuItem(
                                value: sort, child: Text(sort.label)))
                            .toList(),
                        child: Padding(
                            padding: const EdgeInsets.symmetric(vertical: 12),
                            child: Text(_order.sort.label,
                                style: TextStyle(
                                    color:
                                        Theme.of(context).colorScheme.primary,
                                    fontWeight: FontWeight.w600))),
                      ),
                    ]),
                  )),
                  SliverPadding(
                    padding: const EdgeInsets.fromLTRB(20, 0, 20, 24),
                    sliver: _isGrid
                        ? SliverGrid(
                            gridDelegate:
                                SliverGridDelegateWithFixedCrossAxisCount(
                              crossAxisCount: columns,
                              crossAxisSpacing: 12,
                              mainAxisSpacing: 12,
                              mainAxisExtent: cellWidth + 68 * scale,
                            ),
                            delegate: SliverChildBuilderDelegate(
                              (context, index) => item(index),
                              childCount: images.length + 1,
                            ),
                          )
                        : SliverList(
                            delegate: SliverChildBuilderDelegate(
                              (context, index) => Column(children: [
                                item(index),
                                if (index > 0 && index < images.length)
                                  const Divider(),
                                if (index == 0) const SizedBox(height: 6),
                              ]),
                              childCount: images.length + 1,
                            ),
                          ),
                  ),
                ],
              ),
            );
          });
        },
      );

  Widget _settings() {
    final colors = Theme.of(context).colorScheme;
    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 0, 20, 28),
      children: [
        const AppSectionTitle('설정'),
        const SizedBox(height: 18),
        Text('화면 설정',
            style: TextStyle(
                color: colors.onSurfaceVariant, fontWeight: FontWeight.w600)),
        const SizedBox(height: 8),
        Card(
            child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
          child: ValueListenableBuilder<ThemeMode>(
            valueListenable: AppTheme.mode,
            builder: (context, mode, _) => Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const ListTile(
                    contentPadding: EdgeInsets.zero,
                    leading: Icon(Icons.palette_outlined),
                    title: Text('화면 테마'),
                    subtitle: Text('시스템 설정 또는 밝고 어두운 화면을 선택하세요.')),
                SegmentedButton<ThemeMode>(
                  showSelectedIcon: false,
                  segments: const [
                    ButtonSegment(value: ThemeMode.system, label: Text('시스템')),
                    ButtonSegment(value: ThemeMode.light, label: Text('라이트')),
                    ButtonSegment(value: ThemeMode.dark, label: Text('다크')),
                  ],
                  selected: {mode},
                  onSelectionChanged: (selected) =>
                      AppTheme.setMode(selected.first),
                ),
                const SizedBox(height: 10),
              ],
            ),
          ),
        )),
        const SizedBox(height: 20),
        Text('앱 및 계정',
            style: TextStyle(
                color: colors.onSurfaceVariant, fontWeight: FontWeight.w600)),
        const SizedBox(height: 8),
        Card(
            child: Column(children: [
          ListTile(
            leading: Icon(Icons.wallpaper_outlined),
            title: Text('배경화면 제거'),
            subtitle: Text('등록한 이미지는 보관됩니다.'),
            trailing: Icon(Icons.chevron_right),
            enabled: !_removingWallpaper,
            onTap: _removingWallpaper ? null : _removeWallpaper,
          ),
          const Divider(height: 1),
          ListTile(
            key: const ValueKey('migrate-gallery-images'),
            leading: const Icon(Icons.folder_copy_outlined),
            title: const Text('기존 갤러리 이미지 이전'),
            subtitle:
                const Text('앱에서 저장한 복사본을 앱 내부로 옮깁니다. 새 이미지는 갤러리에 저장하지 않습니다.'),
            enabled: !_migratingImages,
            trailing: _migratingImages
                ? const SizedBox.square(
                    dimension: 20,
                    child: CircularProgressIndicator(strokeWidth: 2))
                : const Icon(Icons.chevron_right),
            onTap: _migratingImages ? null : _migrateGalleryImages,
          ),
          const Divider(height: 1),
          ListTile(
            leading: Icon(Icons.logout),
            title: Text('로그아웃'),
            trailing: Icon(Icons.chevron_right),
            onTap: () async {
              await _orderWrites;
              if (!mounted) return;
              await widget.onLogout(context);
            },
          ),
        ])),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    final content = switch (_selectedIndex) {
      1 => GallerySearchPage(
          key: ValueKey(_galleryRevision),
          apiClient: widget.apiClient,
          onImported: _reload,
          selection: _gallerySelection,
        ),
      2 => _settings(),
      _ => _home(),
    };
    return Scaffold(
      body: SafeArea(
        bottom: false,
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 14, 14, 20),
            child: Row(children: [
              const Expanded(
                  child: Text(
                'Upscale Lab',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                    fontSize: 24,
                    fontWeight: FontWeight.w800,
                    letterSpacing: -0.8),
              )),
              if (_selectedIndex == 0) ...[
                const SizedBox(width: 12),
                LibraryLayoutToggle(
                    isGrid: _isGrid,
                    onChanged: (value) => setState(() => _isGrid = value)),
              ],
            ]),
          ),
          Expanded(child: content),
        ]),
      ),
      bottomNavigationBar: DecoratedBox(
        decoration: BoxDecoration(
          border: Border(
              top: BorderSide(
                  color: Theme.of(context).colorScheme.outlineVariant)),
        ),
        child: NavigationBar(
          selectedIndex: _selectedIndex,
          onDestinationSelected: (value) => setState(() {
            _selectedIndex = value;
            if (value == 1) _galleryRevision++;
          }),
          destinations: const [
            NavigationDestination(
              icon: Icon(Icons.home_outlined),
              selectedIcon: Icon(Icons.home_rounded),
              label: '홈',
            ),
            NavigationDestination(icon: Icon(Icons.search), label: '탐색'),
            NavigationDestination(
                icon: Icon(Icons.settings_outlined), label: '설정'),
          ],
        ),
      ),
    );
  }
}

class GallerySearchPage extends StatefulWidget {
  const GallerySearchPage(
      {super.key, required this.apiClient, this.onImported, this.selection});

  final ApiClient apiClient;
  final VoidCallback? onImported;
  final GallerySearchSelection? selection;

  @override
  State<GallerySearchPage> createState() => _GallerySearchPageState();
}

class _GallerySearchPageState extends State<GallerySearchPage> {
  late final _searchController =
      TextEditingController(text: widget.selection?.search ?? '');
  late final Set<String> _selectedTags = widget.selection?.tags ?? {};
  late Future<List<GalleryPostItem>> _posts = _load();

  Future<List<GalleryPostItem>> _load() {
    widget.selection?.search = _searchController.text;
    return widget.apiClient.getGallery(
      search: _searchController.text,
      tags: _selectedTags.length == selectableGalleryTags.length
          ? <String>[]
          : _selectedTags.toList(),
    );
  }

  Future<void> _refresh() async {
    final posts = _load();
    setState(() => _posts = posts);
    await posts;
  }

  void _search() => setState(() => _posts = _load());

  @override
  void dispose() {
    widget.selection?.search = _searchController.text;
    _searchController.dispose();
    super.dispose();
  }

  Future<void> _openTagPicker() async {
    final result =
        await showGalleryTagPicker(context, selectedTags: _selectedTags);
    if (result == null || !mounted) return;
    setState(() {
      _selectedTags
        ..clear()
        ..addAll(result);
      // All checked is the same as no filter, but checkboxes remain selected until reset.
      _posts = _load();
    });
  }

  @override
  Widget build(BuildContext context) => RefreshIndicator(
        onRefresh: _refresh,
        child: CustomScrollView(
          physics: const AlwaysScrollableScrollPhysics(),
          slivers: [
            SliverToBoxAdapter(
                child: Padding(
              padding: const EdgeInsets.fromLTRB(12, 0, 12, 12),
              child: Row(children: [
                Expanded(
                    child: TextField(
                  controller: _searchController,
                  textInputAction: TextInputAction.search,
                  onSubmitted: (_) => _search(),
                  decoration: InputDecoration(
                    hintText: '이미지 이름 검색',
                    prefixIcon: const Icon(Icons.search),
                    suffixIcon: IconButton(
                        tooltip: '검색',
                        onPressed: _search,
                        icon: const Icon(Icons.arrow_forward)),
                  ),
                )),
                const SizedBox(width: 8),
                Badge(
                  isLabelVisible: _selectedTags.isNotEmpty,
                  label: Text('${_selectedTags.length}'),
                  child: IconButton.outlined(
                      tooltip: '태그 선택',
                      onPressed: _openTagPicker,
                      icon: const Icon(Icons.tune)),
                ),
              ]),
            )),
            FutureBuilder<List<GalleryPostItem>>(
              future: _posts,
              builder: (context, snapshot) {
                if (snapshot.connectionState != ConnectionState.done) {
                  return const SliverToBoxAdapter(
                      child: Padding(
                    padding: EdgeInsets.all(32),
                    child: Center(child: CircularProgressIndicator()),
                  ));
                }
                if (snapshot.hasError || (snapshot.data?.isEmpty ?? true)) {
                  return SliverToBoxAdapter(
                      child: Padding(
                    padding: const EdgeInsets.all(32),
                    child: Column(children: [
                      Icon(Icons.photo_library_outlined,
                          color: Theme.of(context).colorScheme.onSurfaceVariant,
                          size: 36),
                      const SizedBox(height: 12),
                      Text(
                          snapshot.hasError
                              ? '공유 이미지를 불러오지 못했습니다.'
                              : '조건에 맞는 공유 이미지가 없습니다.',
                          textAlign: TextAlign.center),
                      if (snapshot.hasError)
                        TextButton(
                            onPressed: _refresh, child: const Text('다시 불러오기')),
                    ]),
                  ));
                }
                final posts = snapshot.data!;
                return SliverPadding(
                  padding: const EdgeInsets.fromLTRB(2, 0, 2, 24),
                  sliver: SliverGrid(
                    gridDelegate:
                        const SliverGridDelegateWithFixedCrossAxisCount(
                            crossAxisCount: 2,
                            childAspectRatio: 1,
                            crossAxisSpacing: 2,
                            mainAxisSpacing: 2),
                    delegate: SliverChildBuilderDelegate(
                      (context, index) => _GalleryPostCard(
                          key: ValueKey(posts[index].id),
                          post: posts[index],
                          apiClient: widget.apiClient,
                          onImported: widget.onImported),
                      childCount: posts.length,
                    ),
                  ),
                );
              },
            ),
          ],
        ),
      );
}

class _GalleryPostCard extends StatefulWidget {
  const _GalleryPostCard(
      {super.key,
      required this.post,
      required this.apiClient,
      this.onImported});

  final GalleryPostItem post;
  final ApiClient apiClient;
  final VoidCallback? onImported;

  @override
  State<_GalleryPostCard> createState() => _GalleryPostCardState();
}

class _GalleryPostCardState extends State<_GalleryPostCard> {
  late Future<String> _imageUrl = _initialImageUrl();
  late int _downloadCount = widget.post.downloadCount;
  bool _importing = false;
  bool _loadingPreview = false;

  Future<String> _initialImageUrl() {
    final thumbnail = widget.post.thumbnailUrl;
    if (thumbnail != null) {
      return Future.value(Uri.parse('${widget.apiClient.baseUrl}/')
          .resolve(thumbnail)
          .toString());
    }
    final uri = Uri.tryParse(widget.post.imageUrl);
    if (uri != null &&
        uri.hasAuthority &&
        (uri.scheme == 'https' || uri.scheme == 'http')) {
      return Future.value(widget.post.imageUrl);
    }
    return widget.apiClient.getGalleryImageUrl(widget.post.id);
  }

  @override
  void didUpdateWidget(covariant _GalleryPostCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.post.downloadCount != widget.post.downloadCount) {
      _downloadCount = widget.post.downloadCount;
    }
    if (oldWidget.post.id != widget.post.id ||
        oldWidget.post.thumbnailUrl != widget.post.thumbnailUrl ||
        (widget.post.thumbnailUrl == null &&
            oldWidget.post.imageUrl != widget.post.imageUrl)) {
      _imageUrl = _initialImageUrl();
    }
  }

  Future<void> _import() async {
    if (_importing) return;
    setState(() {
      _importing = true;
      _loadingPreview = true;
    });
    String? path;
    try {
      final store = LocalImageStore(
          scope: widget.apiClient.storageScope,
          ownerId: widget.apiClient.userId);
      final url = await widget.apiClient.getGalleryDownloadUrl(widget.post.id);
      path = await store.downloadShared(
          url: url,
          fileName: widget.post.fileName ?? Uri.parse(url).pathSegments.last);
      final downloadCountUpdate = _recordDownload();
      if (!mounted) return;
      setState(() => _loadingPreview = false);
      final ratio = await Navigator.of(context)
          .push<ImageRatioSettings>(MaterialPageRoute(
        builder: (_) => WallpaperPreviewPage(
            filePath: path!,
            finishLabel: '내 보관함에 저장',
            title: widget.post.title,
            uploader: widget.post.username,
            tags: widget.post.tags,
            downloadCount: _downloadCount,
            downloadCountUpdate: downloadCountUpdate),
      ));
      if (ratio == null || !mounted) return;
      final bytes = await File(path).readAsBytes();
      final codec = await ui.instantiateImageCodec(bytes);
      final frame = await codec.getNextFrame();
      final width = frame.image.width;
      final height = frame.image.height;
      frame.image.dispose();
      codec.dispose();
      await store.import(
          sourcePath: path,
          fileName: path.split(Platform.pathSeparator).last,
          title: widget.post.title,
          tags: widget.post.tags,
          width: width,
          height: height,
          mode: 'original',
          ratio: ratio);
      widget.onImported?.call();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('공유 이미지를 내 보관함에 저장했습니다.')));
      }
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text('공유 이미지를 가져오지 못했습니다.\n$error')));
      }
    } finally {
      if (path != null) {
        try {
          await File(path).delete();
        } catch (_) {/* Android may have already cleared its cache. */}
      }
      if (mounted) {
        setState(() {
          _importing = false;
          _loadingPreview = false;
        });
      }
    }
  }

  Widget _imageError() => ColoredBox(
        color: Theme.of(context).colorScheme.surfaceContainerHighest,
        child: Center(
          child: TextButton.icon(
            onPressed: () => setState(() {
              _imageUrl = widget.apiClient.getGalleryImageUrl(widget.post.id);
            }),
            icon: Icon(Icons.refresh),
            label: Text('다시 불러오기'),
          ),
        ),
      );

  Future<int> _recordDownload() async {
    try {
      final count =
          await widget.apiClient.recordGalleryDownload(widget.post.id);
      if (mounted) setState(() => _downloadCount = count);
      return count;
    } catch (_) {
      // Counter availability must not block an already downloaded image. A
      // refresh reads the persisted count again; never claim a local-only count.
      return _downloadCount;
    }
  }

  Widget _previewImage() => FutureBuilder<String>(
        future: _imageUrl,
        builder: (context, snapshot) {
          if (snapshot.hasError) return _imageError();
          if (!snapshot.hasData) {
            return const Center(child: CircularProgressIndicator());
          }
          return Image.network(
            snapshot.data!,
            fit: BoxFit.cover,
            cacheWidth: 480,
            frameBuilder: (context, child, frame, synchronous) =>
                frame != null || synchronous
                    ? child
                    : const Center(child: CircularProgressIndicator()),
            errorBuilder: (_, __, ___) => _imageError(),
          );
        },
      );

  @override
  Widget build(BuildContext context) => Material(
        color: Theme.of(context).colorScheme.surfaceContainerHighest,
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: _importing ? null : _import,
          child: Stack(fit: StackFit.expand, children: [
            _previewImage(),
            Positioned(
                top: 6,
                right: 6,
                left: 6,
                child: Align(
                  alignment: Alignment.topRight,
                  child: Tooltip(
                    message: '다운로드 $_downloadCount회',
                    child: Container(
                      padding: const EdgeInsets.symmetric(
                          horizontal: 8, vertical: 4),
                      decoration: BoxDecoration(
                          color: Colors.black.withValues(alpha: .6),
                          borderRadius: BorderRadius.circular(20)),
                      child: Row(mainAxisSize: MainAxisSize.min, children: [
                        const Icon(Icons.download_outlined,
                            size: 14, color: Colors.white),
                        const SizedBox(width: 4),
                        Flexible(
                            child: Text('$_downloadCount',
                                key: ValueKey(
                                    'gallery-download-${widget.post.id}'),
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: const TextStyle(
                                    color: Colors.white, fontSize: 12))),
                      ]),
                    ),
                  ),
                )),
            Positioned(
                bottom: 0,
                left: 0,
                right: 0,
                child: ColoredBox(
                  color: Colors.black.withValues(alpha: .65),
                  child: Padding(
                    padding:
                        const EdgeInsets.symmetric(horizontal: 6, vertical: 5),
                    child: Text(widget.post.title,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        textAlign: TextAlign.center,
                        style: const TextStyle(
                            color: Colors.white,
                            fontSize: 14,
                            fontWeight: FontWeight.w500)),
                  ),
                )),
            if (_loadingPreview)
              Positioned.fill(
                  child: ColoredBox(
                      color: Colors.black38,
                      child: Center(
                          child: Semantics(
                              label: '이미지 불러오는 중',
                              child: const CircularProgressIndicator(
                                  color: Colors.white))))),
          ]),
        ),
      );
}

/// App-owned preview. Unlike the Android system preview, this keeps our UI visible.
class HomeWallpaperPreviewPage extends StatefulWidget {
  const HomeWallpaperPreviewPage({
    super.key,
    required this.image,
    required this.store,
    required this.onEdit,
    required this.onShare,
    required this.onDelete,
  });
  final LocalImageItem image;
  final LocalImageStore store;
  final VoidCallback onEdit;
  final VoidCallback onShare;
  final VoidCallback onDelete;
  @override
  State<HomeWallpaperPreviewPage> createState() =>
      _HomeWallpaperPreviewPageState();
}

class _HomeWallpaperPreviewPageState extends State<HomeWallpaperPreviewPage> {
  bool _busy = false;
  late bool _live2D = widget.image.isLive2D;
  late Future<Map<Object?, Object?>>? _layers =
      _live2D ? widget.store.live2DLayers(widget.image.id) : null;
  WallpaperTarget _target = WallpaperTarget.home;
  bool _targetTouched = false;

  @override
  void initState() {
    super.initState();
    if (_live2D) {
      _syncLiveTarget();
    }
  }

  Future<void> _syncLiveTarget() async {
    final target = await Live2DService.activeTarget();
    if (mounted && !_targetTouched && target != null) {
      setState(() => _target = target);
    }
  }

  Future<void> _convertLive2D() async {
    if (widget.image.isShared) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text('공유 중인 이미지는 수정할 수 없습니다. 먼저 공유를 취소해 주세요.')));
      return;
    }
    final draft = await Navigator.of(context).push<Live2DDraft>(
        MaterialPageRoute(
            builder: (_) => Live2DPage(sourcePath: widget.image.path)));
    if (draft == null) return;
    try {
      if (!mounted) return;
      setState(() => _busy = true);
      await widget.store.attachLive2D(widget.image.id, draft.id);
      if (mounted) {
        setState(() {
          _live2D = true;
          _layers = widget.store.live2DLayers(widget.image.id);
        });
        _syncLiveTarget();
      }
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text('Live 2D를 저장하지 못했습니다.\n$error')));
      }
    } finally {
      await Live2DService.discard(draft);
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _apply() async {
    ScaffoldMessenger.of(context).clearSnackBars();
    setState(() => _busy = true);
    try {
      await widget.store.applyWallpaper(widget.image.id, target: _target);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: const Text('배경화면을 적용했습니다.')));
    } on PlatformException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(
                content: Text(e.message ?? '$e'),
                action: _live2D && e.code == 'live2d_activation_required'
                    ? SnackBarAction(label: 'Live 2D 활성화', onPressed: _activateLive2D)
                    : null));
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('적용하지 못했습니다: $e')));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _activateLive2D() async {
    if (_busy) return;
    ScaffoldMessenger.of(context).clearSnackBars();
    setState(() => _busy = true);
    try {
      final confirmed = await widget.store.activateLive2D(widget.image.id, target: _target);
      if (mounted && confirmed) {
        _targetTouched = false;
        await _syncLiveTarget();
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('Live 2D를 활성화했습니다. 이후 같은 화면에는 미리보기 없이 적용됩니다.')));
      }
    } on PlatformException catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(error.message ?? 'Live 2D를 활성화하지 못했습니다.')));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => DeviceDisplayBuilder(
        builder: (context, display) => AnnotatedRegion<SystemUiOverlayStyle>(
          value: AppTheme.overlayFor(Brightness.dark),
          child: Scaffold(
            extendBody: false,
            backgroundColor: Colors.black,
            bottomNavigationBar: SafeArea(
              top: false,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 12),
                        decoration: BoxDecoration(
                          color: const Color(0xDD20232B),
                          borderRadius: BorderRadius.circular(12),
                        ),
                        child: DropdownButtonHideUnderline(
                          child: DropdownButton<WallpaperTarget>(
                            dropdownColor: const Color(0xFF20232B),
                            value: _target,
                            style: TextStyle(color: Colors.white),
                            isExpanded: true,
                            items: const [
                              DropdownMenuItem(
                                value: WallpaperTarget.home,
                                child: Text('홈 화면'),
                              ),
                              DropdownMenuItem(
                                value: WallpaperTarget.lock,
                                child: Text('잠금 화면'),
                              ),
                              DropdownMenuItem(
                                value: WallpaperTarget.both,
                                child: Text('홈 화면 및 잠금 화면'),
                              ),
                            ],
                            onChanged: _busy
                                ? null
                                : (value) {
                                    if (value != null) {
                                      setState(() => _target = value);
                                      _targetTouched = true;
                                    }
                                  },
                          ),
                        ),
                      ),
                    const SizedBox(height: 10),
                    if (!_live2D && !widget.image.isShared)
                      TextButton.icon(
                          onPressed: _busy ? null : _convertLive2D,
                          icon: const Icon(Icons.view_in_ar_outlined),
                          label: const Text('Live 2D')),
                    if (_live2D)
                      TextButton.icon(
                          key: const ValueKey('activate-saved-live2d'),
                          onPressed: _busy ? null : _activateLive2D,
                          icon: const Icon(Icons.view_in_ar_outlined),
                          label: const Text('배경화면에서 Live 2D 활성화')),
                    SizedBox(
                      width: double.infinity,
                      child: FilledButton.icon(
                        onPressed: _busy ? null : _apply,
                        icon: _busy
                            ? const SizedBox.square(
                                dimension: 16,
                                child:
                                    CircularProgressIndicator(strokeWidth: 2),
                              )
                            : Icon(Icons.wallpaper),
                        label: const Text('배경화면 설정'),
                      ),
                    ),
                  ],
                ),
              ),
            ),
            body: SafeArea(
              bottom: false,
              child: Stack(
                children: [
                  Positioned.fill(
                    child: _live2D
                        ? FutureBuilder<Map<Object?, Object?>>(
                            future: _layers,
                            builder: (_, snapshot) {
                              final layers = snapshot.data;
                              if (layers != null) {
                                return Live2DPreview(
                                    backgroundPath:
                                        layers['backgroundPath']! as String,
                                    foregroundPath:
                                        layers['foregroundPath']! as String);
                              }
                              if (snapshot.hasError) {
                                return Center(
                                    child: Column(
                                        mainAxisSize: MainAxisSize.min,
                                        children: [
                                      const Text(
                                          'Live 2D 레이어를 불러오지 못했습니다.\n원본 사진에서 다시 분리해 주세요.',
                                          textAlign: TextAlign.center,
                                          style:
                                              TextStyle(color: Colors.white)),
                                      TextButton(
                                          onPressed:
                                              _busy ? null : _convertLive2D,
                                          child: const Text('다시 분리')),
                                    ]));
                              }
                              return const Center(
                                  child: CircularProgressIndicator());
                            })
                        : Image.file(
                            File(widget.image.path),
                            key: const ValueKey('wallpaper-preview-image'),
                            fit: BoxFit.contain,
                          ),
                  ),
                  Positioned(
                    top: 8,
                    left: 8,
                    child: IconButton.filledTonal(
                      onPressed: () => Navigator.pop(context),
                      icon: Icon(Icons.arrow_back),
                    ),
                  ),
                  Positioned(
                    top: 8,
                    right: 8,
                    child: PopupMenuButton<String>(
                      icon: Icon(Icons.more_vert, color: Colors.white),
                      onSelected: (action) {
                        Navigator.pop(context);
                        switch (action) {
                          case 'edit':
                            widget.onEdit();
                          case 'share':
                            widget.onShare();
                          case 'delete':
                            widget.onDelete();
                        }
                      },
                      itemBuilder: (_) => [
                        if (!widget.image.isShared)
                          const PopupMenuItem(value: 'edit', child: Text('수정')),
                        PopupMenuItem(
                          value: 'share',
                          child: Text(widget.image.isShared ? '공유 취소' : '공유'),
                        ),
                        const PopupMenuItem(value: 'delete', child: Text('삭제')),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      );
}
