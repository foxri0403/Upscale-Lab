import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:image_picker/image_picker.dart';

import 'image_registration_page.dart';
import 'models/gallery.dart';
import 'models/local_image.dart';
import 'services/api_client.dart';
import 'services/local_image_store.dart';
import 'theme/app_theme.dart';
import 'widgets/app_components.dart';
import 'widgets/image_edit_dialog.dart';
import 'widgets/local_image_card.dart';

export 'image_registration_page.dart';

const _wallpaperChannel = MethodChannel('live_layer/wallpaper');

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
  late final _localImageStore =
      LocalImageStore(scope: widget.apiClient.storageScope);
  late Future<List<LocalImageItem>> _images = _localImageStore.getAll();
  final Set<String> _busyIds = {};
  bool _creating = false;
  bool _isGrid = true;
  bool _removingWallpaper = false;
  int _selectedIndex = 0;
  int _galleryRevision = 0;
  final _gallerySelection = GallerySearchSelection();

  void _reload() => setState(() => _images = _localImageStore.getAll());

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
      await _localImageStore.import(
        sourcePath: sourcePath,
        fileName: edited == null ? picked.name : 'edited.png',
        title: options.title,
        tags: options.tags,
        width: width,
        height: height,
        mode: 'original',
      );
      if (!mounted) return;
      _reload();
      _message('이미지를 로컬에 등록했습니다. 공유할 때만 클라우드에 업로드됩니다.');
    } on PlatformException catch (error) {
      _message(error.message ?? '$error');
    } catch (error) {
      _message('이미지를 등록하지 못했습니다.\n$error');
    } finally {
      await options?.editedFile?.dispose();
      if (mounted) setState(() => _creating = false);
    }
  }

  Future<void> _share(LocalImageItem image) async {
    if (image.isShared || _busyIds.contains(image.id)) return;
    setState(() => _busyIds.add(image.id));
    String? projectId;
    try {
      final project = await widget.apiClient.createProject(
        title: image.title,
        filePath: image.path,
        width: image.width,
        height: image.height,
      );
      projectId = project.id;
      final postId = await widget.apiClient.shareProject(
        projectId: project.id,
        title: image.title,
        tags: image.tags,
      );
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
      if (projectId != null) {
        try {
          await widget.apiClient.deleteProject(projectId);
        } catch (_) {
          // Keep the original share error; rollback is best effort.
        }
      }
      _message('공유하지 못했습니다. 로컬 이미지는 그대로 유지됩니다.\n$error');
    } finally {
      if (mounted) setState(() => _busyIds.remove(image.id));
    }
  }

  Future<void> _unshare(LocalImageItem image) async {
    if (!image.isShared || _busyIds.contains(image.id)) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('공유 취소'),
        content: const Text('검색 화면과 클라우드에서 공유 사본을 삭제할까요? 로컬 이미지는 유지됩니다.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('돌아가기'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('공유 취소'),
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
    final edited = await showDialog<ImageEditResult>(
      context: context,
      builder: (_) => ImageEditDialog(title: image.title, tags: image.tags),
    );
    if (edited == null || !mounted) return;

    setState(() => _busyIds.add(image.id));
    try {
      var confirmedTags = edited.tags;
      if (image.isShared) {
        confirmedTags = await widget.apiClient.updateGalleryPost(
          postId: image.galleryPostId!,
          title: edited.title,
          tags: edited.tags,
        );
      }
      await _localImageStore.updateDetails(
          id: image.id, title: edited.title, tags: confirmedTags);
      if (!mounted) return;
      setState(() => _galleryRevision++);
      _reload();
      _message('이미지 이름과 태그를 수정했습니다.');
    } catch (error) {
      _message('이미지를 수정하지 못했습니다.\n$error');
    } finally {
      if (mounted) setState(() => _busyIds.remove(image.id));
    }
  }

  Future<void> _removeWallpaper() async {
    if (_removingWallpaper) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('홈 배경화면 제거'),
        content:
            const Text('현재 Android 홈 배경화면만 기본 배경으로 되돌릴까요? 등록된 이미지는 삭제되지 않습니다.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('취소'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('배경화면 제거'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;

    setState(() => _removingWallpaper = true);
    try {
      await _wallpaperChannel.invokeMethod<void>('clearHomeWallpaper');
      _message('홈 배경화면을 제거했습니다. 등록된 이미지는 유지됩니다.');
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
        title: const Text('이미지 삭제'),
        content: Text(
          image.isShared
              ? '로컬 이미지와 공유된 클라우드 항목을 모두 삭제할까요?'
              : '이 이미지를 기기에서 삭제할까요?',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('취소'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('삭제'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;

    setState(() => _busyIds.add(image.id));
    try {
      final postId = image.galleryPostId;
      if (postId != null) await widget.apiClient.deleteGalleryPost(postId);
      final projectId = image.cloudProjectId;
      if (projectId != null) await widget.apiClient.deleteProject(projectId);
      await _localImageStore.delete(image.id);
      if (!mounted) return;
      setState(() => _galleryRevision++);
      _reload();
      _message('이미지를 삭제했습니다.');
    } catch (error) {
      _message('이미지를 삭제하지 못했습니다.\n$error');
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
                const Icon(Icons.image_not_supported_outlined,
                    color: AppTheme.muted, size: 36),
                const SizedBox(height: 12),
                const Text('이미지를 불러오지 못했습니다.'),
                TextButton(onPressed: _reload, child: const Text('다시 불러오기')),
              ]),
            ));
          }
          final images = snapshot.data ?? const <LocalImageItem>[];
          Widget item(int index) {
            if (index == 0) {
              return NewImageCard(
                isGrid: _isGrid,
                creating: _creating,
                onTap: _creating ? null : _createImage,
              );
            }
            final image = images[index - 1];
            return LocalImageCard(
              key: ValueKey(image.id),
              image: image,
              isGrid: _isGrid,
              busy: _busyIds.contains(image.id),
              onTap: () => Navigator.of(context).push<void>(MaterialPageRoute(
                builder: (_) => WallpaperPreviewPage(
                  filePath: image.path,
                  finishLabel: '메인으로 돌아가기',
                ),
              )),
              onShare: () => _share(image),
              onUnshare: () => _unshare(image),
              onEdit: () => _editImage(image),
              onDelete: () => _delete(image),
            );
          }

          return LayoutBuilder(builder: (context, constraints) {
            final cellWidth = (constraints.maxWidth - 52) / 2;
            final scale = MediaQuery.textScalerOf(context).scale(14) / 14;
            return RefreshIndicator(
              onRefresh: () async {
                final refreshed = _localImageStore.getAll();
                setState(() => _images = refreshed);
                await refreshed;
              },
              child: CustomScrollView(
                physics: const AlwaysScrollableScrollPhysics(),
                slivers: [
                  SliverToBoxAdapter(
                      child: Padding(
                    padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
                    child:
                        AppSectionTitle('내 이미지', trailing: '${images.length}개'),
                  )),
                  SliverPadding(
                    padding: const EdgeInsets.fromLTRB(20, 0, 20, 24),
                    sliver: _isGrid
                        ? SliverGrid(
                            gridDelegate:
                                SliverGridDelegateWithFixedCrossAxisCount(
                              crossAxisCount: 2,
                              crossAxisSpacing: 12,
                              mainAxisSpacing: 12,
                              mainAxisExtent: cellWidth * 0.8 + 68 * scale,
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

  Widget _settings() => ListView(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 24),
        children: [
          const AppSectionTitle('설정'),
          const SizedBox(height: 16),
          AppPanel(
            padding: EdgeInsets.zero,
            child: Column(children: [
              ListTile(
                leading: const Icon(Icons.wallpaper_outlined),
                title: const Text('홈 배경화면 제거'),
                subtitle: const Text('등록한 이미지는 보관됩니다.'),
                trailing: const Icon(Icons.chevron_right),
                enabled: !_removingWallpaper,
                onTap: _removingWallpaper ? null : _removeWallpaper,
              ),
              const Divider(),
              ListTile(
                leading: const Icon(Icons.logout),
                title: const Text('로그아웃'),
                trailing: const Icon(Icons.chevron_right),
                onTap: () => widget.onLogout(context),
              ),
            ]),
          ),
        ],
      );

  @override
  Widget build(BuildContext context) {
    final content = switch (_selectedIndex) {
      1 => GallerySearchPage(
          key: ValueKey(_galleryRevision),
          apiClient: widget.apiClient,
          onImported: _reload,
          selection: _gallerySelection,
          isGrid: _isGrid,
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
              if (_selectedIndex != 2) ...[
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
        decoration: const BoxDecoration(
          border: Border(top: BorderSide(color: AppTheme.border)),
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
            NavigationDestination(icon: Icon(Icons.search), label: '검색'),
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
      {super.key,
      required this.apiClient,
      this.onImported,
      this.selection,
      this.isGrid = true});

  final ApiClient apiClient;
  final VoidCallback? onImported;
  final GallerySearchSelection? selection;
  final bool isGrid;

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
      tags: _selectedTags.toList(),
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

  @override
  Widget build(BuildContext context) => LayoutBuilder(
        builder: (context, constraints) {
          final cellWidth = (constraints.maxWidth - 52) / 2;
          final scale = MediaQuery.textScalerOf(context).scale(14) / 14;
          return RefreshIndicator(
            onRefresh: _refresh,
            child: CustomScrollView(
              physics: const AlwaysScrollableScrollPhysics(),
              slivers: [
                SliverToBoxAdapter(
                    child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
                  child: const AppSectionTitle('공유 이미지'),
                )),
                SliverToBoxAdapter(
                    child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 20),
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
                        icon: const Icon(Icons.arrow_forward),
                      ),
                    ),
                  ),
                )),
                SliverToBoxAdapter(
                    child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 12, 20, 8),
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(maxHeight: 136),
                    child: SingleChildScrollView(
                        child: Wrap(
                      spacing: 6,
                      runSpacing: 4,
                      children: [
                        ChoiceChip(
                          label: const Text('전체'),
                          selected: _selectedTags.isEmpty,
                          onSelected: (_) => setState(() {
                            _selectedTags.clear();
                            _posts = _load();
                          }),
                        ),
                        ...galleryTags.map((tag) => FilterChip(
                              label: Text(tag),
                              selected: _selectedTags.contains(tag),
                              onSelected: (checked) => setState(() {
                                checked
                                    ? _selectedTags.add(tag)
                                    : _selectedTags.remove(tag);
                                _posts = _load();
                              }),
                            )),
                      ],
                    )),
                  ),
                )),
                SliverToBoxAdapter(
                    child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
                  child: Text(
                    _selectedTags.isEmpty
                        ? '태그를 여러 개 선택할 수 있습니다.'
                        : '${_selectedTags.length}개 선택 · 선택한 태그 중 하나라도 포함된 이미지',
                    style: const TextStyle(fontSize: 12, color: AppTheme.muted),
                  ),
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
                          const Icon(Icons.photo_library_outlined,
                              color: AppTheme.muted, size: 36),
                          const SizedBox(height: 12),
                          Text(
                              snapshot.hasError
                                  ? '공유 이미지를 불러오지 못했습니다.'
                                  : '조건에 맞는 공유 이미지가 없습니다.',
                              textAlign: TextAlign.center),
                          if (snapshot.hasError)
                            TextButton(
                                onPressed: _refresh,
                                child: const Text('다시 불러오기')),
                        ]),
                      ));
                    }
                    final posts = snapshot.data!;
                    Widget item(int index) => _GalleryPostCard(
                          key: ValueKey(posts[index].id),
                          post: posts[index],
                          apiClient: widget.apiClient,
                          onImported: widget.onImported,
                          isGrid: widget.isGrid,
                        );
                    return SliverPadding(
                      padding: const EdgeInsets.fromLTRB(20, 0, 20, 24),
                      sliver: widget.isGrid
                          ? SliverGrid(
                              gridDelegate:
                                  SliverGridDelegateWithFixedCrossAxisCount(
                                crossAxisCount: 2,
                                crossAxisSpacing: 12,
                                mainAxisSpacing: 12,
                                mainAxisExtent: cellWidth * 0.8 + 84 * scale,
                              ),
                              delegate: SliverChildBuilderDelegate(
                                (context, index) => item(index),
                                childCount: posts.length,
                              ),
                            )
                          : SliverList(
                              delegate: SliverChildBuilderDelegate(
                              (context, index) => Padding(
                                padding: const EdgeInsets.only(bottom: 12),
                                child: item(index),
                              ),
                              childCount: posts.length,
                            )),
                    );
                  },
                ),
              ],
            ),
          );
        },
      );
}

