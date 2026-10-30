# PhotoCraft mask brush

- Upstream: https://github.com/storytold/photocraft
- Revision: `5f9b4391874aabc9deb2fcb905267613e4d93ec8` (2026-10-29).
- Elected license: MIT; full notice in [LICENSE-MIT](LICENSE-MIT).
- Adapted files: `crates/algo/src/matting/brush.rs`, `crates/algo/src/selection.rs`, and `crates/algo/src/magnetic.rs`.
- Implementation: `web/src/vendor/photocraft-mask-brush.ts`; browser adapter: `web/src/subject-mask.ts`.
- Modifications: TypeScript translation of segment distance, hardness falloff, maximum coverage within a stroke, sparse 256px tiles and quantized mask Add/Subtract; bounded incremental drawing and canvas read/write supplied by Upscale Lab.
- Magnetic lasso: `web/src/vendor/photocraft-magnetic-lasso.ts` adapts premultiplied RGB/alpha Sobel edges, ridge/subpixel centres, contrast threshold, and eight-connected corridor search. The browser uses a maximum 900px analysis preview, piecewise searches, an approximate tangent cost, and no upstream block cache; traced vectors replay at source resolution. This is edge-based selection, not semantic segmentation.
- Exclusions: no upstream inference engine, UI, fonts, icons, brand assets or external hosted service calls are included.
- Distributed attribution: `web/public/licenses/photocraft-mask-brush.txt`, served at `/licenses/photocraft-mask-brush.txt`.

Keep upstream provenance and the elected license when modifying or distributing this subset. Other repositories under the Storytold organization have their own licenses; this notice grants no rights to those repositories or to model weights.
