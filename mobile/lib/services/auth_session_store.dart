import 'dart:convert';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'api_client.dart';

class AuthSessionStore {
  AuthSessionStore({FlutterSecureStorage? storage})
      : _storage = storage ?? const FlutterSecureStorage();

  static const _tokenKey = 'auto_login_access_token';
  static const _sessionKey = 'auto_login_session_v2';
  static const _expiresAtKey = 'auto_login_expires_at';

  final FlutterSecureStorage _storage;
  Future<void> _writes = Future.value();

  Future<void> _write(Future<void> Function() action) {
    final operation = _writes.then((_) => action());
    _writes = operation.catchError((Object _) {});
    return operation;
  }

  Future<void> save(AuthSession session) => _write(() async {
        await _storage.write(
            key: _sessionKey, value: jsonEncode(session.toJson()));
        await _storage.delete(key: _tokenKey);
        await _storage.delete(key: _expiresAtKey);
      });

  Future<AuthSession?> read() async {
    await _writes;
    final saved = await _storage.read(key: _sessionKey);
    if (saved != null) {
      // Expired access tokens are renewed by the API; retain credentials offline.
      return AuthSession.fromJson(jsonDecode(saved) as Map<String, dynamic>);
    }
    final token = await _storage.read(key: _tokenKey);
    final expiresAtValue = await _storage.read(key: _expiresAtKey);
    final expiresAt = DateTime.tryParse(expiresAtValue ?? '');
    if (token == null || token.isEmpty || expiresAt == null) return null;
    return AuthSession(accessToken: token, expiresAt: expiresAt);
  }

  Future<void> clear() => _write(() async {
        await _storage.delete(key: _sessionKey);
        await _storage.delete(key: _tokenKey);
        await _storage.delete(key: _expiresAtKey);
      });
}
