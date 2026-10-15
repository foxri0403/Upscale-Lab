import 'dart:convert';

import 'package:http/http.dart' as http;

import '../models/gallery.dart';
import '../models/gallery_tag_codec.dart';
import '../models/live_layer_models.dart';

class ApiClient {
  ApiClient({required String baseUrl}) : _baseUrl = _normalize(baseUrl);

  String _baseUrl;
  String? _accessToken;
  AuthSession? _session;
  Future<void>? _refreshing;
  Future<void> Function(AuthSession)? onSessionChanged;

  String get baseUrl => _baseUrl;

  /// Stable subject identifier of the authenticated account.
  String get userId {
    final token = _accessToken;
    if (token == null) throw StateError('로그인이 필요합니다.');
    final parts = token.split('.');
    if (parts.length != 3) throw StateError('계정 정보를 확인하지 못했습니다.');
    final payload =
        jsonDecode(utf8.decode(base64Url.decode(base64Url.normalize(parts[1]))))
            as Map<String, dynamic>;
    final userId = payload['sub'];
    if (userId is! String || userId.isEmpty) {
      throw StateError('계정 정보를 확인하지 못했습니다.');
    }
    return userId;
  }

  /// Legacy app-private storage key; retained for one-time album migration.
  String get storageScope => '$baseUrl\n$userId';

  void restoreSession(AuthSession session) {
    _accessToken = session.accessToken;
    _session = session;
  }

  void updateBaseUrl(String value) {
    _baseUrl = _normalize(value);
    _accessToken = null;
    _session = null;
    onSessionChanged = null;
  }

  static String _normalize(String value) =>
      value.trim().replaceFirst(RegExp(r'/+$'), '');

  Map<String, String> get _headers => {
        'content-type': 'application/json',
        if (_accessToken != null) 'authorization': 'Bearer $_accessToken',
      };

