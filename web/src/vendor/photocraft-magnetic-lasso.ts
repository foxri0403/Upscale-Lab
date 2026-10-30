/*!
 * Adapted from PhotoCraft crates/algo/src/magnetic.rs at
 * 5f9b4391874aabc9deb2fcb905267613e4d93ec8 (MIT option).
 * Copyright (c) 2026 ArtCraft Team and the PhotoCraft contributors.
 * See /licenses/photocraft-mask-brush.txt.
 * Browser adaptation: bounded preview field, piecewise corridor searches and typed arrays.
 */
import { segmentDistance, type BrushPoint } from "./photocraft-mask-brush.ts";

type Point = BrushPoint;
const clamp = (x: number, a: number, b: number) => Math.max(a, Math.min(b, x));
const dirs = [[1, 0], [1, 1], [0, 1], [-1, 1], [-1, 0], [-1, -1], [0, -1], [1, -1]];

// RGB and alpha Sobel gradients, ridge detection and subpixel edge centres, following PhotoCraft.
export class MagneticLasso {
  width: number; height: number;
  private strength: Float32Array; private gx: Float32Array; private gy: Float32Array;
  private ridge: Uint8Array; private ox: Float32Array; private oy: Float32Array;
  constructor(width: number, height: number, rgba: Uint8ClampedArray) {
    if (!Number.isInteger(width) || !Number.isInteger(height) || width < 1 || height < 1 || width * height > 1_000_000 || rgba.length !== width * height * 4)
      throw new Error("올가미 이미지 크기가 올바르지 않습니다.");
    this.width = width; this.height = height;
    const n = width * height;
    this.strength = new Float32Array(n); this.gx = new Float32Array(n); this.gy = new Float32Array(n);
    this.ridge = new Uint8Array(n); this.ox = new Float32Array(n); this.oy = new Float32Array(n);
    const at = (x: number, y: number, c: number) => {
      const i = (clamp(y, 0, height - 1) * width + clamp(x, 0, width - 1)) * 4;
      const a = rgba[i + 3] / 255; return c === 3 ? a : rgba[i + c] / 255 * a;
    };
    for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
      let best = 0, bx = 0, by = 0;
      for (let c = 0; c < 4; c++) {
        const dx = at(x + 1, y - 1, c) + 2 * at(x + 1, y, c) + at(x + 1, y + 1, c) - at(x - 1, y - 1, c) - 2 * at(x - 1, y, c) - at(x - 1, y + 1, c);
        const dy = at(x - 1, y + 1, c) + 2 * at(x, y + 1, c) + at(x + 1, y + 1, c) - at(x - 1, y - 1, c) - 2 * at(x, y - 1, c) - at(x + 1, y - 1, c);
        if (dx * dx + dy * dy > best) { best = dx * dx + dy * dy; bx = dx; by = dy; }
      }
      const m = Math.sqrt(best), i = y * width + x;
      this.strength[i] = Math.min(1, m / 4);
      if (m > 1e-6) { this.gx[i] = bx / m; this.gy[i] = by / m; }
    }
    const strengthAt = (x: number, y: number) => this.strength[clamp(y, 0, height - 1) * width + clamp(x, 0, width - 1)];
    for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
      const i = y * width + x, s = this.strength[i], gx = this.gx[i], gy = this.gy[i];
      const dx = Math.abs(gx) >= .4142 * Math.abs(gy) ? Math.sign(gx) : 0;
      const dy = Math.abs(gy) >= .4142 * Math.abs(gx) ? Math.sign(gy) : 0;
      const sm = strengthAt(x - dx, y - dy), sp = strengthAt(x + dx, y + dy);
      if (s > 0 && s >= sm && s >= sp) {
        this.ridge[i] = 1; const den = sm - 2 * s + sp;
        const t = den < -1e-6 ? clamp(.5 * (sm - sp) / den, -.5, .5) : 0;
        this.ox[i] = t * dx; this.oy[i] = t * dy;
      }
    }
  }
  private point(p: Point) {
    if (!Number.isFinite(p.x) || !Number.isFinite(p.y)) throw new Error("올가미 좌표가 올바르지 않습니다.");
    return { x: clamp(p.x, 0, this.width - .001), y: clamp(p.y, 0, this.height - .001) };
  }
  private centre(i: number, contrast: number): Point {
    const edge = this.ridge[i] && this.strength[i] >= contrast;
    return { x: i % this.width + .5 + (edge ? this.ox[i] : 0), y: Math.floor(i / this.width) + .5 + (edge ? this.oy[i] : 0) };
  }
  snap(input: Point, radius = 10, contrast = .1): Point {
    const p = this.point(input); radius = clamp(radius, 1, 32); contrast = clamp(contrast, .01, 1);
    let best = 0, result = p;
    for (let y = Math.max(0, Math.floor(p.y - radius)); y <= Math.min(this.height - 1, p.y + radius); y++)
      for (let x = Math.max(0, Math.floor(p.x - radius)); x <= Math.min(this.width - 1, p.x + radius); x++) {
        const i = y * this.width + x, d = Math.hypot(x + .5 - p.x, y + .5 - p.y), s = this.strength[i];
        const score = s * (1 - .25 * d / radius);
        if (d <= radius && s >= contrast && this.ridge[i] && score > best) { best = score; result = this.centre(i, contrast); }
      }
    return result;
  }
  // Eight-connected Dijkstra search inside a corridor around a short pointer segment.
  trace(inputA: Point, inputB: Point, radius = 10, contrast = .1): Point[] {
    const a = this.point(inputA), b = this.point(inputB);
    radius = clamp(radius, 1, 32); contrast = clamp(contrast, .01, 1);
    if (Math.hypot(a.x - b.x, a.y - b.y) > 160) {
      const out: Point[] = [a], pieces = Math.ceil(Math.hypot(a.x - b.x, a.y - b.y) / 128);
      for (let k = 1; k <= pieces; k++) out.push(...this.trace(out.at(-1)!, k === pieces ? b : this.snap({ x: a.x + (b.x - a.x) * k / pieces, y: a.y + (b.y - a.y) * k / pieces }, radius, contrast), radius, contrast).slice(1));
      return out;
    }
    const rad = radius + 1.5, x0 = Math.max(0, Math.floor(Math.min(a.x, b.x) - rad)), y0 = Math.max(0, Math.floor(Math.min(a.y, b.y) - rad));
    const w = Math.min(this.width, Math.ceil(Math.max(a.x, b.x) + rad + 1)) - x0, h = Math.min(this.height, Math.ceil(Math.max(a.y, b.y) + rad + 1)) - y0;
    const n = w * h, costs = new Float64Array(n).fill(Infinity), from = new Int8Array(n).fill(-1), node = new Float32Array(n).fill(Infinity);
    const start = (Math.floor(a.y) - y0) * w + Math.floor(a.x) - x0, goal = (Math.floor(b.y) - y0) * w + Math.floor(b.x) - x0;
    let strongest = 0;
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      const i = y * w + x, j = (y + y0) * this.width + x + x0, d = segmentDistance({ x: x + x0 + .5, y: y + y0 + .5 }, a, b);
      if (d > rad && i !== start && i !== goal) continue;
      const s = this.strength[j]; strongest = Math.max(strongest, s);
      const good = clamp((s - contrast) / Math.max(contrast, .02), 0, 1) * (.6 + .4 * s);
      node[i] = .43 * (s >= contrast && this.ridge[j] ? 0 : 1) + .43 * (1 - good) + .02 + .4 * Math.min(1, d / rad);
    }
    if (strongest < contrast || start === goal) return [a, b];
    const heap: [number, number][] = [];
    const push = (v: [number, number]) => { let i = heap.length; heap.push(v); while (i > 0) { const p = (i - 1) >> 1; if (heap[p][0] <= v[0]) break; heap[i] = heap[p]; i = p; } heap[i] = v; };
    const pop = () => { const first = heap[0], last = heap.pop()!; if (heap.length) { let i = 0; while (i * 2 + 1 < heap.length) { let c = i * 2 + 1; if (c + 1 < heap.length && heap[c + 1][0] < heap[c][0]) c++; if (last[0] <= heap[c][0]) break; heap[i] = heap[c]; i = c; } heap[i] = last; } return first; };
    costs[start] = 0; push([0, start]);
    while (heap.length) {
      const [d, i] = pop(); if (d > costs[i]) continue; if (i === goal) break;
      const x = i % w, y = Math.floor(i / w), gi = (y + y0) * this.width + x + x0;
      for (let k = 0; k < dirs.length; k++) {
        const [dx, dy] = dirs[k], nx = x + dx, ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
        const j = ny * w + nx; if (!Number.isFinite(node[j])) continue;
        const gj = (ny + y0) * this.width + nx + x0, len = Math.hypot(dx, dy);
        const tangent = (g: number) => this.strength[g] < .5 * contrast ? 2 / 3 : Math.acos(clamp(Math.abs((this.gy[g] * dx - this.gx[g] * dy) / len), 0, 1)) * 2 / Math.PI;
        const nd = d + node[j] * len + .14 * (tangent(gi) + tangent(gj)) / 2;
        if (nd < costs[j]) { costs[j] = nd; from[j] = k; push([nd, j]); }
      }
    }
    if (!Number.isFinite(costs[goal])) return [a, b];
    const out: Point[] = []; let cur = goal;
    while (cur !== start && out.length <= n) {
      out.push(this.centre((Math.floor(cur / w) + y0) * this.width + cur % w + x0, contrast));
      const k = from[cur]; if (k < 0) return [a, b]; cur -= dirs[k][1] * w + dirs[k][0];
    }
    out.reverse(); return [a, ...out.slice(0, -1), b];
  }
}
