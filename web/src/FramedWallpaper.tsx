import type { HeadRig, Picture } from "./model";
import { PictureView } from "./ui";
import { frameLayout, wallpaperCrop, type ScreenSize, type WallpaperFrame } from "./wallpaper-frame";
import { rigPart } from "./character-rig";
import { useCharacterRig } from "./use-character-rig";
import type { ParallaxMotion } from "./use-parallax";

export function FramedWallpaper({ picture, size, frame, motion, rig }: {
  picture: Picture; size: ScreenSize; frame: WallpaperFrame; motion?: ParallaxMotion; rig?: HeadRig;
}) {
  const crop = wallpaperCrop(picture, size);
  const layout = frameLayout(frame, crop);
  const character = !picture.background && (picture.foregrounds?.length ?? 0) >= 2;
  const characterRig = useCharacterRig(character && !!motion?.active, rig, picture.id, motion?.position);
  const partForSource = characterRig.active ? (source: string) => {
    const layer = picture.layers?.find(value => value.imageUrl === source);
    const file = picture.wallpaperParts?.find(value => value.source === source);
    return rigPart(layer?.layerType ?? file?.role ?? "", layer?.id ?? file?.layerId ?? undefined, rig?.partAssignments);
  } : undefined;
  return <div className={`wallpaper-framed-image ${character ? "wallpaper-character-frame" : ""}`} ref={characterRig.stage} aria-label={picture.name}>
    <div className="wallpaper-crop-plane" ref={motion?.layers} style={{
      left: `${100 * layout.left / layout.width}%`, top: `${100 * layout.top / layout.height}%`,
      width: `${100 * layout.drawWidth / layout.width}%`, height: `${100 * layout.drawHeight / layout.height}%`,
    }}>
      <div className={`wallpaper-source-plane${motion?.active ? " wallpaper-motion-active" : ""}`}
        ref={characterRig.layers} style={{
        left: `${-100 * crop.x / crop.width}%`, top: `${-100 * crop.y / crop.height}%`,
        width: `${100 * size.width / crop.width}%`, height: `${100 * size.height / crop.height}%`,
      }}>
        <PictureView picture={picture} partForSource={partForSource} />
      </div>
    </div>
  </div>;
}
