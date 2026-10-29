# Crop preview size and palette — 2026-10-29

Production: https://d3bjir1z2hduu0.cloudfront.net

Implementation: `b873e91`, pushed to `gusrb03/explore-details`.

- Crop, upscale and background separation share the same fit calculation inside the ruler area. Crop no longer subtracts the ruler space a second time and displays a smaller source.
- Fit sizing uses CSS container units to follow the sidebar animation immediately. Removing the measured surface dimensions prevents a resize/scrollbar feedback loop. Fit mode has no scrolling; percentage zoom retains scrolling through the original crop image.
- Clicking the palette outside the image toggles the current light background and dark gray `#303030`. The choice stays selected across editing-tool and sidebar changes. The palette also supports keyboard and touch input.
- The white selection border, corner brackets and edge markers keep their existing colors. Image clicks, selection movement and resize handles do not activate the palette toggle.
- The regular cropped preview uses normalized image-plane dimensions so it follows the same responsive frame without changing the selected source pixels.

## Verification

- Cloud build and isolated TypeScript compilation passed.
- Production web suite: **67 / 67** passed, including cloud smoke tests.
- Local and production browser checks passed at **1440, 1000, 768, 390 and 320** px. At desktop/tablet widths, all three tools had matching original-image dimensions. Twelve sidebar transitions were sampled frame by frame without horizontal/vertical overflow or scrollbar space in fit mode; the image tracked available space without delayed resize jumps.
- Light/dark toggling passed with mouse, keyboard and touch. The white crop outline and palette state across tools were verified. Manual percentage zoom still scrolls, and returning to fit removes overflow.
- Existing preset selection, custom initialization, translucent masks, mouse/keyboard/touch movement, handle resizing, numeric dimensions, save/reload, downloaded PNG dimensions and decoded crop pixels passed. Mixed layer/upscale dimensions and Explore profile photos also passed the existing regression fixture.
- Browser API requests used deterministic mocks; real user images and account records were not modified. Screenshots and test results are under ignored `artifacts/crop-palette/`.

## Manual web deployment

- Web: `/var/www/upscale-lab/releases/crop-palette-20261029-115208`
- Reviewed sources: `/home/ubuntu/upscale-web-sources/crop-palette-20261029-115208-reviewed`
- JavaScript: `/assets/index-s2cQocRJ.js`; CSS: `/assets/index-BHcYXFnj.css`
- Previous release and manifest: `/home/ubuntu/deploy-backups/crop-palette-20261029-115208`
- API unchanged: `/home/ubuntu/upscale-api-releases/studio-crop-20261029-105304`

The full web source was compared with the previous reviewed release. Only `CropEditor.tsx`, `StudioCanvas.tsx`, `StudioPreview.tsx` and `crop.css` differed. Baseline and static hashes were verified before atomically switching the web symlink. CloudFront served the new bundle and stylesheet; API health, Nginx and API service checks passed. Previous assets and the existing image/download directories were retained. Owned upload/staging files and the deployment helper were removed.
