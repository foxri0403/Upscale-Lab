# Movable crop regions and Explore author photos — 2026-10-29

Production: https://d3bjir1z2hduu0.cloudfront.net

Implementation commits: `5b83f90` and source-dimension refinement `c5bc0da`, pushed to `gusrb03/explore-details`.

## Behavior

- Crop choices are ordered **원본 비율 → 16:9 PC → 9:16 모바일 → 1:1 정사각형 → 사용자 비율**.
- The crop tool keeps the entire source image in its original shape. Opaque gray masks identify excluded pixels, while the selected region has a border and guides. Choosing another preset changes the selection without reshaping the source preview.
- Dragging the selection moves it inside the source. Clicking excluded space recenters it within the image bounds. Crop zoom buttons shrink/enlarge the selected region while preserving its aspect ratio. The existing preview zoom controls change only the view.
- Custom crops support numeric source-pixel width/height and eight edge/corner handles. Mouse, touch and keyboard movement are supported; Shift uses a larger keyboard step. Regions remain inside the original image and have at least one source pixel in each dimension.
- Regions are stored as normalized source coordinates in the existing edit JSON. Static PNG export and immutable Live 2D layer rendering resolve the same pixel rectangle, so panning and crop zoom persist after reload and are included in downloaded/applied files. Baked wallpaper files reset their crop metadata to avoid applying the crop twice.
- Crop controls measure the actual layer source when an upscaled project result has different dimensions. On small screens, crop settings appear below the canvas so the drag area remains usable.
- Public gallery posts and web wallpaper summaries include the author's current username and signed profile-photo URL. Explore cards and details display the photo, with a person icon for absent or failed images. Author queries remain limited to already-visible posts/assets and do not expose email addresses or raw photo keys.

## Verification

- Full working backend: **203 / 203** tests passed. Isolated commit backend: **113 / 113** passed, with isolated TypeScript compilation also successful.
- Production web suite: **67 / 67** tests passed.
- Local and production browser checks covered preset ordering, stable full-source preview bounds, opaque exclusion masks, selection movement, crop zoom, custom handle resizing, numeric sizes, keyboard/touch movement, save/reload and PNG download. Exported dimensions and the first decoded pixel matched the selected source coordinates.
- Browser layout checks ran at **1440, 1000, 768 and 390** px. A mixed-resolution fixture verified that an 800 × 600 project result with 400 × 300 layers uses the 400 × 300 crop source. Web and legacy mobile gallery author photos were checked in both cards and details. Browser API calls were mocked.
- A disposable `@example.invalid` account verified the real HTTPS API, PostgreSQL crop round trip, rejection of out-of-bounds edits, real S3 Live 2D file dimensions/pixels, reset of baked metadata, and accessible author-photo URLs from both public feeds. Its public fixtures, account and known S3 objects were removed afterward. No real user device wallpaper or GPU job was changed.
- Evidence is under ignored `artifacts/studio-crop/` and `artifacts/studio-crop-view/`, including test logs, screenshots, geometry, a decoded export fixture and manifests.

## Manual releases

- API: `/home/ubuntu/upscale-api-releases/studio-crop-20261029-105304`
- Final web: `/var/www/upscale-lab/releases/studio-crop-view-20261029-110101`
- API reviewed sources: `/home/ubuntu/upscale-api-sources/studio-crop-20261029-105304-reviewed`
- Final web reviewed sources: `/home/ubuntu/upscale-web-sources/studio-crop-view-20261029-110101-reviewed`
- JavaScript: `/assets/index-DvFQutXi.js`; CSS: `/assets/index-YWk1nirc.css`
- Private manifests and API verification: `/home/ubuntu/deploy-backups/studio-crop-20261029-105304`
- Final web switch backup: `/home/ubuntu/deploy-backups/studio-crop-view-20261029-110101`

The full sources were compared with the preceding reviewed production snapshots before packaging. Only the reviewed crop/author changes differed. Runtime dependency hashes were preserved; application assemblies were replaced, then the API was health-checked and verified with its disposable account before switching the web. The final source-dimension refinement was a web-only switch. CloudFront delivery, Nginx and API health passed afterward.

No database schema migration was needed; crop coordinates use existing JSON columns. Companion downloads, original images, previous immutable assets, credentials and CI/CD were preserved. Owned staging/uploads were removed, and previous releases remain available. Prefer a forward fix after accepting custom crop edits: older crop UIs do not understand the new region metadata, and older APIs reject the custom preset. Keep the new API available whenever serving the new web bundle.
