import type { MaskPoint, MaskStroke } from "./subject-mask";

/** Hard selection guides use native canvas paths while drawing, without reading pixels. */
export function paintSelectionGuide(context: CanvasRenderingContext2D, stroke: MaskStroke,
  scaleX: number, scaleY: number, from = 0) {
  const points = stroke.points;
  if (!points.length || from >= points.length) return;
  context.save(); context.scale(scaleX, scaleY);
  context.fillStyle = context.strokeStyle = stroke.mode === "erase" ? "black" : "white";
  context.lineWidth = stroke.size; context.lineCap = context.lineJoin = "round";
  const first = points[Math.max(0, from - 1)];
  context.beginPath(); context.moveTo(first.x, first.y);
  for (let i = Math.max(1, from); i < points.length; i++) context.lineTo(points[i].x, points[i].y);
  context.stroke();
  // A stationary click also paints, including on browsers that omit zero-length paths.
  if (!from) { context.beginPath(); context.arc(first.x, first.y, stroke.size / 2, 0, Math.PI * 2); context.fill(); }
  context.restore();
}

export function fillSelectionGuide(context: CanvasRenderingContext2D, points: MaskPoint[],
  mode: MaskStroke["mode"], scaleX: number, scaleY: number) {
  context.beginPath(); points.forEach((point, i) => i
    ? context.lineTo(point.x * scaleX, point.y * scaleY) : context.moveTo(point.x * scaleX, point.y * scaleY));
  context.closePath(); context.fillStyle = mode === "erase" ? "black" : "white"; context.fill("evenodd");
}
