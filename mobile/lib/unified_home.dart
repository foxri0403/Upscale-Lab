import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:image_picker/image_picker.dart';

import 'models/gallery.dart';
import 'models/local_image.dart';
import 'services/api_client.dart';
import 'services/local_image_store.dart';

const _localImageStore = LocalImageStore();
const _wallpaperChannel = MethodChannel('live_layer/wallpaper');

enum ImageCreationMode { original, upscale, depth25d }

class ImageRegistrationOptions {
  const ImageRegistrationOptions({
    required this.title,
    required this.tag,
    required this.mode,
  });

  final String title;
  final String tag;
  final ImageCreationMode mode;
}

extension on ImageCreationMode {
  String get label => switch (this) {
        ImageCreationMode.original => '원본 이미지',
        ImageCreationMode.upscale => '업스케일링',
        ImageCreationMode.depth25d => '2.5D 변환',
      };
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
  late Future<List<LocalImageItem>> _images = _localImageStore.getAll();
  final Set<String> _busyIds = {};
  bool _creating = false;
  int _selectedIndex = 0;
  int _galleryRevision = 0;

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
    try {
      final bytes = await picked.readAsBytes();
      final codec = await ui.instantiateImageCodec(bytes);
      final frame = await codec.getNextFrame();
      final width = frame.image.width;
      final height = frame.image.height;
      frame.image.dispose();
      codec.dispose();

      if (!mounted) return;
      final options = await Navigator.of(context).push<ImageRegistrationOptions>(
        MaterialPageRoute(
          builder: (_) => ImageFeaturePage(
            filePath: picked.path,
            title: picked.name,
          ),
        ),
      );
      if (options == null || !mounted) return;

      await _localImageStore.import(
        sourcePath: picked.path,
        fileName: picked.name,
        title: options.title,
        tag: options.tag,
        width: width,
        height: height,
        mode: options.mode.name,
      );
      if (!mounted) return;
      _reload();
      _message('이미지를 로컬에 등록했습니다. 공유할 때만 클라우드에 업로드됩니다.');
    } on PlatformException catch (error) {
      _message(error.message ?? '$error');
    } catch (error) {
      _message('이미지를 등록하지 못했습니다.\n$error');
    } finally {
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
        tag: image.tag,
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
        content: const Text('탐색 화면과 클라우드에서 공유 사본을 삭제할까요? 로컬 이미지는 유지됩니다.'),
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

  Widget _home() {
    return FutureBuilder<List<LocalImageItem>>(
      future: _images,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: CircularProgressIndicator());
        }
        if (snapshot.hasError) return Center(child: Text('${snapshot.error}'));
        final images = snapshot.data ?? const <LocalImageItem>[];

        return RefreshIndicator(
          onRefresh: () async {
            final refreshed = _localImageStore.getAll();
            setState(() => _images = refreshed);
            await refreshed;
          },
          child: CustomScrollView(
            physics: const AlwaysScrollableScrollPhysics(),
            slivers: [
              const SliverToBoxAdapter(
                child: Padding(
                  padding: EdgeInsets.fromLTRB(22, 0, 22, 18),
                  child: Text(
                    '이미지는 먼저 이 기기에만 저장됩니다. 카드 설정에서 공유할 때만 클라우드에 업로드됩니다.',
                    style: TextStyle(color: Color(0xFF6F7789), height: 1.45),
                  ),
                ),
              ),
              SliverPadding(
                padding: const EdgeInsets.fromLTRB(22, 0, 22, 24),
                sliver: SliverGrid(
                  gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                    crossAxisCount: 2,
                    crossAxisSpacing: 16,
                    mainAxisSpacing: 22,
                    childAspectRatio: 0.70,
                  ),
                  delegate: SliverChildBuilderDelegate(
                    (context, index) {
                      if (index == 0) {
                        return _NewImageCard(
                          creating: _creating,
                          onTap: _creating ? null : _createImage,
                        );
                      }
                      final image = images[index - 1];
                      return _LocalImageCard(
                        image: image,
                        busy: _busyIds.contains(image.id),
                        onTap: () => Navigator.of(context).push<void>(
                          MaterialPageRoute(
                            builder: (_) => WallpaperPreviewPage(
                              filePath: image.path,
                              finishLabel: '메인으로 돌아가기',
                            ),
                          ),
                        ),
                        onShare: () => _share(image),
                        onUnshare: () => _unshare(image),
                        onDelete: () => _delete(image),
                      );
                    },
                    childCount: images.length + 1,
                  ),
                ),
              ),
            ],
          ),
        );
      },
    );
  }

  Widget _settings() => Align(
        alignment: Alignment.topCenter,
        child: OutlinedButton.icon(
          onPressed: () => widget.onLogout(context),
          icon: const Icon(Icons.logout),
          label: const Text('로그아웃'),
        ),
      );

  @override
  Widget build(BuildContext context) {
    final content = switch (_selectedIndex) {
      1 => GalleryExplorePage(
          key: ValueKey(_galleryRevision),
          apiClient: widget.apiClient,
        ),
      2 => _settings(),
      _ => _home(),
    };
    return Scaffold(
      body: SafeArea(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Padding(
              padding: EdgeInsets.fromLTRB(22, 25, 22, 28),
              child: Text(
                'Upscale Lab',
                style: TextStyle(
                  fontSize: 32,
                  fontWeight: FontWeight.bold,
                  letterSpacing: -1,
                ),
              ),
            ),
            Expanded(child: content),
          ],
        ),
      ),
      bottomNavigationBar: NavigationBar(
        height: 70,
        selectedIndex: _selectedIndex,
        onDestinationSelected: (value) => setState(() {
          _selectedIndex = value;
          if (value == 1) _galleryRevision++;
        }),
        destinations: const [
          NavigationDestination(icon: Icon(Icons.home_outlined), label: '홈'),
          NavigationDestination(icon: Icon(Icons.search), label: '탐색'),
          NavigationDestination(
              icon: Icon(Icons.settings_outlined), label: '설정'),
        ],
      ),
    );
  }
}

