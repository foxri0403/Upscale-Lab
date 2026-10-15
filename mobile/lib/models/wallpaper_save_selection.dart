import '../services/wallpaper_service.dart';

class WallpaperSaveSelection {
  const WallpaperSaveSelection({this.home = false, this.lock = false});

  final bool home;
  final bool lock;

  WallpaperTarget? get target => home && lock
      ? WallpaperTarget.both
      : home
          ? WallpaperTarget.home
          : lock
              ? WallpaperTarget.lock
              : null;

  String get description => home && lock
      ? '홈/잠금 화면을 바꿉니다.'
      : home
          ? '홈화면의 배경화면을 바꿉니다.'
          : lock
              ? '잠금화면의 배경화면을 바꿉니다.'
              : '배경화면을 바꾸지 않고 이미지만 저장합니다.';
}
