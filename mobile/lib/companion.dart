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
const privacyTitle = '기기·화면 정보 수집·이용 동의';
const privacyDetails =
    '수집·이용 목적\n기기를 계정에 연결하고 웹에서 선택한 배경화면과 적용 상태를 동기화합니다.\n\n수집·전송 항목\n기기 이름, OS·앱 버전, 화면 이름·개수·해상도·주사율·배율·회전·위치·주 화면·연결 상태, 이 앱이 적용한 배경화면의 식별 정보와 적용 상태.\n\n외부 배경화면 사진, 다른 앱 목록, 제조사·모델·CPU 구조는 전송하지 않습니다. 동의 및 로그인 확인 후에만 화면을 감지합니다. 로그아웃하면 수집·동기화를 중지합니다.\n\n동의를 거부할 수 있으며, 거부하면 연결 앱을 사용할 수 없습니다.';

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
          textTheme:
              const TextTheme(bodyMedium: TextStyle(color: ink, height: 1.5)),
          inputDecorationTheme: InputDecorationTheme(
              filled: true,
              fillColor: const Color(0xfffafbff),
              border:
                  OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
              contentPadding: const EdgeInsets.all(16)),
          filledButtonTheme: FilledButtonThemeData(
              style: FilledButton.styleFrom(
                  backgroundColor: primary,
                  minimumSize: const Size.fromHeight(52),
                  padding:
                      const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
                  shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(12))))),
      home: const CompanionPage());
}

class CompanionPage extends StatefulWidget {
  const CompanionPage({super.key});
  @override
  State<CompanionPage> createState() => _CompanionPageState();
}

