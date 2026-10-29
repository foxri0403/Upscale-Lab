import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

Future<void> runCompanion() async {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const CompanionApp());
}

class CompanionApp extends StatelessWidget {
  const CompanionApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'Upscale Lab Connect', debugShowCheckedModeBanner: false,
    theme: ThemeData(colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xff5964ff)), useMaterial3: true),
    home: const CompanionPage(),
  );
}

class CompanionPage extends StatefulWidget {
  const CompanionPage({super.key});
  @override
  State<CompanionPage> createState() => _CompanionPageState();
}

class _CompanionPageState extends State<CompanionPage> {
  static const channel = MethodChannel('upscale/companion');
  final server = TextEditingController(text: const String.fromEnvironment('API_BASE_URL'));
  final identifier = TextEditingController();
  final password = TextEditingController();
  Map<dynamic, dynamic> state = {};
  Timer? timer;
  bool busy = false, remember = true;
  String? error;
  @override
  void initState() {
    super.initState(); refresh(); timer = Timer.periodic(const Duration(seconds: 3), (_) => refresh());
  }
  Future<void> refresh() async {
    try {
      final value = await channel.invokeMapMethod<dynamic, dynamic>('status');
      if (!mounted) return;
      setState(() { state = value ?? {}; if (server.text.isEmpty) server.text = state['server'] as String? ?? ''; });
    } on PlatformException catch (e) { if (mounted) setState(() => error = e.message); }
    on MissingPluginException { if (mounted) setState(() => error = '이 연결 앱은 Android에서 사용할 수 있습니다.'); }
  }
  Future<void> act(String action) async {
    setState(() { busy = true; error = null; });
    try {
      await channel.invokeMethod<dynamic>(action, action == 'login' ? {
        'server': server.text.trim(), 'identifier': identifier.text.trim(), 'password': password.text, 'remember': remember,
      } : null);
      if (action == 'login') password.clear();
      await refresh();
    } on PlatformException catch (e) { if (mounted) setState(() => error = e.message); }
    finally { if (mounted) setState(() => busy = false); }
  }
  @override
  void dispose() { timer?.cancel(); server.dispose(); identifier.dispose(); password.dispose(); super.dispose(); }
  @override
  Widget build(BuildContext context) {
    final loggedIn = state['loggedIn'] == true;
    final displays = state['displays'] as List? ?? [];
    return Scaffold(backgroundColor: const Color(0xfff6f7fc), appBar: AppBar(title: const Text('Upscale Lab Connect')),
      body: SafeArea(child: Center(child: ConstrainedBox(constraints: const BoxConstraints(maxWidth: 640),
        child: ListView(padding: const EdgeInsets.all(24), children: [
          const Icon(Icons.devices_rounded, size: 60, color: Color(0xff5964ff)), const SizedBox(height: 16),
          Text('웹에서 선택하고, 이 기기에 적용하세요', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 12),
          const Text('웹과 같은 계정으로 로그인하면 이 기기가 자동으로 등록됩니다. 웹의 기기 관리에서 배경화면을 지정해 주세요.'),
          const SizedBox(height: 24),
          if (!loggedIn) ...[
            TextField(controller: server, keyboardType: TextInputType.url, enabled: !busy, decoration: const InputDecoration(labelText: '서버 주소', hintText: 'https://your-server.example', border: OutlineInputBorder())),
            const SizedBox(height: 16),
            TextField(controller: identifier, enabled: !busy, autofillHints: const [AutofillHints.username], decoration: const InputDecoration(labelText: '아이디 또는 이메일', border: OutlineInputBorder())),
            const SizedBox(height: 16),
            TextField(controller: password, enabled: !busy, obscureText: true, autofillHints: const [AutofillHints.password], decoration: const InputDecoration(labelText: '비밀번호', border: OutlineInputBorder())),
            CheckboxListTile(value: remember, contentPadding: EdgeInsets.zero, title: const Text('자동 로그인 및 기기 재시작 시 연결'), onChanged: busy ? null : (v) => setState(() => remember = v ?? false)),
            FilledButton(onPressed: busy ? null : () => act('login'), child: const Text('로그인하고 기기 연결')),
          ] else ...[
            Card(child: Padding(padding: const EdgeInsets.all(18), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(state['account'] as String? ?? '', style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: 8), Text(state['server'] as String? ?? ''), const SizedBox(height: 12),
              Text(state['message'] as String? ?? '연결 확인 중'),
              SwitchListTile(contentPadding: EdgeInsets.zero, title: const Text('배경화면 동기화'), value: state['enabled'] == true,
                onChanged: busy ? null : (v) => act(v ? 'enable' : 'disable')),
              if (state['pendingLive'] == true) FilledButton.icon(onPressed: busy ? null : () => act('activateLive'), icon: const Icon(Icons.wallpaper), label: const Text('Live 2D 적용 확인')),
              TextButton(onPressed: busy ? null : () => act('reconnect'), child: const Text('기기 다시 연결')),
              TextButton(onPressed: busy ? null : () => act('logout'), child: const Text('로그아웃')),
            ]))),
          ],
          if (busy) const LinearProgressIndicator(),
          if (error != null) Padding(padding: const EdgeInsets.symmetric(vertical: 16), child: Text(error!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
          const SizedBox(height: 24), Text('이 기기의 화면 ${displays.length}개', style: Theme.of(context).textTheme.titleLarge),
          Text(state['osVersion'] as String? ?? ''),
          ...displays.map((raw) { final d = raw as Map; final hz = d['refreshRateHz'] as num?;
            return Card(child: ListTile(leading: const Icon(Icons.screenshot_monitor), title: Text(d['name'] as String? ?? '화면'),
              subtitle: Text('${d['width']} × ${d['height']}${hz == null ? '' : ' · ${hz.toStringAsFixed(2)} Hz'}${d['canApply'] == true ? '' : ' · 정보 조회 전용'}')));
          }),
          const SizedBox(height: 24), const Text('수집 정보: 연결된 화면 개수·해상도·주사율·회전·연결 상태, OS 및 이 앱이 적용한 배경화면의 식별 정보. 외부 배경화면 사진과 다른 앱 목록은 전송하지 않습니다.', style: TextStyle(fontSize: 13, color: Colors.black54)),
          const SizedBox(height: 16), const Text('로그아웃하면 동기화를 중지합니다. 이미 적용한 정적 배경화면은 유지되며 Live 2D 움직임은 중지됩니다.', style: TextStyle(fontSize: 13, color: Colors.black54)),
        ]),
      ))));
  }
}