class _GalleryPostCard extends StatefulWidget {
  const _GalleryPostCard(
      {super.key,
      required this.post,
      required this.apiClient,
      this.onImported,
      this.isGrid = true});

  final GalleryPostItem post;
  final ApiClient apiClient;
  final VoidCallback? onImported;
  final bool isGrid;

  @override
  State<_GalleryPostCard> createState() => _GalleryPostCardState();
}

class _GalleryPostCardState extends State<_GalleryPostCard> {
  late Future<String> _imageUrl = _initialImageUrl();
  bool _importing = false;

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
    if (oldWidget.post.id != widget.post.id ||
        oldWidget.post.thumbnailUrl != widget.post.thumbnailUrl ||
        (widget.post.thumbnailUrl == null &&
            oldWidget.post.imageUrl != widget.post.imageUrl)) {
      _imageUrl = _initialImageUrl();
    }
  }

  Future<void> _import() async {
    if (_importing) return;
    setState(() => _importing = true);
    String? path;
    try {
      final store = LocalImageStore(scope: widget.apiClient.storageScope);
      final url = await widget.apiClient.getGalleryDownloadUrl(widget.post.id);
      path = await store.downloadShared(
          url: url,
          fileName: widget.post.fileName ?? Uri.parse(url).pathSegments.last);
      if (!mounted) return;
      final completed =
          await Navigator.of(context).push<bool>(MaterialPageRoute(
        builder: (_) =>
            WallpaperPreviewPage(filePath: path!, finishLabel: '내 보관함에 저장'),
      ));
      if (completed != true || !mounted) return;
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
          mode: 'original');
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
      if (mounted) setState(() => _importing = false);
    }
  }

  Widget _imageError() => ColoredBox(
        color: const Color(0xFFF3F4F7),
        child: Center(
          child: TextButton.icon(
            onPressed: () => setState(() {
              _imageUrl = widget.apiClient.getGalleryImageUrl(widget.post.id);
            }),
            icon: const Icon(Icons.refresh),
            label: const Text('다시 불러오기'),
          ),
        ),
      );

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

  Widget _details() => Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(widget.post.title,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style:
                  const TextStyle(fontSize: 14, fontWeight: FontWeight.w700)),
          const SizedBox(height: 5),
          Text('${widget.post.username} · ${widget.post.tags.join(' · ')}',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 12, color: AppTheme.muted)),
          const SizedBox(height: 5),
          Text(_importing ? '원본 가져오는 중…' : '눌러서 가져오기 · 배경화면 적용',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 11, color: AppTheme.primary)),
        ],
      );

  @override
  Widget build(BuildContext context) => Material(
        color: AppTheme.surface,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
          side: const BorderSide(color: AppTheme.border),
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: _importing ? null : _import,
          child: widget.isGrid
              ? Column(children: [
                  Expanded(child: SizedBox.expand(child: _previewImage())),
                  Padding(padding: const EdgeInsets.all(10), child: _details()),
                ])
              : Padding(
                  padding: const EdgeInsets.all(10),
                  child: Row(children: [
                    ClipRRect(
                        borderRadius: BorderRadius.circular(12),
                        child: SizedBox.square(
                            dimension: 80, child: _previewImage())),
                    const SizedBox(width: 14),
                    Expanded(child: _details()),
                    const SizedBox(width: 8),
                    const Icon(Icons.download_outlined,
                        size: 20, color: AppTheme.primary),
                  ])),
        ),
      );
}

