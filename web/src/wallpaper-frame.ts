import type { Display, Picture } from "./model.ts";
import { pixelCrop } from "./crop-geometry.ts";

export type ScreenSize = { width: number; height: number };
export type WallpaperFrame = ScreenSize & { contain: boolean; zoom: number; x: number; y: number };

export function displaySize(display: Display): ScreenSize | null {
  const match = display.resolution.match(/^(\d+)\s*[×x]\s*(\d+)$/i);
  const width = display.width ?? Number(match?.[1]);
  const height = display.height ?? Number(match?.[2]);
  return Number.isInteger(width) && Number.isInteger(height) && width > 0 && height > 0 &&
    width <= 32768 && height <= 32768 ? { width, height } : null;
}

export function wallpaperCrop(picture: Picture, size: ScreenSize) {
  return pixelCrop(size.width, size.height, picture.edit ?? { crop: "original" });
}

export function frameLayout(frame: WallpaperFrame, source: ScreenSize) {
  const reduction = Math.min(1, 4096 / Math.max(frame.width, frame.height));
  const width = Math.max(1, Math.round(frame.width * reduction));
  const height = Math.max(1, Math.round(frame.height * reduction));
  const scale = (frame.contain ? Math.min : Math.max)(width / source.width, height / source.height) * frame.zoom;
  const drawWidth = source.width * scale;
  const drawHeight = source.height * scale;
  return { width, height, drawWidth, drawHeight, travelX: width - drawWidth, travelY: height - drawHeight,
    left: (width - drawWidth) * frame.x, top: (height - drawHeight) * frame.y };
}

export function defaultFrame(screen: ScreenSize, source: ScreenSize): WallpaperFrame {
  const ratio = (source.width / source.height) / (screen.width / screen.height);
  return { ...screen, contain: ratio > 1.6 || ratio < 1 / 1.6, zoom: 1, x: 0.5, y: 0.5 };
}

export function dragFrame(frame: WallpaperFrame, source: ScreenSize, dx: number, dy: number): WallpaperFrame {
  const { width, height, travelX, travelY } = frameLayout(frame, source);
  const shift = (value: number, distance: number, travel: number) => Math.abs(travel) < 1
    ? value : Math.min(1, Math.max(0, value + distance / travel));
  return { ...frame, x: shift(frame.x, dx * width, travelX), y: shift(frame.y, dy * height, travelY) };
}
