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

bool _meetsPasswordPolicy(String password) {
  return password.length >= 10 &&
      password.length <= 128 &&
      RegExp('[A-Z]').hasMatch(password) &&
      RegExp(r'\d').hasMatch(password) &&
      RegExp(r'[^A-Za-z0-9\s]').hasMatch(password);
}

bool _isValidEmail(String email) =>
    RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$').hasMatch(email);

class LiveLayerApp extends StatelessWidget {
  const LiveLayerApp({super.key, required this.apiClient});

  final ApiClient apiClient;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: 'Upscale Lab',
      theme: ThemeData(
        scaffoldBackgroundColor: Colors.white,
        colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF536DFE)),
      ),
      home: LoginPage(apiClient: apiClient),
    );
  }
}

// 공통 입력창. 기존에 확정한 흰색/미니멀 UI를 그대로 사용한다.
class LiveLayerTextField extends StatelessWidget {
  const LiveLayerTextField({
    super.key,
    required this.controller,
    required this.hintText,
    required this.icon,
    this.obscureText = false,
    this.keyboardType,
    this.errorText,
    this.suffixIcon,
    this.onChanged,
  });

  final TextEditingController controller;
  final String hintText;
  final IconData icon;
  final bool obscureText;
  final TextInputType? keyboardType;
  final String? errorText;
  final Widget? suffixIcon;
  final ValueChanged<String>? onChanged;

  @override
  Widget build(BuildContext context) {
    return TextField(
      controller: controller,
      obscureText: obscureText,
      keyboardType: keyboardType,
      onChanged: onChanged,
      decoration: InputDecoration(
        hintText: hintText,
        errorText: errorText,
        hintStyle: const TextStyle(color: Color(0xFF9299AA), fontSize: 15),
        prefixIcon: Icon(icon, color: const Color(0xFF6F7789)),
        suffixIcon: suffixIcon,
        filled: true,
        fillColor: const Color(0xFFFAFAFC),
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 18,
          vertical: 18,
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: Color(0xFFE3E5EB)),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: Color(0xFF536DFE), width: 1.5),
        ),
        errorBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: Color(0xFFD93025), width: 1.5),
        ),
        focusedErrorBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: Color(0xFFD93025), width: 1.5),
        ),
      ),
    );
  }
}

class LiveLayerButton extends StatelessWidget {
  const LiveLayerButton({
    super.key,
    required this.text,
    required this.onPressed,
  });

  final String text;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: double.infinity,
      height: 54,
      child: ElevatedButton(
        onPressed: onPressed,
        style: ElevatedButton.styleFrom(
          elevation: 0,
          backgroundColor: const Color(0xFF536DFE),
          foregroundColor: Colors.white,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(14),
          ),
        ),
        child: Text(
          text,
          style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
        ),
      ),
    );
  }
}

// 로그인 UI는 이전에 확정한 디자인을 유지하고 실제 /api/auth/login만 연결한다.
class LoginPage extends StatefulWidget {
  const LoginPage({super.key, required this.apiClient});

  final ApiClient apiClient;

  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  final _identifier = TextEditingController();
  final _password = TextEditingController();
  bool _busy = false;
  bool _showPassword = false;

