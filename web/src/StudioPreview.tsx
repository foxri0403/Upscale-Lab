import type { CSSProperties } from "react";

// Both previews fit inside the same ruler inset and leave room for crop handles.
// Container units track layout changes immediately, without a resize/scrollbar loop.
export function previewStageStyle(width: number, height: number, zoom: string, padding = 22): CSSProperties {
  const scale = Number(zoom) / 100;
  return {
    width: zoom === "fit"
      ? `max(1px, min(calc(100cqw - ${padding}px), calc((100cqh - ${padding}px) * ${width / height})))`
      : width * scale,
    height: zoom === "fit" ? "auto" : height * scale,
    aspectRatio: `${width} / ${height}`,
  };
}

export type PreviewPalette = { dark: boolean; toggle: () => void };

export function PreviewPaletteButton({ dark, toggle }: PreviewPalette) {
  return <button type="button" className="studio-palette-toggle" aria-label="미리보기 배경 전환"
    aria-pressed={dark} title={dark ? "밝은 배경으로 전환" : "짙은 회색 배경으로 전환"} onClick={toggle} />;
}
