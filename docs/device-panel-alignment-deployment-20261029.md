# PC and mobile device panel alignment — 2026-10-29

Production: https://d3bjir1z2hduu0.cloudfront.net/app/devices

Implementation: `2b63b13`, pushed to `gusrb03/explore-details`.

- Desktop and Android headers use the same 48 × 48 icon container, 28 px icon size and 64 px header row. Long device names can occupy two lines without shifting the icon or display panels.
- The Android lock and home previews share a bordered panel with the same border, radius and inset as a PC monitor panel. Lock precedes home, and each label is centered above its phone preview.
- Android connection state appears once at the top right of the shared panel: `연결됨`, `연결중` or `미연결`. PC connection indicators remain associated with their individual monitors.
- Display header, preview, resolution/refresh-rate and image-name rows share fixed row geometry on both platforms. Fractional refresh rates and wrapped narrow-screen metadata retain the image-name row alignment.
- Previous fixed preview sizes, registration ordering and per-row outer card bottom alignment remain in place.

## Verification

- The cloud production build and TypeScript compilation of the isolated commit contents passed.
- Production web suite: **62 / 62** tests passed.
- Local and production browser checks passed at **1920, 1440, 1360, 1280, 1120, 1000, 768, 390 and 320** px, before and after toggling the navigation. Checks compared icon placement, preview dimensions, first preview/metadata row coordinates across cards in the same row, card containment, phone-label centering, one mobile connection indicator, status/label separation and visible borders.
- Fixtures included managed and unknown images, pending application, cleared and external wallpapers, a long offline device name and a fractional refresh rate. Browser API calls were mocked; real device wallpapers and account records were not modified.
- Screenshots, geometry and logs are in ignored `artifacts/device-alignment/`.

## Manual web-only release

- Web: `/var/www/upscale-lab/releases/device-alignment-20261029-094325`
- Reviewed sources: `/home/ubuntu/upscale-web-sources/device-alignment-20261029-094325-reviewed`
- JavaScript: `/assets/index-DBmaS_EU.js`
- CSS: `/assets/index-CUGan4Hd.css`
- Previous release and deployment manifest: `/home/ubuntu/deploy-backups/device-alignment-20261029-094325`
- API unchanged: `/home/ubuntu/upscale-api-releases/account-devices-20261029-080757`

The complete web source was compared against the preceding reviewed production snapshot; only `Devices.tsx` and `styles.css` changed. Source and static asset hashes were verified before the atomic web switch, followed by CloudFront, API health and service checks. Existing image/download directories and previous immutable assets were preserved. No database, API, companion release, credential or CI/CD change was made. Temporary uploads and staging were removed.
