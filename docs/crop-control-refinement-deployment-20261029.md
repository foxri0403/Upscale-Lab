# Crop frame and control refinement — 2026-10-29

Production: https://d3bjir1z2hduu0.cloudfront.net

Implementation: `398997a`, pushed to `gusrb03/explore-details`.

- Removed the left crop icons from ratio choices, preserving the active check on the right.
- Entering custom ratio mode from another preset starts with the full original rectangle. Re-clicking an already-active custom choice preserves the user's adjustments; saved custom regions still survive reload.
- Excluded regions use a gray mask with 62% opacity so the original remains visible beneath it.
- The selection uses a thin white border, thicker corner brackets and midpoint bars inspired by the supplied reference. Project colors remain on controls and keyboard focus. Resize handles retain mouse/touch hit areas and keyboard operation; preset frames show non-interactive matching markers. The internal grid was removed.
- Width and height are separate stacked rows with the number next to the label. Their `(px)` suffixes were removed. The size caption is `표시 영역 … × … px`, and zoom actions are ordered `+`, then `−`.
- Removed all crop instructional paragraphs below the controls.

## Verification

- Cloud build and isolated TypeScript compilation passed. Production web suite: **67 / 67** tests passed.
- Browser checks against local and production bundles passed at **1440, 1000, 768, 390 and 320** px. Checks covered preset order, full-source initialization after switching from every fixed preset, preservation of edits when re-clicking custom, translucent masks, marker counts, removed grid/icons/help text, vertically stacked input rows and zoom button order.
- Existing mouse/keyboard/touch movement, custom handle resize, numeric input, zoom, save/reload, export dimensions and decoded PNG pixels continued to pass. Mixed layer/upscale dimensions and Explore author photos were also checked by the regression fixture.
- API calls in browser checks were mocked; no real user images, device wallpapers or account records changed. Screenshots include a repository fox photo for visual comparison with the reference, under ignored `artifacts/crop-refinement/`.

## Manual web-only deployment

- Web: `/var/www/upscale-lab/releases/crop-refinement-20261029-112800`
- Reviewed sources: `/home/ubuntu/upscale-web-sources/crop-refinement-20261029-112800-reviewed`
- JavaScript: `/assets/index-BhwnJE9W.js`; CSS: `/assets/index-BZDoAxJQ.css`
- Previous release and manifest: `/home/ubuntu/deploy-backups/crop-refinement-20261029-112800`
- API unchanged: `/home/ubuntu/upscale-api-releases/studio-crop-20261029-105304`

The full web sources were compared against the preceding reviewed release; only `CropControls.tsx`, `CropEditor.tsx` and `crop.css` differed. Baseline and static asset hashes were verified before the atomic web switch. CloudFront, API health and service checks passed afterward. Previous assets, image/download directories, credentials, database, API, companion releases and CI/CD were preserved. Owned upload/staging files were removed.
