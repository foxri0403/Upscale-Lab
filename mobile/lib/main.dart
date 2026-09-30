import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:image_picker/image_picker.dart';

import 'models/live_layer_models.dart';
import 'services/api_client.dart';
import 'widgets/parallax_preview.dart';

void main() {
  const apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.0.2.2:5080',
  );
  runApp(LiveLayerApp(apiClient: ApiClient(baseUrl: apiBaseUrl)));
}

class LiveLayerApp extends StatelessWidget {
  const LiveLayerApp({super.key, required this.apiClient});

  final ApiClient apiClient;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'LiveLayer',
      theme: ThemeData(colorScheme: ColorScheme.fromSeed(seedColor: Colors.deepPurple)),
      home: LoginPage(apiClient: apiClient),
    );
  }
}

class LoginPage extends StatefulWidget {
  const LoginPage({super.key, required this.apiClient});

  final ApiClient apiClient;

  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _busy = false;

  Future<void> _login() async {
    setState(() => _busy = true);
    try {
      await widget.apiClient.login(_email.text.trim(), _password.text);
      if (!mounted) return;
      await Navigator.of(context).pushReplacement(
        MaterialPageRoute(builder: (_) => ProjectListPage(apiClient: widget.apiClient)),
      );
    } catch (error) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('$error')));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text('LiveLayer', style: Theme.of(context).textTheme.displaySmall),
                const SizedBox(height: 32),
                TextField(controller: _email, keyboardType: TextInputType.emailAddress, decoration: const InputDecoration(labelText: 'Email')),
                TextField(controller: _password, obscureText: true, decoration: const InputDecoration(labelText: 'Password')),
                const SizedBox(height: 24),
                FilledButton(onPressed: _busy ? null : _login, child: Text(_busy ? 'Signing in…' : 'Sign in')),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class ProjectListPage extends StatefulWidget {
  const ProjectListPage({super.key, required this.apiClient});

  final ApiClient apiClient;

  @override
  State<ProjectListPage> createState() => _ProjectListPageState();
}

class _ProjectListPageState extends State<ProjectListPage> {
  late Future<List<LiveLayerProject>> _projects = widget.apiClient.getProjects();
  bool _creating = false;

  void _reload() => setState(() => _projects = widget.apiClient.getProjects());

  Future<void> _createProject() async {
    final picked = await ImagePicker().pickImage(source: ImageSource.gallery);
    if (picked == null) return;
    setState(() => _creating = true);
    try {
      final decoded = await ui.decodeImageFromList(await picked.readAsBytes());
      final project = await widget.apiClient.createProject(
        title: picked.name,
        filePath: picked.path,
        width: decoded.width,
        height: decoded.height,
      );
      await widget.apiClient.startProcessing(project.id);
      _reload();
    } catch (error) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('$error')));
    } finally {
      if (mounted) setState(() => _creating = false);
    }
  }

  Future<void> _open(LiveLayerProject summary) async {
    try {
      final project = await widget.apiClient.getProject(summary.id);
      if (!mounted) return;
      await Navigator.of(context).push(
        MaterialPageRoute(builder: (_) => PreviewPage(project: project, apiClient: widget.apiClient)),
      );
      _reload();
    } catch (error) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('$error')));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('My LiveLayers')),
      body: FutureBuilder<List<LiveLayerProject>>(
        future: _projects,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) return const Center(child: CircularProgressIndicator());
          if (snapshot.hasError) return Center(child: Text('${snapshot.error}'));
          final projects = snapshot.data ?? const [];
          if (projects.isEmpty) return const Center(child: Text('Upload an image to create your first LiveLayer.'));
          return RefreshIndicator(
            onRefresh: () async => _reload(),
            child: ListView.builder(
              itemCount: projects.length,
              itemBuilder: (context, index) {
                final project = projects[index];
                return ListTile(
                  leading: Image.network(project.originalImageUrl, width: 56, height: 56, fit: BoxFit.cover),
                  title: Text(project.title),
                  subtitle: Text(project.status),
                  onTap: () => _open(project),
                );
              },
            ),
          );
        },
      ),
      floatingActionButton: FloatingActionButton(
        onPressed: _creating ? null : _createProject,
        child: _creating ? const CircularProgressIndicator() : const Icon(Icons.add_photo_alternate_outlined),
      ),
    );
  }
}

class PreviewPage extends StatefulWidget {
  const PreviewPage({super.key, required this.project, required this.apiClient});

  final LiveLayerProject project;
  final ApiClient apiClient;

  @override
  State<PreviewPage> createState() => _PreviewPageState();
}

class _PreviewPageState extends State<PreviewPage> {
  static const _wallpaperChannel = MethodChannel('live_layer/wallpaper');
  double _sensitivity = 1;

  @override
  void initState() {
    super.initState();
    widget.apiClient.getSensorSensitivity().then((value) {
      if (mounted) setState(() => _sensitivity = value.clamp(0, 2).toDouble());
    });
  }

  Future<void> _openWallpaperPicker() async {
    try {
      await _wallpaperChannel.invokeMethod<void>('openWallpaperPicker', {'projectId': widget.project.id});
    } on PlatformException catch (error) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(error.message ?? '$error')));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.project.title),
        actions: [IconButton(onPressed: _openWallpaperPicker, icon: const Icon(Icons.wallpaper))],
      ),
      body: Column(
        children: [
          Expanded(
            child: widget.project.layers.isEmpty
                ? Center(child: Text('Processing status: ${widget.project.status}'))
                : AspectRatio(
                    aspectRatio: 9 / 16,
                    child: ParallaxPreview(layers: widget.project.layers, sensitivity: _sensitivity),
                  ),
          ),
          ListTile(
            title: const Text('Sensor sensitivity'),
            subtitle: Slider(
              value: _sensitivity,
              min: 0,
              max: 2,
              divisions: 20,
              onChanged: (value) => setState(() => _sensitivity = value),
              onChangeEnd: widget.apiClient.updateSensorSensitivity,
            ),
          ),
        ],
      ),
    );
  }
}
