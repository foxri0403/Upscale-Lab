import 'dart:convert';

import 'package:http/http.dart' as http;

import '../models/live_layer_models.dart';

class ApiClient {
  ApiClient({required String baseUrl}) : _baseUrl = _normalize(baseUrl);

  String _baseUrl;
  String? _accessToken;

  String get baseUrl => _baseUrl;

  void restoreSession(AuthSession session) {
    _accessToken = session.accessToken;
  }

  void updateBaseUrl(String value) {
    _baseUrl = _normalize(value);
    _accessToken = null;
  }

  static String _normalize(String value) =>
      value.trim().replaceFirst(RegExp(r'/+$'), '');

  Map<String, String> get _headers => {
        'content-type': 'application/json',
        if (_accessToken != null) 'authorization': 'Bearer $_accessToken',
      };

  Future<AuthSession> login(String identifier, String password) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/auth/login'),
      headers: _headers,
      // The API keeps the legacy "email" key for existing clients, but accepts
      // either an email address or username as its value.
      body: jsonEncode({'email': identifier, 'password': password}),
    );
    _ensureSuccess(response);
    final session = AuthSession.fromJson(
      jsonDecode(response.body) as Map<String, dynamic>,
    );
    _accessToken = session.accessToken;
    return session;
  }

  Future<void> validateSession() async {
    final response = await http.get(
      Uri.parse('$baseUrl/api/auth/me'),
      headers: _headers,
    );
    _ensureSuccess(response);
  }

  void logout() {
    _accessToken = null;
  }

  // 회원가입: 성공하면 Cognito가 이메일로 6자리 인증 코드를 전송한다.
  Future<void> register({
    required String email,
    required String username,
    required String password,
  }) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/auth/register'),
      headers: _headers,
      body: jsonEncode({
        'email': email,
        'username': username,
        'password': password,
      }),
    );
    _ensureSuccess(response);
  }

  // 이메일 인증: 성공 응답의 JWT를 저장해 바로 로그인 상태로 만든다.
  Future<void> verifyEmail({
    required String email,
    required String code,
  }) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/auth/verify-email'),
      headers: _headers,
      body: jsonEncode({'email': email, 'code': code}),
    );
    _ensureSuccess(response);
    _accessToken = (jsonDecode(response.body)
        as Map<String, dynamic>)['accessToken'] as String;
  }

  // Cognito 이메일 인증 코드 재전송.
  Future<void> resendVerification(String email) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/auth/resend-verification'),
      headers: _headers,
      body: jsonEncode({'email': email}),
    );
    _ensureSuccess(response);
  }

  // 이메일과 일치하는 인증 완료 계정의 사용자 아이디 전체를 반환한다.
  Future<String?> findId(String email) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/auth/find-id'),
      headers: _headers,
      body: jsonEncode({'email': email}),
    );
    _ensureSuccess(response);
    final body = jsonDecode(response.body) as Map<String, dynamic>;
    return body['username'] as String?;
  }

  // 계정 존재 여부와 관계없이 서버는 동일한 응답을 반환한다.
  Future<void> startPasswordReset(String email) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/auth/forgot-password'),
      headers: _headers,
      body: jsonEncode({'email': email}),
    );
    _ensureSuccess(response);
  }

  Future<void> confirmPasswordReset({
    required String email,
    required String code,
    required String newPassword,
  }) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/auth/reset-password'),
      headers: _headers,
      body: jsonEncode({
        'email': email,
        'code': code,
        'newPassword': newPassword,
      }),
    );
    _ensureSuccess(response);
  }

  Future<List<LiveLayerProject>> getProjects() async {
    final response = await http.get(
      Uri.parse('$baseUrl/api/projects'),
      headers: _headers,
    );
    _ensureSuccess(response);
    return (jsonDecode(response.body) as List<dynamic>)
        .map((item) => LiveLayerProject.fromJson(item as Map<String, dynamic>))
        .toList();
  }

  Future<LiveLayerProject> getProject(String id) async {
    final response = await http.get(
      Uri.parse('$baseUrl/api/projects/$id'),
      headers: _headers,
    );
    _ensureSuccess(response);
    return LiveLayerProject.fromJson(
      jsonDecode(response.body) as Map<String, dynamic>,
    );
  }

  Future<LiveLayerProject> createProject({
    required String title,
    required String filePath,
    required int width,
    required int height,
  }) async {
    final request =
        http.MultipartRequest('POST', Uri.parse('$baseUrl/api/projects'))
          ..headers.addAll({
            if (_accessToken != null) 'authorization': 'Bearer $_accessToken',
          })
          ..fields.addAll({
            'title': title,
            'originalWidth': '$width',
            'originalHeight': '$height',
          })
          ..files.add(await http.MultipartFile.fromPath('file', filePath));
    final streamed = await request.send();
    final response = await http.Response.fromStream(streamed);
    _ensureSuccess(response);
    return LiveLayerProject.fromJson(
      jsonDecode(response.body) as Map<String, dynamic>,
    );
  }

  Future<void> startProcessing(String projectId) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/projects/$projectId/process'),
      headers: _headers,
    );
    _ensureSuccess(response);
  }

  Future<String> shareProject({
    required String projectId,
    required String title,
  }) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/gallery'),
      headers: _headers,
      body: jsonEncode({
        'projectId': projectId,
        'title': title,
        'description': 'Upscale Lab 모바일 앱에서 공유했습니다.',
        'isPublic': true,
      }),
    );
    _ensureSuccess(response);
    return (jsonDecode(response.body) as Map<String, dynamic>)['id'] as String;
  }

  Future<void> deleteGalleryPost(String id) async {
    final response = await http.delete(
      Uri.parse('$baseUrl/api/gallery/$id'),
      headers: _headers,
    );
    if (response.statusCode != 404) _ensureSuccess(response);
  }

  Future<void> deleteProject(String id) async {
    final response = await http.delete(
      Uri.parse('$baseUrl/api/projects/$id'),
      headers: _headers,
    );
    if (response.statusCode != 404) _ensureSuccess(response);
  }

  Future<double> getSensorSensitivity() async {
    final response = await http.get(
      Uri.parse('$baseUrl/api/settings'),
      headers: _headers,
    );
    _ensureSuccess(response);
    final settings = jsonDecode(response.body) as Map<String, dynamic>;
    return (settings['sensorSensitivity'] as num?)?.toDouble() ?? 1;
  }

  Future<void> updateSensorSensitivity(double sensitivity) async {
    final current = await http.get(
      Uri.parse('$baseUrl/api/settings'),
      headers: _headers,
    );
    _ensureSuccess(current);
    final settings = jsonDecode(current.body) as Map<String, dynamic>;
    final response = await http.put(
      Uri.parse('$baseUrl/api/settings'),
      headers: _headers,
      body: jsonEncode({
        'syncEnabled': settings['syncEnabled'] as bool? ?? true,
        'dynamicOrientation': settings['dynamicOrientation'] as bool? ?? true,
        'autoUpscale': settings['autoUpscale'] as bool? ?? true,
        'sensorSensitivity': sensitivity,
      }),
    );
    _ensureSuccess(response);
  }

  void _ensureSuccess(http.Response response) {
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw ApiException(response.statusCode, response.body);
    }
  }
}

