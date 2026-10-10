import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'api_client.dart';

class AuthSessionStore {
  AuthSessionStore({FlutterSecureStorage? storage})
      : _storage = storage ?? const FlutterSecureStorage();

  static const _tokenKey = 'auto_login_access_token';
  static const _expiresAtKey = 'auto_login_expires_at';

  final FlutterSecureStorage _storage;

  Future<void> save(AuthSession session) async {
    await _storage.write(key: _tokenKey, value: session.accessToken);
    await _storage.write(
      key: _expiresAtKey,
      value: session.expiresAt.toUtc().toIso8601String(),
    );
  }

  Future<AuthSession?> read() async {
    final token = await _storage.read(key: _tokenKey);
    final expiresAtValue = await _storage.read(key: _expiresAtKey);
    final expiresAt = DateTime.tryParse(expiresAtValue ?? '');
    if (token == null || token.isEmpty || expiresAt == null) return null;
    if (!expiresAt.isAfter(DateTime.now().toUtc())) {
      await clear();
      return null;
    }
    return AuthSession(accessToken: token, expiresAt: expiresAt);
  }

  Future<void> clear() async {
    await _storage.delete(key: _tokenKey);
    await _storage.delete(key: _expiresAtKey);
  }
}
