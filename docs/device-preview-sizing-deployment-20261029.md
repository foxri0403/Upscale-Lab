# Device preview sizing and wallpaper names — 2026-10-29

Production: https://d3bjir1z2hduu0.cloudfront.net/app/devices

Implementation: `a42436f`, pushed to `gusrb03/explore-details`.

- Monitor and phone previews use the same 180 px frame height on normal viewports and the same viewport-based reduction on narrow screens. Their dimensions depend on viewport width, so toggling the navigation does not scale either preview. Grid minimums keep the fixed monitor frame inside its card.
- The image name is displayed by itself below the resolution/refresh-rate line on both platforms. The top row contains the display name and connection status.
- The name comes from the confirmed current image. Empty, unknown and external assignments with no resolved current image display `기본 바탕화면`. A pending image is shown separately as pending and is never mislabeled as the current image.
- Lock and home previews remain side by side, in that order. Device registration order, per-row card bottom alignment, apply/removal controls and account features are preserved.

## Validation

- Cloud production build passed, as did TypeScript compilation of the isolated commit contents.
- Production web suite: **62 / 62** tests passed.
- Browser checks against local and production bundles passed at **1920, 1440, 1360, 1280, 1120, 1000, 768, 390 and 320** px. Each check compared frame width/height before and after toggling the navigation, checked matching monitor/phone heights, card containment, no horizontal overflow, image names below resolution, and per-row card bottoms. Fixtures included a managed wallpaper, an external wallpaper without an asset, a cleared wallpaper, an unknown wallpaper and a pending image.
- Browser API responses were mocked; no real device wallpaper or account data was changed. Screenshots and geometry results are saved under ignored `artifacts/device-preview/`.

## Manual web-only deployment

- Web release: `/var/www/upscale-lab/releases/device-preview-20261029-092456`
- Reviewed sources: `/home/ubuntu/upscale-web-sources/device-preview-20261029-092456-reviewed`
- JavaScript: `/assets/index-BBg3cYr-.js`
- CSS: `/assets/index-CWh63evb.css`
- Backup manifest and previous release: `/home/ubuntu/deploy-backups/device-preview-20261029-092456`
- API remains `/home/ubuntu/upscale-api-releases/account-devices-20261029-080757`.

The full web source was compared with the previous reviewed production snapshot; only `Devices.tsx` and `styles.css` differed. Runtime hashes and source hashes were checked before switching the web symlink. CloudFront delivery, API health and Nginx were verified afterward. Existing image/download directories and old immutable web assets were preserved. The database, API, companion releases, credentials and CI/CD were unchanged. Owned upload/staging files were removed.