class _CompanionPageState extends State<CompanionPage>
    with WidgetsBindingObserver {
  static const channel = MethodChannel('upscale/companion');
  final identifier = TextEditingController(),
      password = TextEditingController();
  final scroll = ScrollController();
  Map<dynamic, dynamic> state = {};
  Timer? timer, identifierTimer;
  bool busy = false,
      remember = true,
      saveIdentifier = false,
      hidePassword = true,
      consent = false;
  bool identityLoaded = false, livePrompt = false, resumed = true;
  VoidCallback? updateAccount;
  String? error;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    identifier.addListener(() {
      identifierTimer?.cancel();
      if (saveIdentifier) {
        identifierTimer = Timer(const Duration(milliseconds: 400), () async {
          try {
            await channel.invokeMethod<dynamic>('saveIdentifier',
                {'enabled': true, 'identifier': identifier.text.trim()});
          } on PlatformException {
            // Login saves the selected identifier again; keep typing responsive.
          }
        });
      }
    });
    refresh();
    timer = Timer.periodic(const Duration(seconds: 3), (_) => refresh());
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState value) {
    resumed = value == AppLifecycleState.resumed;
    if (resumed) refresh();
  }

  Future<void> refresh() async {
    try {
      final value = await channel.invokeMapMethod<dynamic, dynamic>('status');
      if (!mounted) return;
      final wasLoggedIn = state['loggedIn'] == true;
      setState(() {
        state = value ?? {};
        if (!identityLoaded || (wasLoggedIn && state['loggedIn'] != true)) {
          identityLoaded = true;
          saveIdentifier = state['saveIdentifier'] == true;
          identifier.text =
              saveIdentifier ? state['savedIdentifier'] as String? ?? '' : '';
        }
      });
      updateAccount?.call();
      if (resumed &&
          state['loggedIn'] == true &&
          state['needsLiveSetup'] == true &&
          !livePrompt) {
        livePrompt = true;
        try {
          await channel.invokeMethod<dynamic>('activateLive');
        } finally {
          livePrompt = false;
        }
      }
    } on PlatformException catch (e) {
      if (mounted) {
        setState(() => error = e.message);
        updateAccount?.call();
      }
    } on MissingPluginException {
      if (mounted) setState(() => error = '이 연결 앱은 Android에서 사용할 수 있습니다.');
    }
  }

  Future<bool> act(String action, [Map<String, dynamic>? arguments]) async {
    if (busy) return false;
    setState(() {
      busy = true;
      error = null;
    });
    updateAccount?.call();
    try {
      await channel.invokeMethod<dynamic>(
          action,
          arguments ??
              (action == 'login'
                  ? {
                      'identifier': identifier.text.trim(),
                      'password': password.text,
                      'remember': remember,
                      'saveIdentifier': saveIdentifier
                    }
                  : action == 'acceptPrivacy'
                      ? {'accepted': consent}
                      : null));
      await refresh();
      if (mounted &&
          scroll.hasClients &&
          ['login', 'logout', 'acceptPrivacy'].contains(action)) {
        scroll.jumpTo(0);
      }
      return true;
    } on PlatformException catch (e) {
      if (mounted) setState(() => error = e.message);
      return false;
    } finally {
      if (action == 'login') password.clear();
      if (mounted) {
        setState(() => busy = false);
        updateAccount?.call();
      }
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    timer?.cancel();
    identifierTimer?.cancel();
    identifier.dispose();
    password.dispose();
    scroll.dispose();
    super.dispose();
  }

  Widget panel(Widget child) => SizedBox(
      width: double.infinity,
      child: Material(
          color: Colors.white,
          shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(18),
              side: const BorderSide(color: Color(0xffe1e6f0))),
          child: Padding(padding: const EdgeInsets.all(20), child: child)));
  Widget heading(String text) => Text(text,
      style: const TextStyle(
          fontSize: 23, fontWeight: FontWeight.w800, color: ink));
  Widget avatar(double size) {
    final raw = state['profileImageUrl'] as String?;
    final url = Uri.tryParse(raw ?? '');
    return ClipOval(
        child: SizedBox(
            width: size,
            height: size,
            child: raw != null && url?.scheme == 'https'
                ? Image.network(raw,
                    fit: BoxFit.cover,
                    errorBuilder: (_, __, ___) =>
                        Icon(Icons.account_circle, size: size, color: primary))
                : Icon(Icons.account_circle, size: size, color: primary)));
  }

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
        const Text('웹과 같은 계정으로 로그인해 주세요.', style: TextStyle(color: muted)),
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
                          onPressed: busy
                              ? null
                              : () =>
                                  setState(() => hidePassword = !hidePassword),
                          icon: Icon(hidePassword
                              ? Icons.visibility_outlined
                              : Icons.visibility_off_outlined)))),
              const SizedBox(height: 12),
              Wrap(spacing: 14, runSpacing: 0, children: [
                Row(mainAxisSize: MainAxisSize.min, children: [
                  Checkbox(
                      value: saveIdentifier,
                      onChanged: busy
                          ? null
                          : (v) async {
                              identifierTimer?.cancel();
                              setState(() => saveIdentifier = v ?? false);
                              await act('saveIdentifier', {
                                'enabled': saveIdentifier,
                                'identifier': identifier.text.trim()
                              });
                            }),
                  const Text('아이디 저장')
                ]),
                Row(mainAxisSize: MainAxisSize.min, children: [
                  Checkbox(
                      value: remember,
                      onChanged: busy
                          ? null
                          : (v) => setState(() => remember = v ?? false)),
                  const Text('자동 로그인')
                ]),
              ]),
              const SizedBox(height: 14),
              FilledButton(
                  onPressed: busy ? null : () => act('login'),
                  child: const Text('로그인')),
            ]))),
      ];
  List<Widget> consentView() => [
        heading('연결 전에 확인해 주세요'),
        const SizedBox(height: 12),
        panel(Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          const Text(privacyTitle,
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
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
          FilledButton(
              onPressed: busy || !consent ? null : () => act('acceptPrivacy'),
              child: const Text('동의하고 계속')),
        ])),
      ];
  Future<void> accountPopup() async {
    unawaited(act('refreshProfile'));
    await showDialog<void>(
        context: context,
        builder: (dialogContext) =>
            StatefulBuilder(builder: (context, setDialogState) {
              updateAccount = () {
                if (context.mounted) setDialogState(() {});
              };
              return Dialog(
                  insetPadding: const EdgeInsets.all(20),
                  child: ConstrainedBox(
                      constraints: BoxConstraints(
                          maxWidth: 390,
                          maxHeight: MediaQuery.sizeOf(context).height * .82),
                      child: SingleChildScrollView(
                          padding: const EdgeInsets.all(22),
                          child: Column(
                              mainAxisSize: MainAxisSize.min,
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                Row(children: [
                                  Expanded(child: heading('계정 정보')),
                                  IconButton(
                                      tooltip: '닫기',
                                      onPressed: () =>
                                          Navigator.pop(dialogContext),
                                      icon: const Icon(Icons.close))
                                ]),
                                const SizedBox(height: 12),
                                Row(children: [
                                  avatar(52),
                                  const SizedBox(width: 12),
                                  Expanded(
                                      child: Column(
                                          crossAxisAlignment:
                                              CrossAxisAlignment.start,
                                          children: [
                                        Text(state['username'] as String? ?? '',
                                            style: const TextStyle(
                                                fontSize: 20,
                                                fontWeight: FontWeight.w800)),
                                        Text(state['account'] as String? ?? '',
                                            style:
                                                const TextStyle(color: muted)),
                                      ]))
                                ]),
                                const SizedBox(height: 18),
                                Wrap(
                                    alignment: WrapAlignment.spaceBetween,
                                    spacing: 16,
                                    children: [
                                      const Text('이용 중인 플랜'),
                                      Text(state['plan'] as String? ?? '확인 중',
                                          style: const TextStyle(
                                              fontWeight: FontWeight.w700))
                                    ]),
                                const Divider(height: 30),
                                const Text('설정',
                                    style: TextStyle(
                                        color: muted,
                                        fontWeight: FontWeight.w700)),
                                SwitchListTile(
                                    contentPadding: EdgeInsets.zero,
                                    title: const Text('알람'),
                                    value: state['alarmsEnabled'] == true,
                                    onChanged: busy
                                        ? null
                                        : (v) =>
                                            act('setAlarms', {'enabled': v})),
                                SwitchListTile(
                                    contentPadding: EdgeInsets.zero,
                                    title: const Text('Live 2D 활성화'),
                                    value: state['liveEnabled'] == true,
                                    onChanged: busy
                                        ? null
                                        : (v) =>
                                            act('setLive', {'enabled': v})),
                                const Text('Live 2D는 최초 적용 시 시스템 확인 화면이 표시됩니다.',
                                    style:
                                        TextStyle(fontSize: 12, color: muted)),
                                if (error != null)
                                  Padding(
                                      padding: const EdgeInsets.only(top: 10),
                                      child: Text(error!,
                                          style: TextStyle(
                                              color: Theme.of(context)
                                                  .colorScheme
                                                  .error))),
                                const SizedBox(height: 18),
                                OutlinedButton(
                                    onPressed: busy
                                        ? null
                                        : () async {
                                            if (await act('logout') &&
                                                dialogContext.mounted) {
                                              Navigator.pop(dialogContext);
                                            }
                                          },
                                    child: const Text('로그아웃')),
                              ]))));
            }));
    updateAccount = null;
  }

  List<Widget> connectedView(bool tablet) {
    final displays = state['displays'] as List? ?? [];
    return [
      panel(Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        heading('연결된 화면'),
        const SizedBox(height: 20),
        if (displays.isEmpty)
          const Text('화면 정보를 확인하고 있어요.', style: TextStyle(color: muted)),
        for (final raw in displays)
          Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child:
                  Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Icon(
                    raw['canApply'] == true
                        ? (tablet
                            ? Icons.tablet_android_outlined
                            : Icons.phone_android_outlined)
                        : Icons.monitor_outlined,
                    size: 40,
                    color: const Color(0xff8f97ce)),
                const SizedBox(width: 16),
                Expanded(
                    child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                      Text(
                          raw['canApply'] == true
                              ? '내장 화면'
                              : raw['name'] as String? ?? '외부 화면',
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
                              ? [pill('잠금 화면'), pill('홈 화면')]
                              : [pill('정보 조회 전용')]),
                    ])),
              ])),
      ])),
      const SizedBox(height: 10),
      if (state['message'] != null)
        Text(state['message'] as String,
            style: const TextStyle(fontSize: 12, color: muted)),
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
                    child: Align(
                  alignment: Alignment.topCenter,
                  child: ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 680),
                      child: ListView(
                          controller: scroll,
                          padding: EdgeInsets.all(tablet ? 28 : 20),
                          children: [
                            Row(children: [
                              Image.asset('assets/app-icon.png',
                                  width: 52, height: 52),
                              const SizedBox(width: 12),
                              const Expanded(
                                  child: Column(
                                      crossAxisAlignment:
                                          CrossAxisAlignment.start,
                                      children: [
                                    Text('Upscale Lab',
                                        style: TextStyle(
                                            fontSize: 22,
                                            fontWeight: FontWeight.w800,
                                            color: ink)),
                                    Text('Connect',
                                        style: TextStyle(
                                            fontSize: 17, color: muted))
                                  ])),
                              if (loggedIn)
                                IconButton(
                                    tooltip: '계정 정보',
                                    onPressed: accountPopup,
                                    icon: avatar(42)),
                            ]),
                            SizedBox(height: loggedIn ? 14 : 30),
                            if (state.isEmpty && error == null)
                              const Text('연결 준비 중…',
                                  style: TextStyle(color: muted))
                            else if (!accepted)
                              ...consentView()
                            else if (!loggedIn)
                              ...loginView()
                            else
                              ...connectedView(tablet),
                            if (busy)
                              const Padding(
                                  padding: EdgeInsets.symmetric(vertical: 12),
                                  child: LinearProgressIndicator()),
                            if (error != null)
                              Semantics(
                                  liveRegion: true,
                                  child: Text(error!,
                                      style: TextStyle(
                                          color: Theme.of(context)
                                              .colorScheme
                                              .error))),
                          ])),
                )))));
  }
}