class _NewImageCard extends StatelessWidget {
  const _NewImageCard({required this.creating, required this.onTap});

  final bool creating;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => InkWell(
        borderRadius: BorderRadius.circular(16),
        onTap: onTap,
        child: Container(
          decoration: BoxDecoration(
            color: const Color(0xFFFAFBFF),
            borderRadius: BorderRadius.circular(16),
            border: Border.all(color: const Color(0xFFDDE2F1)),
          ),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              if (creating)
                const CircularProgressIndicator()
              else
                const Icon(Icons.add, size: 52, color: Color(0xFF58647D)),
              const SizedBox(height: 18),
              Text(creating ? '등록 중...' : '새 이미지 등록'),
              const SizedBox(height: 6),
              const Text(
                '로컬에 저장',
                style: TextStyle(fontSize: 12, color: Color(0xFF9299AA)),
              ),
            ],
          ),
        ),
      );
}

class _LocalImageCard extends StatelessWidget {
  const _LocalImageCard({
    required this.image,
    required this.busy,
    required this.onTap,
    required this.onShare,
    required this.onUnshare,
    required this.onDelete,
  });

  final LocalImageItem image;
  final bool busy;
  final VoidCallback onTap;
  final VoidCallback onShare;
  final VoidCallback onUnshare;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) => InkWell(
        borderRadius: BorderRadius.circular(16),
        onTap: busy ? null : onTap,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: ClipRRect(
                borderRadius: BorderRadius.circular(16),
                child: Stack(
                  fit: StackFit.expand,
                  children: [
                    Image.file(
                      File(image.path),
                      fit: BoxFit.cover,
                      errorBuilder: (_, __, ___) => const ColoredBox(
                        color: Color(0xFFF3F4F7),
                        child: Icon(Icons.broken_image_outlined, size: 42),
                      ),
                    ),
                    Positioned(
                      top: 4,
                      right: 4,
                      child: Material(
                        color: Colors.black54,
                        shape: const CircleBorder(),
                        child: PopupMenuButton<String>(
                          enabled: !busy,
                          iconColor: Colors.white,
                          onSelected: (value) {
                            if (value == 'share') onShare();
                            if (value == 'unshare') onUnshare();
                            if (value == 'delete') onDelete();
                          },
                          itemBuilder: (_) => [
                            if (image.isShared)
                              const PopupMenuItem(
                                value: 'unshare',
                                child: Text('공유 취소'),
                              )
                            else
                              const PopupMenuItem(
                                value: 'share',
                                child: Text('공유'),
                              ),
                            const PopupMenuItem(
                              value: 'delete',
                              child: Text('삭제'),
                            ),
                          ],
                        ),
                      ),
                    ),
                    if (busy)
                      const ColoredBox(
                        color: Colors.black38,
                        child: Center(child: CircularProgressIndicator()),
                      ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 10),
            Text(
              image.title,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: 3),
            Text(
              image.isShared ? '공유됨 · 클라우드' : '로컬 · 공유 안 됨',
              style: const TextStyle(fontSize: 12, color: Color(0xFF9299AA)),
            ),
            const SizedBox(height: 3),
            Text(
              image.tag,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 11, color: Color(0xFF6F7789)),
            ),
          ],
        ),
      );
}

