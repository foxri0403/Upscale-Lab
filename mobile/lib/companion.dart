import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

Future<void> runCompanion() async {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const CompanionApp());
}

const primary = Color(0xff536dfe);
const ink = Color(0xff172033);
const muted = Color(0xff778196);
const testApp = bool.fromEnvironment('COMPANION_LOCAL_TEST');
const privacyTitle = '기기·화면 정보 수집·이용 동의';
const privacyDetails =
    '수집·이용 목적\n기기를 계정에 연결하고 웹에서 선택한 배경화면과 적용 상태를 동기화합니다.\n\n수집·전송 항목\n기기 이름, OS·앱 버전, 화면 이름·개수·해상도·주사율·배율·회전·위치·주 화면·연결 상태, 이 앱이 적용한 배경화면의 식별 정보와 적용 상태.\n\n외부 배경화면 사진, 다른 앱 목록, 제조사·모델·CPU 구조는 전송하지 않습니다. 동의 및 로그인 확인 후에만 화면을 감지합니다. 로그아웃하면 수집·동기화를 중지합니다.\n\n동의를 거부할 수 있으며, 거부하면 연결 앱을 사용할 수 없습니다.';

class CompanionApp extends StatelessWidget {
  const CompanionApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(
        title: testApp ? 'Upscale Lab Connect (테스트 앱)' : 'Upscale Lab Connect',
        debugShowCheckedModeBanner: false,
        theme: ThemeData(
          colorScheme: ColorScheme.fromSeed(seedColor: primary),
          useMaterial3: true,
          scaffoldBackgroundColor: const Color(0xfffafbff),
          textTheme:
              const TextTheme(bodyMedium: TextStyle(color: ink, height: 1.5)),
          inputDecorationTheme: InputDecorationTheme(
            filled: true,
            fillColor: const Color(0xfffafbff),
            border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(12),
                borderSide: const BorderSide(color: Color(0xffdce1ed))),
            enabledBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(12),
                borderSide: const BorderSide(color: Color(0xffdce1ed))),
            contentPadding: const EdgeInsets.all(16),
          ),
          filledButtonTheme: FilledButtonThemeData(
              style: FilledButton.styleFrom(
            backgroundColor: primary,
            minimumSize: const Size.fromHeight(52),
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
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
  final scroll = ScrollController();
  Map<dynamic, dynamic> state = {};
  Timer? timer;
  bool busy = false, remember = true, hidePassword = true, consent = false;
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
      if (mounted) setState(() => state = value ?? {});
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
      // The native service also enforces consent and authentication. Neither
      // the UI nor channel arguments can change the embedded server origin.
      await channel.invokeMethod<dynamic>(
          action,
          action == 'login'
              ? {
                  'identifier': identifier.text.trim(),
                  'password': password.text,
                  'remember': !testApp && remember
                }
              : action == 'acceptPrivacy'
                  ? {'accepted': consent}
                  : null);
      if (action == 'login') password.clear();
      await refresh();
      if (mounted &&
          scroll.hasClients &&
          ['login', 'logout', 'acceptPrivacy'].contains(action)) {
        scroll.jumpTo(0);
      }
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
    scroll.dispose();
    super.dispose();
  }

  Widget panel(Widget child, {Color color = Colors.white}) => SizedBox(
      width: double.infinity,
      child: Material(
          color: color,
          shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(18),
              side: const BorderSide(color: Color(0xffe1e6f0))),
          child: Padding(padding: const EdgeInsets.all(20), child: child)));
  Widget heading(String text) => Text(text,
      style: const TextStyle(
          fontSize: 24, fontWeight: FontWeight.w800, color: ink));
  Widget pill(String text) => Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
          color: const Color(0xffeeebff),
          borderRadius: BorderRadius.circular(20)),
      child: Text(text,
          style: const TextStyle(
              color: primary, fontSize: 12, fontWeight: FontWeight.w600)));

  List<Widget> loginView() => [
        heading('이 기기를 연결하세요'),
        const SizedBox(height: 8),
        const Text(
            testApp
                ? '아무 정보나 입력해도 시작할 수 있습니다. 빈칸도 가능합니다.'
                : '웹과 같은 계정으로 로그인해 주세요.',
            style: TextStyle(color: muted)),
        const SizedBox(height: 26),
        panel(AutofillGroup(
            child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
              const Text('아이디 또는 이메일',
                  style: TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(height: 8),
              TextField(
                  controller: identifier,
                  enabled: !busy,
                  autofillHints: const [AutofillHints.username],
                  decoration: const InputDecoration(hintText: '아이디 또는 이메일'),
                  textInputAction: TextInputAction.next),
              const SizedBox(height: 20),
              const Text('비밀번호', style: TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(height: 8),
              TextField(
                  controller: password,
                  enabled: !busy,
                  obscureText: hidePassword,
                  autofillHints: const [AutofillHints.password],
                  onSubmitted: busy ? null : (_) => act('login'),
                  decoration: InputDecoration(
                      hintText: '비밀번호',
                      suffixIcon: IconButton(
                          tooltip: hidePassword ? '비밀번호 표시' : '비밀번호 숨기기',
                          onPressed: busy
                              ? null
                              : () =>
                                  setState(() => hidePassword = !hidePassword),
                          icon: Icon(hidePassword
                              ? Icons.visibility_outlined
                              : Icons.visibility_off_outlined)))),
              const SizedBox(height: 8),
              CheckboxListTile(
                  value: testApp ? false : remember,
                  contentPadding: EdgeInsets.zero,
                  controlAffinity: ListTileControlAffinity.leading,
                  title: const Text('자동 로그인'),
                  onChanged: busy || testApp
                      ? null
                      : (v) => setState(() => remember = v ?? false)),
              const SizedBox(height: 12),
              FilledButton(
                  onPressed: busy ? null : () => act('login'),
                  child: const Text('로그인하고 연결')),
            ]))),
        const SizedBox(height: 22),
        const Text(
            testApp
                ? '테스트 앱은 서버 연결 없이 UI를 확인합니다.\n입력 내용은 전송하거나 저장하지 않습니다.'
                : '배경화면은 웹의 기기 관리에서 선택해 주세요.',
            textAlign: TextAlign.center,
            style: TextStyle(color: muted, height: 1.6)),
      ];

  List<Widget> consentView() => [
        heading('연결 전에 확인해 주세요'),
        const SizedBox(height: 10),
        const Text('로그인 전에 기기·화면 정보 수집 안내를 확인해 주세요.',
            style: TextStyle(color: muted)),
        const SizedBox(height: 24),
        panel(Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Text(privacyTitle,
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
          if (testApp)
            const Padding(
              padding: EdgeInsets.only(top: 16),
              child: Text('테스트 앱은 화면 정보를 이 기기에서만 확인하며 서버로 전송하지 않습니다.',
                  style: TextStyle(color: primary)),
            ),
          const SizedBox(height: 18),
          const Text(privacyDetails, style: TextStyle(height: 1.7)),
          const SizedBox(height: 16),
          CheckboxListTile(
              value: consent,
              contentPadding: EdgeInsets.zero,
              controlAffinity: ListTileControlAffinity.leading,
              title: const Text('[필수] 기기·화면 정보 수집·이용에 동의합니다.'),
              onChanged:
                  busy ? null : (v) => setState(() => consent = v ?? false)),
          const SizedBox(height: 12),
          FilledButton(
              onPressed: busy || !consent ? null : () => act('acceptPrivacy'),
              child: const Text('동의하고 계속')),
        ])),
      ];

  List<Widget> connectedView(bool tablet) {
    final displays = state['displays'] as List? ?? [];
    final screenIcon =
        tablet ? Icons.tablet_android_rounded : Icons.phone_android_rounded;
    return [
      Wrap(
          alignment: WrapAlignment.spaceBetween,
          crossAxisAlignment: WrapCrossAlignment.center,
          spacing: 20,
          runSpacing: 12,
          children: [
            heading('내 기기'),
            Container(
                padding:
                    const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                decoration: BoxDecoration(
                    color: const Color(0xffdff6e6),
                    borderRadius: BorderRadius.circular(24)),
                child: const Text(testApp ? '● 테스트 중' : '● 연결됨',
                    style: TextStyle(
                        color: Color(0xff24834d),
                        fontWeight: FontWeight.w700))),
          ]),
      const SizedBox(height: 18),
      panel(
          Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Icon(screenIcon, size: 48, color: const Color(0xff7d85c2)),
            const SizedBox(width: 18),
            Expanded(
                child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                  Text(tablet ? '내 Android 태블릿' : '내 Android',
                      style: const TextStyle(
                          fontSize: 19, fontWeight: FontWeight.w800)),
                  Text(
                      '화면 ${displays.length}개 · ${state['osVersion'] ?? 'Android'}',
                      style: const TextStyle(color: muted)),
                  const SizedBox(height: 6),
                  Text(state['account'] as String? ?? '',
                      style: const TextStyle(color: muted)),
                  const SizedBox(height: 4),
                  Text(state['message'] as String? ?? '웹과 연결되어 있어요.',
                      style: const TextStyle(color: Color(0xff24834d))),
                ])),
          ]),
          color: const Color(0xffeeebff)),
      const SizedBox(height: 14),
      panel(SwitchListTile(
          contentPadding: EdgeInsets.zero,
          title: const Text(testApp ? '동기화 미리보기' : '배경화면 동기화',
              style: TextStyle(fontWeight: FontWeight.w700)),
          value: state['enabled'] == true,
          onChanged: busy ? null : (v) => act(v ? 'enable' : 'disable'))),
      const SizedBox(height: 14),
      panel(Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        const Text('이 기기의 화면',
            style: TextStyle(fontSize: 19, fontWeight: FontWeight.w800)),
        const SizedBox(height: 20),
        if (displays.isEmpty)
          const Text('화면 정보를 확인하고 있어요.', style: TextStyle(color: muted)),
        for (final raw in displays)
          Padding(
              padding: const EdgeInsets.only(bottom: 14),
              child:
                  Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Icon(
                    raw['canApply'] == true
                        ? screenIcon
                        : Icons.monitor_rounded,
                    size: 40,
                    color: const Color(0xff8f97ce)),
                const SizedBox(width: 18),
                Expanded(
                    child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                      Text(
                          raw['canApply'] == true
                              ? '내장 화면'
                              : (raw['name'] as String? ?? '외부 화면'),
                          style: const TextStyle(fontWeight: FontWeight.w700)),
                      const SizedBox(height: 4),
                      Text(
                          '${raw['width']} × ${raw['height']}${raw['refreshRateHz'] is num ? ' · ${(raw['refreshRateHz'] as num).toStringAsFixed(2)} Hz' : ''}',
                          style: const TextStyle(color: muted)),
                      const SizedBox(height: 8),
                      Wrap(
                          spacing: 6,
                          runSpacing: 6,
                          children: raw['canApply'] == true
                              ? [pill('홈 화면'), pill('잠금 화면')]
                              : [pill('정보 조회 전용')]),
                    ])),
              ])),
      ])),
      if (state['pendingLive'] == true) ...[
        const SizedBox(height: 14),
        panel(
            Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              const Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Icon(Icons.info_outline, color: Color(0xffdf980c)),
                    SizedBox(width: 10),
                    Expanded(
                        child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                          Text('Live 2D 승인 필요',
                              style: TextStyle(
                                  fontWeight: FontWeight.w800, fontSize: 17)),
                          Text('기기에서 최초 적용을 확인해 주세요.',
                              style: TextStyle(color: muted)),
                        ])),
                  ]),
              const SizedBox(height: 14),
              OutlinedButton(
                  onPressed: busy ? null : () => act('activateLive'),
                  child: const Text('Live 2D 적용 확인')),
            ]),
            color: const Color(0xfffff8e9)),
      ],
      const SizedBox(height: 16),
      Wrap(alignment: WrapAlignment.center, spacing: 18, children: [
        TextButton.icon(
            onPressed: busy ? null : () => act('reconnect'),
            icon: const Icon(Icons.refresh),
            label: const Text('기기 다시 연결')),
        TextButton.icon(
            onPressed: busy ? null : () => act('logout'),
            icon: const Icon(Icons.logout, size: 18),
            label: const Text('로그아웃')),
      ]),
      ExpansionTile(
          tilePadding: EdgeInsets.zero,
          title: const Text('수집 정보 안내',
              style: TextStyle(fontSize: 14, color: muted)),
          children: const [
            Padding(
                padding: EdgeInsets.only(bottom: 16),
                child: Text(privacyDetails,
                    style: TextStyle(fontSize: 13, height: 1.7, color: muted)))
          ]),
    ];
  }

  @override
  Widget build(BuildContext context) {
    final accepted = state['privacyAccepted'] == true;
    final loggedIn = accepted && state['loggedIn'] == true;
    final tablet = MediaQuery.sizeOf(context).shortestSide >= 600;
    return Scaffold(
        appBar: testApp
            ? AppBar(
                title: const Text('테스트 앱'),
                backgroundColor: const Color(0xffeeebff),
                foregroundColor: primary,
                automaticallyImplyLeading: false,
              )
            : null,
        body: DecoratedBox(
          decoration: const BoxDecoration(
              gradient: LinearGradient(
                  begin: Alignment.topRight,
                  end: Alignment.bottomLeft,
                  colors: [
                Color(0xfff0ecff),
                Color(0xfffafbff),
                Color(0xfffafbff)
              ],
                  stops: [
                0,
                .3,
                1
              ])),
          child: Material(
              color: Colors.transparent,
              child: SafeArea(
                  child: Center(
                      child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 680),
                child: ListView(
                    controller: scroll,
                    padding: EdgeInsets.all(tablet ? 32 : 22),
                    children: [
                      Row(children: [
                        Image.asset('assets/app-icon.png',
                            width: 58, height: 58),
                        const SizedBox(width: 12),
                        const Expanded(
                            child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                              Text('Upscale Lab',
                                  style: TextStyle(
                                      fontSize: 22,
                                      fontWeight: FontWeight.w800,
                                      color: ink)),
                              Text('Connect',
                                  style: TextStyle(
                                      fontSize: 17,
                                      color: muted,
                                      fontWeight: FontWeight.w600)),
                            ])),
                      ]),
                      const SizedBox(height: 34),
                      if (state.isEmpty && error == null)
                        const Text('연결 준비 중…',
                            textAlign: TextAlign.center,
                            style: TextStyle(color: muted))
                      else if (!accepted)
                        ...consentView()
                      else if (!loggedIn)
                        ...loginView()
                      else
                        ...connectedView(tablet),
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
                                        color: Theme.of(context)
                                            .colorScheme
                                            .error)))),
                    ]),
              )))),
        ));
  }
}