class AuthSession {
  const AuthSession({required this.accessToken, required this.expiresAt});

  final String accessToken;
  final DateTime expiresAt;

  factory AuthSession.fromJson(Map<String, dynamic> json) => AuthSession(
        accessToken: json['accessToken'] as String,
        expiresAt: DateTime.parse(json['expiresAt'] as String).toUtc(),
      );
}

class ApiException implements Exception {
  const ApiException(this.statusCode, this.body);

  final int statusCode;
  final String body;

  Map<String, dynamic>? get _problem {
    try {
      final decoded = jsonDecode(body);
      return decoded is Map<String, dynamic> ? decoded : null;
    } catch (_) {
      return null;
    }
  }

  Map<String, String> get fieldErrors {
    final errors = _problem?['errors'];
    if (errors is! Map<String, dynamic>) return const {};

    return errors.map((key, value) {
      if (value is List && value.isNotEmpty) {
        return MapEntry(key, '${value.first}');
      }
      return MapEntry(key, '$value');
    });
  }

  String get message {
    final problem = _problem;
    final detail = problem?['detail'];
    if (detail is String && detail.trim().isNotEmpty) return detail;
    if (fieldErrors.isNotEmpty) return fieldErrors.values.first;
    return '요청을 처리하지 못했습니다. ($statusCode)';
  }

  @override
  String toString() => message;
}