  Future<void> _login() async {
    if (_identifier.text.trim().isEmpty || _password.text.isEmpty) {
      _message('이메일 또는 사용자 아이디와 비밀번호를 입력해주세요.');
      return;
    }

    setState(() => _busy = true);
    try {
      await widget.apiClient.login(_identifier.text.trim(), _password.text);
      if (!mounted) return;
      await Navigator.of(context).pushReplacement(
        MaterialPageRoute(
          builder: (_) => ProjectListPage(apiClient: widget.apiClient),
        ),
      );
    } catch (error) {
      if (mounted) _message('로그인에 실패했습니다.\n$error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _message(String message) {
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  void dispose() {
    _identifier.dispose();
    _password.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 28),
          child: Column(
            children: [
              const Spacer(flex: 3),
              const Text(
                'Upscale Lab',
                style: TextStyle(
                  fontSize: 38,
                  fontWeight: FontWeight.bold,
                  letterSpacing: -1,
                ),
              ),
              const SizedBox(height: 14),
              const Text(
                '당신의 이미지를,\n움직이는 배경화면으로',
                textAlign: TextAlign.center,
                style: TextStyle(
                  fontSize: 16,
                  height: 1.5,
                  color: Color(0xFF6F7789),
                ),
              ),
              const SizedBox(height: 55),
              LiveLayerTextField(
                controller: _identifier,
                hintText: '이메일 또는 사용자 아이디',
                icon: Icons.person_outline,
              ),
              const SizedBox(height: 14),
              LiveLayerTextField(
                controller: _password,
                hintText: '비밀번호',
                icon: Icons.lock_outline,
                obscureText: !_showPassword,
                suffixIcon: IconButton(
                  tooltip: _showPassword ? '비밀번호 숨기기' : '비밀번호 보기',
                  onPressed: () =>
                      setState(() => _showPassword = !_showPassword),
                  icon: Icon(
                    _showPassword
                        ? Icons.visibility_off_outlined
                        : Icons.visibility_outlined,
                    color: const Color(0xFF6F7789),
                  ),
                ),
              ),
              const SizedBox(height: 22),
              LiveLayerButton(
                text: _busy ? '로그인 중...' : '로그인',
                onPressed: _busy ? null : _login,
              ),
              const SizedBox(height: 8),
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  TextButton(
                    onPressed: _busy
                        ? null
                        : () => Navigator.of(context).push(
                              MaterialPageRoute(
                                builder: (_) =>
                                    FindIdPage(apiClient: widget.apiClient),
                              ),
                            ),
                    child: const Text('아이디 찾기'),
                  ),
                  const Text('|', style: TextStyle(color: Color(0xFFD0D3DB))),
                  TextButton(
                    onPressed: _busy
                        ? null
                        : () => Navigator.of(context).push(
                              MaterialPageRoute(
                                builder: (_) => PasswordResetPage(
                                  apiClient: widget.apiClient,
                                ),
                              ),
                            ),
                    child: const Text('비밀번호 재설정'),
                  ),
                ],
              ),
              const SizedBox(height: 2),
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  const Text(
                    '계정이 없으신가요?',
                    style: TextStyle(color: Color(0xFF777E8E)),
                  ),
                  TextButton(
                    onPressed: _busy
                        ? null
                        : () => Navigator.of(context).push(
                              MaterialPageRoute(
                                builder: (_) =>
                                    SignUpPage(apiClient: widget.apiClient),
                              ),
                            ),
                    child: const Text(
                      '회원가입하기',
                      style: TextStyle(
                        color: Color(0xFF536DFE),
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                ],
              ),
              const Spacer(flex: 4),
            ],
          ),
        ),
      ),
    );
  }
}

class FindIdPage extends StatefulWidget {
  const FindIdPage({super.key, required this.apiClient});

  final ApiClient apiClient;

  @override
  State<FindIdPage> createState() => _FindIdPageState();
}

class _FindIdPageState extends State<FindIdPage> {
  final _email = TextEditingController();
  bool _busy = false;
  String? _result;

