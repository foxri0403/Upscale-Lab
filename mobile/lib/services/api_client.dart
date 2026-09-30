import 'dart:convert';

import 'package:http/http.dart' as http;

import '../models/live_layer_models.dart';

class ApiClient {
  ApiClient({required this.baseUrl});

  final String baseUrl;
  String? _accessToken;

  Map<String, String> get _headers => {
        'content-type': 'application/json',
        if (_accessToken != null) 'authorization': 'Bearer $_accessToken',
      };

  Future<void> login(String email, String password) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/auth/login'),
      headers: _headers,
      body: jsonEncode({'email': email, 'password': password}),
    );
    _ensureSuccess(response);
    _accessToken = (jsonDecode(response.body) as Map<String, dynamic>)['accessToken'] as String;
  }

  Future<List<LiveLayerProject>> getProjects() async {
    final response = await http.get(Uri.parse('$baseUrl/api/projects'), headers: _headers);
    _ensureSuccess(response);
    return (jsonDecode(response.body) as List<dynamic>)
        .map((item) => LiveLayerProject.fromJson(item as Map<String, dynamic>))
        .toList();
  }

  Future<LiveLayerProject> getProject(String id) async {
    final response = await http.get(Uri.parse('$baseUrl/api/projects/$id'), headers: _headers);
    _ensureSuccess(response);
    return LiveLayerProject.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
  }

  Future<LiveLayerProject> createProject({
    required String title,
    required String filePath,
    required int width,
    required int height,
  }) async {
    final request = http.MultipartRequest('POST', Uri.parse('$baseUrl/api/projects'))
      ..headers.addAll({if (_accessToken != null) 'authorization': 'Bearer $_accessToken'})
      ..fields.addAll({
        'title': title,
        'originalWidth': '$width',
        'originalHeight': '$height',
      })
      ..files.add(await http.MultipartFile.fromPath('file', filePath));
    final streamed = await request.send();
    final response = await http.Response.fromStream(streamed);
    _ensureSuccess(response);
    return LiveLayerProject.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
  }

  Future<void> startProcessing(String projectId) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/projects/$projectId/process'),
      headers: _headers,
    );
    _ensureSuccess(response);
  }

  Future<double> getSensorSensitivity() async {
    final response = await http.get(Uri.parse('$baseUrl/api/settings'), headers: _headers);
    _ensureSuccess(response);
    final settings = jsonDecode(response.body) as Map<String, dynamic>;
    return (settings['sensorSensitivity'] as num?)?.toDouble() ?? 1;
  }

  Future<void> updateSensorSensitivity(double sensitivity) async {
    final current = await http.get(Uri.parse('$baseUrl/api/settings'), headers: _headers);
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

class ApiException implements Exception {
  const ApiException(this.statusCode, this.body);

  final int statusCode;
  final String body;

  @override
  String toString() => 'API request failed ($statusCode): $body';
}
