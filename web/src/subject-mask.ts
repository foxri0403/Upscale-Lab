import { PhotoCraftBrushCoverage, combineCoverage, type CoverageTile } from "./vendor/photocraft-mask-brush.ts";

export type MaskPoint = { x: number; y: number };
export type MaskStroke = { mode: "restore" | "erase"; size: number; hardness?: number; points: MaskPoint[] };

export function maskPoint(clientX: number, clientY: number,
  bounds: { left: number; top: number; width: number; height: number }, width: number, height: number): MaskPoint {
  return {
    x: Math.max(0, Math.min(width, (clientX - bounds.left) / bounds.width * width)),
    y: Math.max(0, Math.min(height, (clientY - bounds.top) / bounds.height * height)),
  };
}

export function paintMaskStroke(context: CanvasRenderingContext2D, stroke: MaskStroke) {
  const painter = new MaskStrokePainter(context, stroke);
  for (const point of stroke.points) painter.append(point);
}

export class MaskStrokePainter {
  private brush: PhotoCraftBrushCoverage;
  private baselines = new Map<CoverageTile, Uint8Array>();
  private context: CanvasRenderingContext2D;
  private stroke: Pick<MaskStroke, "size" | "hardness" | "mode">;
  private scaleX: number;
  private scaleY: number;
  constructor(context: CanvasRenderingContext2D, stroke: Pick<MaskStroke, "size" | "hardness" | "mode">,
    scaleX = 1, scaleY = 1) {
    this.context = context; this.stroke = stroke; this.scaleX = scaleX; this.scaleY = scaleY;
    this.brush = new PhotoCraftBrushCoverage(context.canvas.width, context.canvas.height,
      stroke.size * (scaleX + scaleY) / 2, stroke.hardness ?? 100);
  }
  append(point: MaskPoint) {
    const target = { x: point.x * this.scaleX, y: point.y * this.scaleY };
    for (const tile of this.brush.append(target)) {
      const image = this.context.getImageData(tile.x, tile.y, tile.width, tile.height);
      let baseline = this.baselines.get(tile);
      if (!baseline) {
        baseline = new Uint8Array(tile.width * tile.height);
        for (let i = 0; i < baseline.length; i++) baseline[i] = image.data[i * 4];
        this.baselines.set(tile, baseline);
      }
      for (let i = 0; i < baseline.length; i++) {
        const gray = combineCoverage(baseline[i], tile.coverage[i], this.stroke.mode);
        image.data[i * 4] = image.data[i * 4 + 1] = image.data[i * 4 + 2] = gray;
        image.data[i * 4 + 3] = 255;
      }
      this.context.putImageData(image, tile.x, tile.y);
    }
  }
}

export function canvasPng(canvas: HTMLCanvasElement): Promise<Blob> {
  return new Promise((resolve, reject) => canvas.toBlob(blob => blob ? resolve(blob)
    : reject(new Error("피사체 마스크를 만들지 못했습니다.")), "image/png"));
}