  Future<void> _findId() async {
    final email = _email.text.trim();
    if (!_isValidEmail(email)) {
      _message('올바른 이메일 주소를 입력해주세요.');
      return;
    }

    setState(() {
      _busy = true;
      _result = null;
    });
    try {
      final username = await widget.apiClient.findId(email);
      if (!mounted) return;
      setState(() {
        _result = username == null
            ? '일치하는 인증 완료 계정을 찾지 못했습니다.'
            : '등록된 사용자 아이디는 $username 입니다.';
      });
    } catch (error) {
      if (mounted) _message('아이디를 찾지 못했습니다.\n$error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _message(String message) {
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  void dispose() {
    _email.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        backgroundColor: Colors.white,
        elevation: 0,
        title: const Text('아이디 찾기'),
      ),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.symmetric(horizontal: 28, vertical: 36),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                '가입할 때 사용한 이메일을 입력하면 사용자 아이디를 알려드려요.',
                style: TextStyle(
                  fontSize: 15,
                  height: 1.5,
                  color: Color(0xFF6F7789),
                ),
              ),
              const SizedBox(height: 30),
              LiveLayerTextField(
                controller: _email,
                hintText: '이메일 주소',
                icon: Icons.mail_outline,
                keyboardType: TextInputType.emailAddress,
              ),
              const SizedBox(height: 20),
              LiveLayerButton(
                text: _busy ? '확인 중...' : '아이디 찾기',
                onPressed: _busy ? null : _findId,
              ),
              if (_result != null) ...[
                const SizedBox(height: 24),
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.all(18),
                  decoration: BoxDecoration(
                    color: const Color(0xFFF3F5FF),
                    borderRadius: BorderRadius.circular(14),
                  ),
                  child: Text(
                    _result!,
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      color: Color(0xFF434A5B),
                      height: 1.5,
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class PasswordResetPage extends StatefulWidget {
  const PasswordResetPage({super.key, required this.apiClient});

  final ApiClient apiClient;

  @override
  State<PasswordResetPage> createState() => _PasswordResetPageState();
}

class _PasswordResetPageState extends State<PasswordResetPage> {
  final _email = TextEditingController();
  final _code = TextEditingController();
  final _newPassword = TextEditingController();
  final _passwordCheck = TextEditingController();
  bool _codeSent = false;
  bool _busy = false;
  bool _showNewPassword = false;
  bool _showPasswordCheck = false;

  Future<void> _sendCode() async {
    final email = _email.text.trim();
    if (email.isEmpty || !email.contains('@')) {
      _message('올바른 이메일 주소를 입력해주세요.');
      return;
    }

    setState(() => _busy = true);
    try {
      await widget.apiClient.startPasswordReset(email);
      if (!mounted) return;
      setState(() => _codeSent = true);
      _message('일치하는 계정이 있다면 이메일로 재설정 코드를 보냈습니다.');
    } catch (error) {
      if (mounted) _message('재설정 코드를 요청하지 못했습니다.\n$error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _resetPassword() async {
    final code = _code.text.trim();
    final password = _newPassword.text;
    if (!RegExp(r'^\d{6}$').hasMatch(code)) {
      _message('이메일로 받은 6자리 코드를 입력해주세요.');
      return;
    }
    if (!_meetsPasswordPolicy(password)) {
      _message('비밀번호는 10자 이상이며 대문자, 숫자, 특수문자를 포함해야 합니다.');
      return;
    }
    if (password != _passwordCheck.text) {
      _message('새 비밀번호가 일치하지 않습니다.');
      return;
    }

    setState(() => _busy = true);
    try {
      await widget.apiClient.confirmPasswordReset(
        email: _email.text.trim(),
        code: code,
        newPassword: password,
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('비밀번호를 변경했습니다. 새 비밀번호로 로그인해주세요.')),
      );
      Navigator.pop(context);
    } catch (error) {
      if (mounted) _message('비밀번호를 변경하지 못했습니다.\n$error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _message(String message) {
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  void dispose() {
    _email.dispose();
    _code.dispose();
    _newPassword.dispose();
    _passwordCheck.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        backgroundColor: Colors.white,
        elevation: 0,
        title: const Text('비밀번호 재설정'),
      ),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.symmetric(horizontal: 28, vertical: 30),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                '가입한 이메일로 6자리 재설정 코드를 보내드려요.',
                style: TextStyle(
                  fontSize: 15,
                  height: 1.5,
                  color: Color(0xFF6F7789),
                ),
              ),
              const SizedBox(height: 26),
              LiveLayerTextField(
                controller: _email,
                hintText: '이메일 주소',
                icon: Icons.mail_outline,
                keyboardType: TextInputType.emailAddress,
              ),
              const SizedBox(height: 16),
              LiveLayerButton(
                text:
                    _busy ? '요청 중...' : (_codeSent ? '코드 다시 보내기' : '재설정 코드 받기'),
                onPressed: _busy ? null : _sendCode,
              ),
              if (_codeSent) ...[
                const SizedBox(height: 30),
                LiveLayerTextField(
                  controller: _code,
                  hintText: '6자리 재설정 코드',
                  icon: Icons.mark_email_read_outlined,
                  keyboardType: TextInputType.number,
                ),
                const SizedBox(height: 14),
                LiveLayerTextField(
                  controller: _newPassword,
                  hintText: '새 비밀번호',
                  icon: Icons.lock_reset_outlined,
                  obscureText: !_showNewPassword,
                  suffixIcon: IconButton(
                    tooltip: _showNewPassword ? '비밀번호 숨기기' : '비밀번호 보기',
                    onPressed: () =>
                        setState(() => _showNewPassword = !_showNewPassword),
                    icon: Icon(
                      _showNewPassword
                          ? Icons.visibility_off_outlined
                          : Icons.visibility_outlined,
                      color: const Color(0xFF6F7789),
                    ),
                  ),
                ),
                const SizedBox(height: 8),
                const Padding(
                  padding: EdgeInsets.only(left: 5),
                  child: Text(
                    '10자 이상 · 대문자 · 숫자 · 특수문자를 포함해주세요.',
                    style: TextStyle(fontSize: 12, color: Color(0xFF9299AA)),
                  ),
                ),
                const SizedBox(height: 14),
                LiveLayerTextField(
                  controller: _passwordCheck,
                  hintText: '새 비밀번호 확인',
                  icon: Icons.lock_outline,
                  obscureText: !_showPasswordCheck,
                  suffixIcon: IconButton(
                    tooltip: _showPasswordCheck ? '비밀번호 숨기기' : '비밀번호 보기',
                    onPressed: () => setState(
                        () => _showPasswordCheck = !_showPasswordCheck),
                    icon: Icon(
                      _showPasswordCheck
                          ? Icons.visibility_off_outlined
                          : Icons.visibility_outlined,
                      color: const Color(0xFF6F7789),
                    ),
                  ),
                ),
                const SizedBox(height: 22),
                LiveLayerButton(
                  text: _busy ? '변경 중...' : '비밀번호 변경',
                  onPressed: _busy ? null : _resetPassword,
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

// 실제 register 계약에 username이 필요하므로 기존 UI에 사용자 이름 입력칸만 추가한다.
class SignUpPage extends StatefulWidget {
  const SignUpPage({super.key, required this.apiClient});

  final ApiClient apiClient;

  @override
  State<SignUpPage> createState() => _SignUpPageState();
}

class _SignUpPageState extends State<SignUpPage> {
  final _email = TextEditingController();
  final _username = TextEditingController();
  final _password = TextEditingController();
  final _passwordCheck = TextEditingController();
  bool _busy = false;
  bool _showPassword = false;
  bool _showPasswordCheck = false;
  String? _emailError;
  String? _usernameError;
  String? _passwordError;
  String? _passwordCheckError;
  String? _submitError;

  bool _validateForm() {
    final email = _email.text.trim();
    final username = _username.text.trim();
    final password = _password.text;
    final passwordCheck = _passwordCheck.text;

    setState(() {
      _emailError = email.isEmpty
          ? '이메일을 입력해주세요.'
          : (!_isValidEmail(email) ? '올바른 이메일 형식이 아닙니다.' : null);
      _usernameError = username.isEmpty
          ? '사용자 아이디를 입력해주세요.'
          : (username.length < 2 || username.length > 50
              ? '사용자 아이디는 2~50자로 입력해주세요.'
              : null);
      _passwordError = password.isEmpty
          ? '비밀번호를 입력해주세요.'
          : (!_meetsPasswordPolicy(password)
              ? '10~128자이며 대문자, 숫자, 특수문자를 포함해야 합니다.'
              : null);
      _passwordCheckError = passwordCheck.isEmpty
          ? '비밀번호 확인을 입력해주세요.'
          : (password != passwordCheck ? '비밀번호가 일치하지 않습니다.' : null);
      _submitError = null;
    });

    final valid = _emailError == null &&
        _usernameError == null &&
        _passwordError == null &&
        _passwordCheckError == null;
    if (!valid) {
      setState(() => _submitError = '붉게 표시된 입력 내용을 확인해주세요.');
    }
    return valid;
  }

  void _applyRegistrationError(Object error) {
    var message = '회원가입 요청을 처리하지 못했습니다.';
    String? emailError;
    String? usernameError;
    String? passwordError;

    if (error is ApiException) {
      message = error.message;
      emailError = error.fieldErrors['Email'];
      usernameError = error.fieldErrors['Username'];
      passwordError = error.fieldErrors['Password'];

      if (message.contains('이메일') && message.contains('사용 중')) {
        emailError = '이미 사용 중인 이메일입니다.';
      }
      if ((message.contains('사용자 이름') || message.contains('사용자 아이디')) &&
          message.contains('사용 중')) {
        usernameError = '이미 사용 중인 사용자 아이디입니다.';
      }
      if (message.contains('비밀번호') && passwordError == null) {
        passwordError = message;
      }
    }

    setState(() {
      _emailError = emailError;
      _usernameError = usernameError;
      _passwordError = passwordError;
      _submitError = message;
    });
  }

  void _clearFieldError(String field) {
    setState(() {
      switch (field) {
        case 'email':
          _emailError = null;
          break;
        case 'username':
          _usernameError = null;
          break;
        case 'password':
          _passwordError = null;
          break;
        case 'passwordCheck':
          _passwordCheckError = null;
          break;
      }
      _submitError = null;
    });
  }

  Future<void> _signUp() async {
    final email = _email.text.trim();
    final username = _username.text.trim();
    final password = _password.text;

    if (!_validateForm()) return;

    setState(() => _busy = true);
    try {
      await widget.apiClient.register(
        email: email,
        username: username,
        password: password,
      );
      if (!mounted) return;
      await Navigator.of(context).push(
        MaterialPageRoute(
          builder: (_) =>
              EmailVerificationPage(apiClient: widget.apiClient, email: email),
        ),
      );
    } catch (error) {
      if (mounted) _applyRegistrationError(error);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  void dispose() {
    _email.dispose();
    _username.dispose();
    _password.dispose();
    _passwordCheck.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        backgroundColor: Colors.white,
        elevation: 0,
        leading: IconButton(
          icon: const Icon(Icons.arrow_back_ios_new, color: Colors.black),
          onPressed: () => Navigator.pop(context),
        ),
      ),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.symmetric(horizontal: 28),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const SizedBox(height: 25),
              const Text(
                'Upscale Lab',
                style: TextStyle(
                  fontSize: 36,
                  fontWeight: FontWeight.bold,
                  letterSpacing: -1,
                ),
              ),
              const SizedBox(height: 12),
              const Text(
                '계정을 생성하고\n나만의 Live2D 배경화면을 만들어보세요.',
                style: TextStyle(
                  fontSize: 15,
                  height: 1.5,
                  color: Color(0xFF6F7789),
                ),
              ),
              const SizedBox(height: 40),
              LiveLayerTextField(
                controller: _email,
                hintText: '이메일 주소',
                icon: Icons.mail_outline,
                keyboardType: TextInputType.emailAddress,
                errorText: _emailError,
                onChanged: (_) => _clearFieldError('email'),
              ),
              const SizedBox(height: 14),
              LiveLayerTextField(
                controller: _username,
                hintText: '사용자 아이디',
                icon: Icons.person_outline,
                errorText: _usernameError,
                onChanged: (_) => _clearFieldError('username'),
              ),
              const SizedBox(height: 14),
              LiveLayerTextField(
                controller: _password,
                hintText: '비밀번호',
                icon: Icons.lock_outline,
                obscureText: !_showPassword,
                errorText: _passwordError,
                onChanged: (_) => _clearFieldError('password'),
                suffixIcon: IconButton(
                  tooltip: _showPassword ? '비밀번호 숨기기' : '비밀번호 보기',
                  onPressed: () =>
                      setState(() => _showPassword = !_showPassword),
                  icon: Icon(
                    _showPassword
                        ? Icons.visibility_off_outlined
                        : Icons.visibility_outlined,
                    color: const Color(0xFF6F7789),
                  ),
                ),
              ),
              const SizedBox(height: 8),
              const Padding(
                padding: EdgeInsets.only(left: 5),
                child: Text(
                  '10자 이상 · 대문자 · 숫자 · 특수문자를 포함해주세요.',
                  style: TextStyle(fontSize: 12, color: Color(0xFF9299AA)),
                ),
              ),
              const SizedBox(height: 14),
              LiveLayerTextField(
                controller: _passwordCheck,
                hintText: '비밀번호 확인',
                icon: Icons.lock_outline,
                obscureText: !_showPasswordCheck,
                errorText: _passwordCheckError,
                onChanged: (_) => _clearFieldError('passwordCheck'),
                suffixIcon: IconButton(
                  tooltip: _showPasswordCheck ? '비밀번호 숨기기' : '비밀번호 보기',
                  onPressed: () =>
                      setState(() => _showPasswordCheck = !_showPasswordCheck),
                  icon: Icon(
                    _showPasswordCheck
                        ? Icons.visibility_off_outlined
                        : Icons.visibility_outlined,
                    color: const Color(0xFF6F7789),
                  ),
                ),
              ),
              const SizedBox(height: 24),
              LiveLayerButton(
                text: _busy ? '회원가입 중...' : '회원가입하기',
                onPressed: _busy ? null : _signUp,
              ),
              if (_submitError != null) ...[
                const SizedBox(height: 10),
                SizedBox(
                  width: double.infinity,
                  child: Text(
                    _submitError!,
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      color: Color(0xFFD93025),
                      fontSize: 13,
                      height: 1.4,
                    ),
                  ),
                ),
              ],
              const SizedBox(height: 25),
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  const Text(
                    '이미 계정이 있으신가요?',
                    style: TextStyle(color: Color(0xFF777E8E)),
                  ),
                  TextButton(
                    onPressed: _busy ? null : () => Navigator.pop(context),
                    child: const Text(
                      '로그인하기',
                      style: TextStyle(
                        color: Color(0xFF536DFE),
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class EmailVerificationPage extends StatefulWidget {
  const EmailVerificationPage({
    super.key,
    required this.apiClient,
    required this.email,
  });

  final ApiClient apiClient;
  final String email;

  @override
  State<EmailVerificationPage> createState() => _EmailVerificationPageState();
}

class _EmailVerificationPageState extends State<EmailVerificationPage> {
  final _controllers = List.generate(6, (_) => TextEditingController());
  final _focusNodes = List.generate(6, (_) => FocusNode());
  bool _busy = false;

  String get _code => _controllers.map((controller) => controller.text).join();

  Future<void> _verify() async {
    if (_code.length != 6) {
      _message('6자리 인증번호를 입력해주세요.');
      return;
    }

    setState(() => _busy = true);
    try {
      await widget.apiClient.verifyEmail(email: widget.email, code: _code);
      if (!mounted) return;

      // verify-email 성공 응답에 JWT가 있으므로 바로 홈으로 이동한다.
      Navigator.of(context).pushAndRemoveUntil(
        MaterialPageRoute(
          builder: (_) => ProjectListPage(apiClient: widget.apiClient),
        ),
        (_) => false,
      );
    } catch (error) {
      if (mounted) _message('인증에 실패했습니다.\n$error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _resend() async {
    try {
      await widget.apiClient.resendVerification(widget.email);
      if (mounted) _message('인증번호를 다시 전송했습니다.');
    } catch (error) {
      if (mounted) _message('재전송에 실패했습니다.\n$error');
    }
  }

  void _message(String message) {
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  void dispose() {
    for (final controller in _controllers) {
      controller.dispose();
    }
    for (final focusNode in _focusNodes) {
      focusNode.dispose();
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        backgroundColor: Colors.white,
        elevation: 0,
        leading: IconButton(
          icon: const Icon(Icons.arrow_back_ios_new, color: Colors.black),
          onPressed: () => Navigator.pop(context),
        ),
      ),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 28),
          child: Column(
            children: [
              const SizedBox(height: 60),
              const Icon(
                Icons.mail_outline_rounded,
                size: 75,
                color: Color(0xFF7C8497),
              ),
              const SizedBox(height: 25),
              const Text(
                '이메일 인증',
                style: TextStyle(fontSize: 28, fontWeight: FontWeight.bold),
              ),
              const SizedBox(height: 14),
              const Text(
                '입력하신 이메일로 전송된\n인증 코드를 입력해주세요.',
                textAlign: TextAlign.center,
                style: TextStyle(
                  fontSize: 15,
                  height: 1.5,
                  color: Color(0xFF747B8C),
                ),
              ),
              const SizedBox(height: 12),
              Text(
                widget.email,
                style: const TextStyle(color: Color(0xFF536DFE)),
              ),
              const SizedBox(height: 35),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: List.generate(6, (index) {
                  return SizedBox(
                    width: 45,
                    height: 58,
                    child: TextField(
                      controller: _controllers[index],
                      focusNode: _focusNodes[index],
                      textAlign: TextAlign.center,
                      keyboardType: TextInputType.number,
                      inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                      maxLength: 1,
                      style: const TextStyle(
                        fontSize: 22,
                        fontWeight: FontWeight.w600,
                      ),
                      onChanged: (value) {
                        if (value.isNotEmpty && index < 5) {
                          _focusNodes[index + 1].requestFocus();
                        } else if (value.isEmpty && index > 0) {
                          _focusNodes[index - 1].requestFocus();
                        }
                      },
                      decoration: InputDecoration(
                        counterText: '',
                        enabledBorder: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(12),
                          borderSide: const BorderSide(
                            color: Color(0xFFDCE0E9),
                          ),
                        ),
                        focusedBorder: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(12),
                          borderSide: const BorderSide(
                            color: Color(0xFF536DFE),
                            width: 1.5,
                          ),
                        ),
                      ),
                    ),
                  );
                }),
              ),
              const SizedBox(height: 32),
              LiveLayerButton(
                text: _busy ? '인증 중...' : '인증하기',
                onPressed: _busy ? null : _verify,
              ),
              const SizedBox(height: 18),
              TextButton(
                onPressed: _busy ? null : _resend,
                child: const Text(
                  '인증 코드를 받지 못하셨나요?  다시 보내기',
                  style: TextStyle(color: Color(0xFF536DFE)),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

// 기존 ProjectListPage의 실제 getProjects/createProject/open 기능은 유지하고
// 화면만 확정한 2열 홈 UI로 바꾼다.
class ProjectListPage extends StatefulWidget {
  const ProjectListPage({super.key, required this.apiClient});

  final ApiClient apiClient;

  @override
  State<ProjectListPage> createState() => _ProjectListPageState();
}

class _ProjectListPageState extends State<ProjectListPage> {
  late Future<List<LiveLayerProject>> _projects =
      widget.apiClient.getProjects();
  bool _creating = false;
  int _selectedIndex = 0;

  void _reload() => setState(() => _projects = widget.apiClient.getProjects());

  void _logout() {
    widget.apiClient.logout();
    Navigator.of(context).pushAndRemoveUntil(
      MaterialPageRoute(
        builder: (_) => LoginPage(apiClient: widget.apiClient),
      ),
      (_) => false,
    );
  }

  Widget _settings() {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 22),
      child: Align(
        alignment: Alignment.topCenter,
        child: OutlinedButton.icon(
          onPressed: _logout,
          icon: const Icon(Icons.logout),
          label: const Text('로그아웃'),
        ),
      ),
    );
  }

  Future<void> _createProject() async {
    final picked = await ImagePicker().pickImage(source: ImageSource.gallery);
    if (picked == null) return;

    setState(() => _creating = true);
    try {
      final bytes = await picked.readAsBytes();
      final codec = await ui.instantiateImageCodec(bytes);
      final frame = await codec.getNextFrame();
      final width = frame.image.width;
      final height = frame.image.height;
      frame.image.dispose();
      codec.dispose();
      final project = await widget.apiClient.createProject(
        title: picked.name,
        filePath: picked.path,
        width: width,
        height: height,
      );
      await widget.apiClient.startProcessing(project.id);
      _reload();
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('$error')));
      }
    } finally {
      if (mounted) setState(() => _creating = false);
    }
  }

  Future<void> _open(LiveLayerProject summary) async {
    try {
      final project = await widget.apiClient.getProject(summary.id);
      if (!mounted) return;
      await Navigator.of(context).push(
        MaterialPageRoute(
          builder: (_) =>
              PreviewPage(project: project, apiClient: widget.apiClient),
        ),
      );
      _reload();
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('$error')));
      }
    }
  }

  Widget _home() {
    return FutureBuilder<List<LiveLayerProject>>(
      future: _projects,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: CircularProgressIndicator());
        }
        if (snapshot.hasError) {
          return Center(child: Text('${snapshot.error}'));
        }

        final projects = snapshot.data ?? const <LiveLayerProject>[];

        // itemCount에 +1을 하여 첫 번째 칸은 항상 "새 작품 만들기"로 사용한다.
        // 신규 계정이면 projects가 비어 있으므로 이 카드 하나만 표시된다.
        return RefreshIndicator(
          onRefresh: () async => _reload(),
          child: GridView.builder(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.fromLTRB(22, 0, 22, 24),
            gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
              crossAxisCount: 2,
              crossAxisSpacing: 16,
              mainAxisSpacing: 22,
              childAspectRatio: 0.73,
            ),
            itemCount: projects.length + 1,
            itemBuilder: (context, index) {
              if (index == 0) {
                return NewProjectCard(
                  creating: _creating,
                  onTap: _creating ? null : _createProject,
                );
              }
              final project = projects[index - 1];
              return ProjectCard(project: project, onTap: () => _open(project));
            },
          ),
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    Widget content;
    switch (_selectedIndex) {
      case 1:
        content = const Center(child: Text('탐색 화면은 다음 단계에서 연결합니다.'));
        break;
      case 2:
        content = _settings();
        break;
      default:
        content = _home();
    }

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
        backgroundColor: Colors.white,
        indicatorColor: const Color(0xFFE9EDFF),
        onDestinationSelected: (index) =>
            setState(() => _selectedIndex = index),
        destinations: const [
          NavigationDestination(
            icon: Icon(Icons.home_outlined),
            selectedIcon: Icon(Icons.home, color: Color(0xFF536DFE)),
            label: '홈',
          ),
          NavigationDestination(
            icon: Icon(Icons.search),
            selectedIcon: Icon(Icons.search, color: Color(0xFF536DFE)),
            label: '탐색',
          ),
          NavigationDestination(
            icon: Icon(Icons.settings_outlined),
            selectedIcon: Icon(Icons.settings, color: Color(0xFF536DFE)),
            label: '설정',
          ),
        ],
      ),
    );
  }
}

class NewProjectCard extends StatelessWidget {
  const NewProjectCard({
    super.key,
    required this.creating,
    required this.onTap,
  });

  final bool creating;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
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
            Text(
              creating ? '업로드 중...' : '이미지 업로드',
              style: const TextStyle(
                fontSize: 15,
                fontWeight: FontWeight.w500,
                color: Color(0xFF58647D),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class ProjectCard extends StatelessWidget {
  const ProjectCard({super.key, required this.project, required this.onTap});

  final LiveLayerProject project;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      borderRadius: BorderRadius.circular(16),
      onTap: onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: ClipRRect(
              borderRadius: BorderRadius.circular(16),
              child: Container(
                width: double.infinity,
                color: const Color(0xFFF3F4F7),
                child: Image.network(
                  project.originalImageUrl,
                  fit: BoxFit.cover,
                  errorBuilder: (_, __, ___) => const Center(
                    child: Icon(
                      Icons.image_outlined,
                      size: 42,
                      color: Color(0xFF9299AA),
                    ),
                  ),
                ),
              ),
            ),
          ),
          const SizedBox(height: 10),
          Text(
            project.title,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600),
          ),
          const SizedBox(height: 3),
          Text(
            project.status,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontSize: 12, color: Color(0xFF9299AA)),
          ),
        ],
      ),
    );
  }
}

class PreviewPage extends StatefulWidget {
  const PreviewPage({
    super.key,
    required this.project,
    required this.apiClient,
  });

  final LiveLayerProject project;
  final ApiClient apiClient;

  @override
  State<PreviewPage> createState() => _PreviewPageState();
}

class _PreviewPageState extends State<PreviewPage> {
  static const _wallpaperChannel = MethodChannel('live_layer/wallpaper');
  double _sensitivity = 1;
  bool _downloading = false;
  bool _applyingStatic = false;
  bool _applyingLive = false;

  @override
  void initState() {
    super.initState();
    widget.apiClient.getSensorSensitivity().then((value) {
      if (mounted) setState(() => _sensitivity = value.clamp(0, 2).toDouble());
    });
  }

  Future<void> _downloadOriginal() async {
    setState(() => _downloading = true);
    try {
      // Project image URLs are short-lived S3 URLs. Refresh immediately before
      // handing the URL to Android so a preview left open does not download an
      // expired link.
      final project = await widget.apiClient.getProject(widget.project.id);
      await _wallpaperChannel.invokeMethod<int>('downloadImage', {
        'url': project.originalImageUrl,
        'fileName': project.title,
      });
      _message('다운로드를 시작했습니다. 완료되면 알림으로 알려드려요.');
    } on PlatformException catch (error) {
      _message(error.message ?? '$error');
    } catch (error) {
      _message('다운로드 URL을 가져오지 못했습니다.\n$error');
    } finally {
      if (mounted) setState(() => _downloading = false);
    }
  }

  Future<void> _applyStaticWallpaper() async {
    setState(() => _applyingStatic = true);
    try {
      final project = await widget.apiClient.getProject(widget.project.id);
      await _wallpaperChannel.invokeMethod<void>('applyStaticWallpaper', {
        'url': project.originalImageUrl,
      });
      _message('원본 이미지를 홈 화면 배경화면으로 적용했습니다.');
    } on PlatformException catch (error) {
      _message(error.message ?? '$error');
    } catch (error) {
      _message('배경화면 URL을 가져오지 못했습니다.\n$error');
    } finally {
      if (mounted) setState(() => _applyingStatic = false);
    }
  }

  Future<void> _applyLiveWallpaper() async {
    setState(() => _applyingLive = true);
    try {
      final project = await widget.apiClient.getProject(widget.project.id);
      if (project.layers.isEmpty) {
        _message('레이어 처리가 완료된 뒤 라이브 배경화면을 적용할 수 있습니다.');
        return;
      }
      await _wallpaperChannel.invokeMethod<void>('prepareLiveWallpaper', {
        'projectId': project.id,
        'title': project.title,
        'originalUrl': project.originalImageUrl,
        'sensitivity': _sensitivity,
        'layers':
            project.layers.map((layer) => layer.toWallpaperJson()).toList(),
      });
    } on PlatformException catch (error) {
      _message(error.message ?? '$error');
    } catch (error) {
      _message('라이브 배경화면 파일을 가져오지 못했습니다.\n$error');
    } finally {
      if (mounted) setState(() => _applyingLive = false);
    }
  }

  void _message(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.project.title),
      ),
      body: Column(
        children: [
          Expanded(
            child: widget.project.layers.isEmpty
                ? Center(
                    child: Text('처리 상태: ${widget.project.status}'),
                  )
                : AspectRatio(
                    aspectRatio: 9 / 16,
                    child: ParallaxPreview(
                      layers: widget.project.layers,
                      sensitivity: _sensitivity,
                    ),
                  ),
          ),
          ListTile(
            title: const Text('센서 반응 감도'),
            subtitle: Slider(
              value: _sensitivity,
              min: 0,
              max: 2,
              divisions: 20,
              onChanged: (value) => setState(() => _sensitivity = value),
              onChangeEnd: widget.apiClient.updateSensorSensitivity,
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 10),
            child: SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                onPressed: _applyingLive ? null : _applyLiveWallpaper,
                icon: _applyingLive
                    ? const SizedBox.square(
                        dimension: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.wallpaper),
                label: Text(
                  _applyingLive ? '레이어 다운로드 중...' : '라이브 배경화면 적용',
                ),
              ),
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 18),
            child: Row(
              children: [
                Expanded(
                  child: OutlinedButton.icon(
                    onPressed: _downloading ? null : _downloadOriginal,
                    icon: const Icon(Icons.download_outlined),
                    label: Text(_downloading ? '준비 중...' : '원본 다운로드'),
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: OutlinedButton.icon(
                    onPressed: _applyingStatic ? null : _applyStaticWallpaper,
                    icon: const Icon(Icons.phone_android),
                    label: Text(_applyingStatic ? '적용 중...' : '원본 바로 적용'),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