class GalleryExplorePage extends StatefulWidget {
  const GalleryExplorePage({super.key, required this.apiClient});

  final ApiClient apiClient;

  @override
  State<GalleryExplorePage> createState() => _GalleryExplorePageState();
}

class _GalleryExplorePageState extends State<GalleryExplorePage> {
  final _searchController = TextEditingController();
  String? _selectedTag;
  late Future<List<GalleryPostItem>> _posts = _load();

  Future<List<GalleryPostItem>> _load() => widget.apiClient.getGallery(
        search: _searchController.text,
        tag: _selectedTag,
      );

  Future<void> _refresh() async {
    final posts = _load();
    setState(() => _posts = posts);
    await posts;
  }

  void _search() => setState(() => _posts = _load());

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(22, 0, 22, 12),
            child: TextField(
              controller: _searchController,
              textInputAction: TextInputAction.search,
              onSubmitted: (_) => _search(),
              decoration: InputDecoration(
                hintText: '이름, 작성자, 태그 검색',
                prefixIcon: const Icon(Icons.search),
                suffixIcon: IconButton(
                  tooltip: '검색',
                  onPressed: _search,
                  icon: const Icon(Icons.arrow_forward),
                ),
                border: const OutlineInputBorder(),
              ),
            ),
          ),
          SizedBox(
            height: 45,
            child: ListView(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: 22),
              children: [
                Padding(
                  padding: const EdgeInsets.only(right: 8),
                  child: ChoiceChip(
                    label: const Text('전체'),
                    selected: _selectedTag == null,
                    onSelected: (_) {
                      setState(() {
                        _selectedTag = null;
                        _posts = _load();
                      });
                    },
                  ),
                ),
                ...galleryTags.map(
                  (tag) => Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: ChoiceChip(
                      label: Text(tag),
                      selected: _selectedTag == tag,
                      onSelected: (_) {
                        setState(() {
                          _selectedTag = tag;
                          _posts = _load();
                        });
                      },
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),
          Expanded(
            child: FutureBuilder<List<GalleryPostItem>>(
              future: _posts,
              builder: (context, snapshot) {
                if (snapshot.connectionState != ConnectionState.done) {
                  return const Center(child: CircularProgressIndicator());
                }
                if (snapshot.hasError) {
                  return RefreshIndicator(
                    onRefresh: _refresh,
                    child: ListView(
                      physics: const AlwaysScrollableScrollPhysics(),
                      children: [
                        const SizedBox(height: 100),
                        Center(child: Text('공유 이미지를 불러오지 못했습니다.\n${snapshot.error}')),
                      ],
                    ),
                  );
                }
                final posts = snapshot.data ?? const <GalleryPostItem>[];
                if (posts.isEmpty) {
                  return RefreshIndicator(
                    onRefresh: _refresh,
                    child: ListView(
                      physics: const AlwaysScrollableScrollPhysics(),
                      children: const [
                        SizedBox(height: 100),
                        Center(child: Text('조건에 맞는 공유 이미지가 없습니다.')),
                      ],
                    ),
                  );
                }
                return RefreshIndicator(
                  onRefresh: _refresh,
                  child: GridView.builder(
                    physics: const AlwaysScrollableScrollPhysics(),
                    padding: const EdgeInsets.fromLTRB(22, 0, 22, 24),
                    gridDelegate:
                        const SliverGridDelegateWithFixedCrossAxisCount(
                      crossAxisCount: 2,
                      crossAxisSpacing: 14,
                      mainAxisSpacing: 18,
                      childAspectRatio: 0.72,
                    ),
                    itemCount: posts.length,
                    itemBuilder: (context, index) =>
                        _GalleryPostCard(post: posts[index]),
                  ),
                );
              },
            ),
          ),
        ],
      );
}

class _GalleryPostCard extends StatelessWidget {
  const _GalleryPostCard({required this.post});

  final GalleryPostItem post;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: ClipRRect(
              borderRadius: BorderRadius.circular(16),
              child: SizedBox.expand(
                child: Image.network(
                  post.imageUrl,
                  fit: BoxFit.cover,
                  errorBuilder: (_, __, ___) => const ColoredBox(
                    color: Color(0xFFF3F4F7),
                    child: Icon(Icons.broken_image_outlined, size: 42),
                  ),
                ),
              ),
            ),
          ),
          const SizedBox(height: 9),
          Text(
            post.title,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontWeight: FontWeight.w600),
          ),
          const SizedBox(height: 3),
          Text(
            '${post.username} · ${post.tag}',
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontSize: 12, color: Color(0xFF6F7789)),
          ),
        ],
      );
}