  Future<AuthSession> login(String identifier, String password) async {
    final response = await _post(
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
    restoreSession(session);
    return session;
  }

  Future<void> validateSession() async {
    final response = await _get(
      Uri.parse('$baseUrl/api/auth/me'),
      headers: _headers,
    );
    _ensureSuccess(response);
  }

  void logout() {
    _accessToken = null;
    _session = null;
    onSessionChanged = null;
  }

  Future<void> revokeSession() async {
    final refreshToken = _session?.refreshToken;
    final server = baseUrl;
    logout();
    if (refreshToken == null) return;
    try {
      await http
          .post(Uri.parse('$server/api/auth/logout'),
              headers: {'content-type': 'application/json'},
              body: jsonEncode({'refreshToken': refreshToken}))
          .timeout(const Duration(seconds: 5));
    } catch (_) {/* Local logout remains available offline. */}
  }

  Future<void> _renew({bool force = false}) async {
    final current = _session;
    if (current?.refreshToken == null ||
        (!force &&
            current!.expiresAt.isAfter(
                DateTime.now().toUtc().add(const Duration(minutes: 1))))) {
      return;
    }
    if (_refreshing != null) return _refreshing!;
    final operation = _refresh(current!);
    _refreshing = operation;
    try {
      await operation;
    } finally {
      if (identical(_refreshing, operation)) _refreshing = null;
    }
  }

  Future<void> _refresh(AuthSession previous) async {
    final server = baseUrl;
    final response = await http
        .post(Uri.parse('$server/api/auth/refresh'),
            headers: {'content-type': 'application/json'},
            body: jsonEncode({'refreshToken': previous.refreshToken}))
        .timeout(const Duration(seconds: 15));
    _ensureSuccess(response);
    // A response from the previous login/server must never restore a logged-out session.
    if (!identical(_session, previous) || baseUrl != server) return;
    final renewed =
        AuthSession.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
    restoreSession(renewed);
    await onSessionChanged?.call(renewed);
  }

  Future<http.Response> _request(String method, Uri uri,
      {Map<String, String>? headers, Object? body}) async {
    final server = baseUrl;
    final authenticated =
        !uri.path.startsWith('/api/auth/') || uri.path == '/api/auth/me';
    if (authenticated) await _renew();
    Future<http.Response> send() {
      if (baseUrl != server) throw StateError('서버 주소가 변경되었습니다. 다시 시도해 주세요.');
      final request = http.Request(method, uri)..headers.addAll(_headers);
      if (body != null) request.body = body as String;
      return request.send().then(http.Response.fromStream);
    }

    var response = await send();
    if (authenticated &&
        response.statusCode == 401 &&
        _session?.refreshToken != null) {
      await _renew(force: true);
      response = await send();
    }
    return response;
  }

  Future<http.Response> _get(Uri uri, {Map<String, String>? headers}) =>
      _request('GET', uri, headers: headers);
  Future<http.Response> _post(Uri uri,
          {Map<String, String>? headers, Object? body}) =>
      _request('POST', uri, headers: headers, body: body);
  Future<http.Response> _patch(Uri uri,
          {Map<String, String>? headers, Object? body}) =>
      _request('PATCH', uri, headers: headers, body: body);
  Future<http.Response> _put(Uri uri,
          {Map<String, String>? headers, Object? body}) =>
      _request('PUT', uri, headers: headers, body: body);
  Future<http.Response> _delete(Uri uri, {Map<String, String>? headers}) =>
      _request('DELETE', uri, headers: headers);

  // 회원가입: 성공하면 Cognito가 이메일로 6자리 인증 코드를 전송한다.
  Future<void> register({
    required String email,
    required String username,
    required String password,
  }) async {
    final response = await _post(
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
    final response = await _post(
      Uri.parse('$baseUrl/api/auth/verify-email'),
      headers: _headers,
      body: jsonEncode({'email': email, 'code': code}),
    );
    _ensureSuccess(response);
    restoreSession(AuthSession.fromJson(
        jsonDecode(response.body) as Map<String, dynamic>));
  }

  // Cognito 이메일 인증 코드 재전송.
  Future<void> resendVerification(String email) async {
    final response = await _post(
      Uri.parse('$baseUrl/api/auth/resend-verification'),
      headers: _headers,
      body: jsonEncode({'email': email}),
    );
    _ensureSuccess(response);
  }

  // 이메일과 일치하는 인증 완료 계정의 사용자 아이디 전체를 반환한다.
  Future<String?> findId(String email) async {
    final response = await _post(
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
    final response = await _post(
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
    final response = await _post(
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
    final response = await _get(
      Uri.parse('$baseUrl/api/projects'),
      headers: _headers,
    );
    _ensureSuccess(response);
    return (jsonDecode(response.body) as List<dynamic>)
        .map((item) => LiveLayerProject.fromJson(item as Map<String, dynamic>))
        .toList();
  }

  Future<LiveLayerProject> getProject(String id) async {
    final response = await _get(
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
    await _renew();
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
    final response = await _post(
      Uri.parse('$baseUrl/api/projects/$projectId/process'),
      headers: _headers,
    );
    _ensureSuccess(response);
  }

  Future<String> shareProject({
    required String projectId,
    required String title,
    String? tag,
    List<String>? tags,
  }) async {
    final selected =
        GalleryTagCodec.normalize(tags ?? [tag ?? unspecifiedGalleryTag]);
    final response = await _post(
      Uri.parse('$baseUrl/api/gallery'),
      headers: _headers,
      body: jsonEncode({
        'projectId': projectId,
        'title': title,
        'description': GalleryTagCodec.sharingDescription(selected),
        'isPublic': true,
        'tag': selected.first,
        'tags': selected,
      }),
    );
    _ensureSuccess(response);
    final post =
        jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>;
    _confirmedGalleryTags(post, selected);
    return post['id'] as String;
  }

  Future<List<GalleryPostItem>> getGallery({
    String? search,
    String? tag,
    List<String>? tags,
  }) async {
    final parameters = <String, dynamic>{
      if (search != null && search.trim().isNotEmpty) 'search': search.trim(),
      if (tag != null && tag.isNotEmpty) 'tag': tag,
      if (tags != null && tags.isNotEmpty) 'tags': tags,
    };
    final response = await _get(
      Uri.parse('$baseUrl/api/gallery').replace(queryParameters: parameters),
      headers: _headers,
    );
    _ensureSuccess(response);
    return (jsonDecode(utf8.decode(response.bodyBytes)) as List<dynamic>)
        .map((item) => GalleryPostItem.fromJson(item as Map<String, dynamic>))
        .where((post) =>
            post.matches(search: search, selectedTag: tag, selectedTags: tags))
        .toList();
  }

  Future<String> updateGalleryTag({
    required String postId,
    required String tag,
  }) async {
    return (await updateGalleryTags(postId: postId, tags: [tag])).first;
  }

  Future<List<String>> updateGalleryTags({
    required String postId,
    required List<String> tags,
  }) async {
    final response = await _patch(
      Uri.parse('$baseUrl/api/gallery/$postId/tag'),
      headers: _headers,
      body: jsonEncode({'tag': tags.first, 'tags': tags}),
    );
    if (response.statusCode == 404 || response.statusCode == 405) {
      throw StateError('공유 태그를 수정할 수 없습니다. 서버 업데이트가 필요한지 관리자에게 문의해주세요.');
    }
    _ensureSuccess(response);
    final post =
        jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>;
    if (post['id'] != postId) {
      throw StateError('수정한 공유 게시물 정보를 확인하지 못했습니다.');
    }
    return _confirmedGalleryTags(post, tags);
  }

  List<String> _confirmedGalleryTags(
      Map<String, dynamic> post, List<String> requested) {
    final confirmed = GalleryTagCodec.fromPost(post);
    if (confirmed.toSet().length != requested.toSet().length ||
        !requested.every(confirmed.contains)) {
      throw StateError('공유 태그가 저장되지 않았습니다. 서버 업데이트가 필요한지 관리자에게 문의해주세요.');
    }
    return confirmed;
  }

  Future<List<String>> updateGalleryPost({
    required String postId,
    required String title,
    required List<String> tags,
  }) async {
    final response = await _patch(
      Uri.parse('$baseUrl/api/gallery/$postId'),
      headers: _headers,
      body: jsonEncode({'title': title, 'tags': tags}),
    );
    if (response.statusCode == 404 || response.statusCode == 405) {
      throw StateError('공유 이미지를 수정하려면 서버 업데이트가 필요합니다.');
    }
    _ensureSuccess(response);
    final post =
        jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>;
    if (post['id'] != postId || post['title'] != title) {
      throw StateError('공유 이미지 이름이 저장되지 않았습니다.');
    }
    return _confirmedGalleryTags(post, tags);
  }

  Future<String> getGalleryDownloadUrl(String postId) async {
    final response = await _post(
        Uri.parse('$baseUrl/api/gallery/$postId/download-url'),
        headers: _headers);
    _ensureSuccess(response);
    final body =
        jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>;
    return body['url'] as String;
  }

  Future<String> getGalleryImageUrl(String postId) async {
    final response = await _get(
      Uri.parse('$baseUrl/api/gallery/$postId/preview-url'),
      headers: _headers,
    );
    _ensureSuccess(response);
    final body =
        jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>;
    final url = body['url'] as String;
    final uri = Uri.parse(url);
    if (!uri.hasAuthority || (uri.scheme != 'https' && uri.scheme != 'http')) {
      throw StateError('이미지 주소를 불러오지 못했습니다.');
    }
    return url;
  }

  Future<void> deleteGalleryPost(String id) async {
    final response = await _delete(
      Uri.parse('$baseUrl/api/gallery/$id'),
      headers: _headers,
    );
    if (response.statusCode != 404) _ensureSuccess(response);
  }

  Future<void> deleteProject(String id) async {
    final response = await _delete(
      Uri.parse('$baseUrl/api/projects/$id'),
      headers: _headers,
    );
    if (response.statusCode != 404) _ensureSuccess(response);
  }

  Future<double> getSensorSensitivity() async {
    final response = await _get(
      Uri.parse('$baseUrl/api/settings'),
      headers: _headers,
    );
    _ensureSuccess(response);
    final settings = jsonDecode(response.body) as Map<String, dynamic>;
    return (settings['sensorSensitivity'] as num?)?.toDouble() ?? 1;
  }

  Future<void> updateSensorSensitivity(double sensitivity) async {
    final current = await _get(
      Uri.parse('$baseUrl/api/settings'),
      headers: _headers,
    );
    _ensureSuccess(current);
    final settings = jsonDecode(current.body) as Map<String, dynamic>;
    final response = await _put(
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
  const AuthSession(
      {required this.accessToken,
      required this.expiresAt,
      this.refreshToken,
      this.refreshExpiresAt});

  final String accessToken;
  final DateTime expiresAt;
  final String? refreshToken;
  final DateTime? refreshExpiresAt;

  Map<String, dynamic> toJson() => {
        'accessToken': accessToken,
        'expiresAt': expiresAt.toUtc().toIso8601String(),
        'refreshToken': refreshToken,
        'refreshExpiresAt': refreshExpiresAt?.toUtc().toIso8601String(),
      };

  factory AuthSession.fromJson(Map<String, dynamic> json) => AuthSession(
        accessToken: json['accessToken'] as String,
        expiresAt: DateTime.parse(json['expiresAt'] as String).toUtc(),
        refreshToken: json['refreshToken'] as String?,
        refreshExpiresAt:
            DateTime.tryParse(json['refreshExpiresAt'] as String? ?? '')
                ?.toUtc(),
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