class WallpaperPreviewPage extends StatefulWidget {
  const WallpaperPreviewPage({
    super.key,
    required this.filePath,
    required this.finishLabel,
  });

  final String filePath;
  final String finishLabel;

  @override
  State<WallpaperPreviewPage> createState() => _WallpaperPreviewPageState();
}

class _WallpaperPreviewPageState extends State<WallpaperPreviewPage> {
  bool _applying = false;
  bool _applied = false;

  Future<void> _apply() async {
    setState(() => _applying = true);
    try {
      await _wallpaperChannel.invokeMethod<void>('applyLocalStaticWallpaper', {
        'sourcePath': widget.filePath,
      });
      if (!mounted) return;
      setState(() => _applied = true);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('배경화면을 적용했습니다. 앱 종료와 재부팅 후에도 유지됩니다.')),
      );
    } on PlatformException catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(error.message ?? '$error')),
        );
      }
    } finally {
      if (mounted) setState(() => _applying = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('배경화면 미리보기')),
        body: SafeArea(
            child: Column(children: [
          Expanded(
              child: Padding(
            padding: const EdgeInsets.fromLTRB(20, 8, 20, 16),
            child: ClipRRect(
              borderRadius: BorderRadius.circular(20),
              child: SizedBox.expand(
                child: Image.file(File(widget.filePath), fit: BoxFit.cover),
              ),
            ),
          )),
          Flexible(
            fit: FlexFit.loose,
            child: SingleChildScrollView(
              padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
              child: Column(mainAxisSize: MainAxisSize.min, children: [
                SizedBox(
                    width: double.infinity,
                    child: FilledButton.icon(
                      onPressed: _applying ? null : _apply,
                      icon: _applying
                          ? const SizedBox.square(
                              dimension: 18,
                              child: CircularProgressIndicator(strokeWidth: 2))
                          : Icon(_applied ? Icons.check : Icons.wallpaper),
                      label: Text(_applied ? '배경화면 적용됨' : '홈 화면에 적용'),
                    )),
                const SizedBox(height: 10),
                SizedBox(
                    width: double.infinity,
                    child: OutlinedButton(
                      onPressed:
                          _applying ? null : () => Navigator.pop(context, true),
                      child: Text(widget.finishLabel),
                    )),
              ]),
            ),
          ),
        ])),
      );
}
