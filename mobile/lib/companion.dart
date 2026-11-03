import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

Future<void> runCompanion() async {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const CompanionApp());
}

const primary = Color(0xff536dfe);
const ink = Color(0xff172033);

class CompanionApp extends StatelessWidget {
  const CompanionApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(
        title: 'Upscale Lab Connect',
        debugShowCheckedModeBanner: false,
        theme: ThemeData(
          colorScheme: ColorScheme.fromSeed(seedColor: primary),
          useMaterial3: true,
          scaffoldBackgroundColor: const Color(0xfffafbff),
          inputDecorationTheme: InputDecorationTheme(
            filled: true,
            fillColor: Colors.white,
            border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
            contentPadding: const EdgeInsets.all(16),
          ),
          filledButtonTheme: FilledButtonThemeData(
              style: FilledButton.styleFrom(
            backgroundColor: primary,
            minimumSize: const Size.fromHeight(52),
            shape:
                RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
          )),
        ),
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
  final identifier = TextEditingController();
  final password = TextEditingController();
  Map<dynamic, dynamic> state = {};
  Timer? timer;
  bool busy = false, remember = true, hidePassword = true;
  String? error;

  @override
  void initState() {
    super.initState();
    refresh();
    timer = Timer.periodic(const Duration(seconds: 3), (_) => refresh());
  }

  Future<void> refresh() async {
    try {
      final value = await channel.invokeMapMethod<dynamic, dynamic>('status');
      if (!mounted) return;
      setState(() {
        state = value ?? {};
      });
    } on PlatformException catch (e) {
      if (mounted) setState(() => error = e.message);
    } on MissingPluginException {
      if (mounted) setState(() => error = '이 연결 앱은 Android에서 사용할 수 있습니다.');
    }
  }

  Future<void> act(String action) async {
    if (busy) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      // The native service uses the origin embedded at build time. No URL or
      // device key is accepted from an editable Flutter control.
      await channel.invokeMethod<dynamic>(
          action,
          action == 'login'
              ? {
                  'identifier': identifier.text.trim(),
                  'password': password.text,
                  'remember': remember,
                }
              : null);
      if (action == 'login') password.clear();
      await refresh();
    } on PlatformException catch (e) {
      if (mounted) setState(() => error = e.message);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  void dispose() {
    timer?.cancel();
    identifier.dispose();
    password.dispose();
    super.dispose();
  }

  Widget panel(Widget child, {Color color = Colors.white}) => SizedBox(
        width: double.infinity,
        child: Material(
          color: color,
          shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(16),
              side: const BorderSide(color: Color(0xffe1e6f0))),
          child: Padding(padding: const EdgeInsets.all(20), child: child),
        ),
      );
  Widget heading(String text) => Text(text,
      style: const TextStyle(
          fontSize: 24, fontWeight: FontWeight.w700, color: ink));

  @override
  Widget build(BuildContext context) {
    final loggedIn = state['loggedIn'] == true;
    final displays = state['displays'] as List? ?? [];
    final tablet = MediaQuery.sizeOf(context).shortestSide >= 600;
    return Scaffold(
        body: SafeArea(
            child: Center(
                child: ConstrainedBox(
      constraints: const BoxConstraints(maxWidth: 640),
      child: ListView(padding: const EdgeInsets.all(20), children: [
        Row(children: [
          ClipRRect(
              borderRadius: BorderRadius.circular(12),
              child: Image.asset('assets/app-icon.png', width: 52, height: 52)),
          const SizedBox(width: 12),
          const Expanded(
              child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                Text('Upscale Lab',
                    style: TextStyle(
                        fontSize: 21, fontWeight: FontWeight.w800, color: ink)),
                Text('Connect', style: TextStyle(color: Color(0xff778196))),
              ])),
        ]),
        const SizedBox(height: 32),
        if (!loggedIn) ...[
          heading('이 기기를 연결하세요'),
          const SizedBox(height: 10),
          const Text('웹과 같은 계정으로 로그인하면 기기가 자동으로 연결됩니다.',
              style: TextStyle(color: Color(0xff778196), height: 1.6)),
          const SizedBox(height: 24),
          panel(AutofillGroup(
              child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                TextField(
                    controller: identifier,
                    enabled: !busy,
                    autofillHints: const [AutofillHints.username],
                    decoration: const InputDecoration(labelText: '아이디 또는 이메일'),
                    textInputAction: TextInputAction.next),
                const SizedBox(height: 18),
                TextField(
                    controller: password,
                    enabled: !busy,
                    obscureText: hidePassword,
                    autofillHints: const [AutofillHints.password],
                    onSubmitted: busy ? null : (_) => act('login'),
                    decoration: InputDecoration(
                        labelText: '비밀번호',
                        suffixIcon: IconButton(
                            tooltip: hidePassword ? '비밀번호 표시' : '비밀번호 숨기기',
                            onPressed: () =>
                                setState(() => hidePassword = !hidePassword),
                            icon: Icon(hidePassword
                                ? Icons.visibility_outlined
                                : Icons.visibility_off_outlined)))),
                const SizedBox(height: 12),
                CheckboxListTile(
                    value: remember,
                    contentPadding: EdgeInsets.zero,
                    controlAffinity: ListTileControlAffinity.leading,
                    title: const Text('자동 로그인'),
                    subtitle: const Text('기기 재시작 시에도 연결을 복원합니다.'),
                    onChanged: busy
                        ? null
                        : (v) => setState(() => remember = v ?? false)),
                const SizedBox(height: 16),
                FilledButton(
                    onPressed: busy ? null : () => act('login'),
                    child: const Text('로그인하고 기기 연결')),
              ]))),
          const SizedBox(height: 20),
          const Text('배경화면은 웹의 기기 관리에서 선택해 주세요.',
              textAlign: TextAlign.center,
              style: TextStyle(color: Color(0xff778196), height: 1.6)),
        ] else ...[
          heading('내 기기'),
          const SizedBox(height: 16),
          panel(
              Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                const Row(children: [
                  Icon(Icons.check_circle, color: Color(0xff299c67), size: 20),
                  SizedBox(width: 8),
                  Text('연결됨',
                      style: TextStyle(
                          color: Color(0xff299c67),
                          fontWeight: FontWeight.w700))
                ]),
                const SizedBox(height: 14),
                Text(state['account'] as String? ?? '',
                    style: const TextStyle(
                        fontSize: 18, fontWeight: FontWeight.w600)),
                const SizedBox(height: 8),
                Text(state['message'] as String? ?? '연결 확인 중',
                    style: const TextStyle(height: 1.6)),
              ]),
              color: const Color(0xffeef0ff)),
          const SizedBox(height: 16),
          panel(SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('배경화면 동기화'),
              subtitle: const Text('웹에서 선택한 배경화면을 적용합니다.'),
              value: state['enabled'] == true,
              onChanged: busy ? null : (v) => act(v ? 'enable' : 'disable'))),
          if (state['pendingLive'] == true) ...[
            const SizedBox(height: 16),
            panel(
                Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const Text('Live 2D 승인 필요',
                          style: TextStyle(fontWeight: FontWeight.w700)),
                      const SizedBox(height: 8),
                      const Text('기기에서 최초 적용을 확인해 주세요.'),
                      const SizedBox(height: 16),
                      FilledButton.icon(
                          onPressed: busy ? null : () => act('activateLive'),
                          icon: const Icon(Icons.wallpaper),
                          label: const Text('Live 2D 적용 확인')),
                    ]),
                color: const Color(0xfffff8e9)),
          ],
        ],
        if (busy)
          const Padding(
              padding: EdgeInsets.symmetric(vertical: 16),
              child: LinearProgressIndicator()),
        if (error != null)
          Padding(
              padding: const EdgeInsets.symmetric(vertical: 16),
              child: Semantics(
                  liveRegion: true,
                  child: Text(error!,
                      style: TextStyle(
                          color: Theme.of(context).colorScheme.error)))),
        const SizedBox(height: 28),
        Text('이 기기의 화면 ${displays.length}개',
            style: const TextStyle(
                fontSize: 18, fontWeight: FontWeight.w700, color: ink)),
        const SizedBox(height: 6),
        Text(state['osVersion'] as String? ?? '',
            style: const TextStyle(color: Color(0xff778196))),
        const SizedBox(height: 14),
        ...displays.map((raw) {
          final d = raw as Map;
          final hz = d['refreshRateHz'] as num?;
          return Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: panel(
                  Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Icon(
                    d['canApply'] == true
                        ? (tablet
                            ? Icons.tablet_android_rounded
                            : Icons.phone_android_rounded)
                        : Icons.monitor,
                    size: 32,
                    color: primary),
                const SizedBox(width: 16),
                Expanded(
                    child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                      Text(d['name'] as String? ?? '화면',
                          style: const TextStyle(fontWeight: FontWeight.w600)),
                      const SizedBox(height: 8),
                      Text(
                          '${d['width']} × ${d['height']}${hz == null ? '' : ' · ${hz.toStringAsFixed(2)} Hz'}',
                          style: const TextStyle(color: Color(0xff778196))),
                      const SizedBox(height: 6),
                      Text(d['canApply'] == true ? '홈 화면 · 잠금 화면' : '정보 조회 전용',
                          style: const TextStyle(fontSize: 13, color: primary)),
                    ])),
              ])));
        }),
        if (loggedIn)
          Wrap(alignment: WrapAlignment.center, spacing: 16, children: [
            TextButton.icon(
                onPressed: busy ? null : () => act('reconnect'),
                icon: const Icon(Icons.refresh),
                label: const Text('기기 다시 연결')),
            TextButton(
                onPressed: busy ? null : () => act('logout'),
                child: const Text('로그아웃')),
          ]),
        const SizedBox(height: 12),
        const ExpansionTile(
            tilePadding: EdgeInsets.zero,
            title: Text('수집 정보 안내', style: TextStyle(fontSize: 14)),
            children: [
              Padding(
                  padding: EdgeInsets.only(bottom: 16),
                  child: Text(
                      '연결된 화면 개수·해상도·주사율·회전·연결 상태, OS와 이 앱이 적용한 배경화면의 식별 정보를 전송합니다. 외부 배경화면 사진과 다른 앱 목록은 전송하지 않습니다.\n\n로그아웃하면 동기화를 중지합니다. 적용한 정적 배경화면은 유지되며 Live 2D 움직임은 중지됩니다.',
                      style: TextStyle(
                          fontSize: 13,
                          height: 1.7,
                          color: Color(0xff778196)))),
            ]),
      ]),
    ))));
  }
}
