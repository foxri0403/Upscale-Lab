import type { Picture } from "./model";
import { PictureView } from "./ui";
import { frameLayout, wallpaperCrop, type ScreenSize, type WallpaperFrame } from "./wallpaper-frame";

export function FramedWallpaper({ picture, size, frame }: { picture: Picture; size: ScreenSize; frame: WallpaperFrame }) {
  const crop = wallpaperCrop(picture, size);
  const layout = frameLayout(frame, crop);
  const character = !picture.background && (picture.foregrounds?.length ?? 0) >= 2;
  return <div className={`wallpaper-framed-image ${character ? "wallpaper-character-frame" : ""}`} aria-label={picture.name}>
    <div className="wallpaper-crop-plane" style={{
      left: `${100 * layout.left / layout.width}%`, top: `${100 * layout.top / layout.height}%`,
      width: `${100 * layout.drawWidth / layout.width}%`, height: `${100 * layout.drawHeight / layout.height}%`,
    }}>
      <div className="wallpaper-source-plane" style={{
        left: `${-100 * crop.x / crop.width}%`, top: `${-100 * crop.y / crop.height}%`,
        width: `${100 * size.width / crop.width}%`, height: `${100 * size.height / crop.height}%`,
      }}>
        <PictureView picture={picture} />
      </div>
    </div>
  </div>;
}
