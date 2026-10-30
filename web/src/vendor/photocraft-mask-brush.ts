/*!
 * Adapted from PhotoCraft's matting/brush.rs at
 * https://github.com/storytold/photocraft/tree/5f9b4391874aabc9deb2fcb905267613e4d93ec8
 * Copyright (c) 2026 ArtCraft Team and the PhotoCraft contributors.
 * SPDX-License-Identifier: MIT
 * Full license and adaptation notes: /licenses/photocraft-mask-brush.txt
 *
 * TypeScript adaptation: bounded sparse coverage tiles, incremental stroke input.
 * This module contains brush geometry only; it performs no image inference or I/O.
 */

export type BrushPoint = { x: number; y: number };
export type CoverageTile = { x: number; y: number; width: number; height: number; coverage: Uint8Array };
const TILE_SIZE = 256;
const MAX_SWEPT_AREA = 2 ** 28;
const MAX_POINTS = 100_000;
const clamp01 = (value: number) => Math.max(0, Math.min(1, value));

export function segmentDistance(point: BrushPoint, a: BrushPoint, b: BrushPoint) {
  const dx = b.x - a.x, dy = b.y - a.y;
  const lengthSquared = dx * dx + dy * dy;
  const t = lengthSquared > 0 ? clamp01(((point.x - a.x) * dx + (point.y - a.y) * dy) / lengthSquared) : 0;
  return Math.hypot(point.x - (a.x + t * dx), point.y - (a.y + t * dy));
}

/** PhotoCraft's solid core and smoothstep falloff, including the antialiased rim. */
export function softCoverage(distance: number, radius: number, core: number) {
  const ramp = radius + .5 - core;
  const t = clamp01((radius + .5 - distance) / Math.max(ramp, 1));
  return ramp > 1 ? t * t * (3 - 2 * t) : t;
}

/** One stroke keeps maximum coverage across its segments, independent of event frequency. */
export class PhotoCraftBrushCoverage {
  readonly tiles = new Map<number, CoverageTile>();
  private previous: BrushPoint | null = null;
  private pointCount = 0;
  private area: number;
  private radius: number;
  private core: number;
  private width: number;
  private height: number;
  private size: number;

  constructor(width: number, height: number, size: number, hardness = 100) {
    if (!Number.isInteger(width) || !Number.isInteger(height) || width < 1 || height < 1 ||
      width > 32768 || height > 32768 || width * height > 64_000_000 ||
      !Number.isFinite(size) || size <= 0 || size > 5000 || !Number.isFinite(hardness))
      throw new Error("브러시 크기와 이미지 크기를 확인해 주세요.");
    this.width = width; this.height = height; this.size = size;
    // Subpixel sizes are needed when replaying source-pixel strokes in a reduced preview.
    this.radius = size / 2;
    this.core = this.radius * Math.max(0, Math.min(100, hardness)) / 100;
    this.area = size * size;
  }

  append(point: BrushPoint): CoverageTile[] {
    if (!Number.isFinite(point.x) || !Number.isFinite(point.y) || point.x < 0 || point.x > this.width || point.y < 0 || point.y > this.height)
      throw new Error("브러시 위치가 이미지 영역을 벗어났습니다.");
    const a = this.previous || point;
    const distance = Math.hypot(point.x - a.x, point.y - a.y);
    if (this.pointCount >= MAX_POINTS || this.area + distance * this.size > MAX_SWEPT_AREA)
      throw new Error("한 번의 브러시 작업이 너무 깁니다. 짧게 나누어 수정해 주세요.");
    this.pointCount++; this.area += distance * this.size;
    const changed = new Set<number>();
    // Splitting long diagonals bounds the scanned area to the actual swept brush.
    const pieces = Math.max(1, Math.ceil(distance / Math.max(this.radius, 2)));
    let start = a;
    for (let part = 1; part <= pieces; part++) {
      const end = { x: a.x + (point.x - a.x) * part / pieces, y: a.y + (point.y - a.y) * part / pieces };
      this.paintSegment(start, end, changed); start = end;
    }
    this.previous = { ...point };
    return [...changed].map(key => this.tiles.get(key)!);
  }

  private paintSegment(a: BrushPoint, b: BrushPoint, changed: Set<number>) {
    const x0 = Math.max(0, Math.floor(Math.min(a.x, b.x) - this.radius - 1));
    const y0 = Math.max(0, Math.floor(Math.min(a.y, b.y) - this.radius - 1));
    const x1 = Math.min(this.width, Math.ceil(Math.max(a.x, b.x) + this.radius + 1));
    const y1 = Math.min(this.height, Math.ceil(Math.max(a.y, b.y) + this.radius + 1));
    const columns = Math.ceil(this.width / TILE_SIZE);
    for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) {
      const coverage = Math.round(softCoverage(segmentDistance({ x: x + .5, y: y + .5 }, a, b), this.radius, this.core) * 255);
      if (!coverage) continue;
      const tileX = Math.floor(x / TILE_SIZE) * TILE_SIZE, tileY = Math.floor(y / TILE_SIZE) * TILE_SIZE;
      const key = Math.floor(y / TILE_SIZE) * columns + Math.floor(x / TILE_SIZE);
      let tile = this.tiles.get(key);
      if (!tile) {
        const width = Math.min(TILE_SIZE, this.width - tileX), height = Math.min(TILE_SIZE, this.height - tileY);
        tile = { x: tileX, y: tileY, width, height, coverage: new Uint8Array(width * height) };
        this.tiles.set(key, tile);
      }
      const index = (y - tileY) * tile.width + x - tileX;
      if (coverage > tile.coverage[index]) { tile.coverage[index] = coverage; changed.add(key); }
    }
  }
}

/** PhotoCraft selection Add/Subtract on quantized 8-bit coverage. */
export function combineCoverage(previous: number, coverage: number, mode: "restore" | "erase") {
  return mode === "restore" ? Math.max(previous, coverage) : Math.max(0, previous - coverage);
}