class ImageFeaturePage extends StatefulWidget {
  const ImageFeaturePage({
    super.key,
    required this.filePath,
    required this.title,
  });

  final String filePath;
  final String title;

  @override
  State<ImageFeaturePage> createState() => _ImageFeaturePageState();
}

class _ImageFeaturePageState extends State<ImageFeaturePage> {
  ImageCreationMode _mode = ImageCreationMode.original;
  late final TextEditingController _titleController =
      TextEditingController(text: widget.title);
  String _tag = unspecifiedGalleryTag;

  @override
  void dispose() {
    _titleController.dispose();
    super.dispose();
  }

  Future<void> _continue() async {
    final title = _titleController.text.trim();
    if (title.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('이미지 이름을 입력해 주세요.')),
      );
      return;
    }
    final completed = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => WallpaperPreviewPage(
          filePath: widget.filePath,
          finishLabel: '완료하고 등록',
        ),
      ),
    );
    if (completed == true && mounted) {
      Navigator.pop(
        context,
        ImageRegistrationOptions(title: title, tag: _tag, mode: _mode),
      );
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('이미지 기능 선택')),
        body: SafeArea(
          child: ListView(
            padding: const EdgeInsets.all(20),
            children: [
              SizedBox(
                height: 260,
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(18),
                  child: Image.file(File(widget.filePath), fit: BoxFit.contain),
                ),
              ),
              const SizedBox(height: 18),
              TextField(
                controller: _titleController,
                maxLength: 160,
                decoration: const InputDecoration(
                  labelText: '이미지 이름',
                  border: OutlineInputBorder(),
                ),
              ),
              const SizedBox(height: 10),
              const Text(
                '이 이미지로 무엇을 할까요?',
                style: TextStyle(fontSize: 21, fontWeight: FontWeight.bold),
              ),
              const SizedBox(height: 12),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: ImageCreationMode.values.map((mode) {
                  return ChoiceChip(
                    label: Text(mode == ImageCreationMode.original
                        ? mode.label
                        : '${mode.label} (준비 중)'),
                    selected: _mode == mode,
                    onSelected: (_) => setState(() => _mode = mode),
                  );
                }).toList(),
              ),
              if (_mode != ImageCreationMode.original) ...[
                const SizedBox(height: 12),
                const Text(
                  '변환 엔진 연결 전까지 미리보기와 등록에는 원본 이미지가 사용됩니다.',
                  style: TextStyle(color: Color(0xFF6F7789)),
                ),
              ],
              const SizedBox(height: 20),
              const Text(
                '장르 태그',
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
              ),
              const SizedBox(height: 10),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: galleryTags.map((tag) {
                  return ChoiceChip(
                    label: Text(tag),
                    selected: _tag == tag,
                    onSelected: (_) => setState(() => _tag = tag),
                  );
                }).toList(),
              ),
              const SizedBox(height: 20),
              SizedBox(
                width: double.infinity,
                child: FilledButton(
                  onPressed: _continue,
                  child: const Text('배경화면 미리보기'),
                ),
              ),
            ],
          ),
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
        backgroundColor: Colors.black,
        appBar: AppBar(
          title: const Text('배경화면 미리보기'),
          backgroundColor: Colors.black,
          foregroundColor: Colors.white,
        ),
        body: SafeArea(
          child: Column(
            children: [
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: ClipRRect(
                    borderRadius: BorderRadius.circular(20),
                    child: SizedBox.expand(
                      child:
                          Image.file(File(widget.filePath), fit: BoxFit.cover),
                    ),
                  ),
                ),
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(18, 4, 18, 8),
                child: SizedBox(
                  width: double.infinity,
                  child: FilledButton.icon(
                    onPressed: _applying ? null : _apply,
                    icon: _applying
                        ? const SizedBox.square(
                            dimension: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : Icon(_applied ? Icons.check : Icons.wallpaper),
                    label: Text(_applied ? '배경화면 적용됨' : '홈 화면에 적용'),
                  ),
                ),
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(18, 0, 18, 18),
                child: SizedBox(
                  width: double.infinity,
                  child: OutlinedButton(
                    onPressed: () => Navigator.pop(context, true),
                    style:
                        OutlinedButton.styleFrom(foregroundColor: Colors.white),
                    child: Text(widget.finishLabel),
                  ),
                ),
              ),
            ],
          ),
        ),
      );
}
