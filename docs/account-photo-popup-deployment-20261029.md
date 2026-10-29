# Profile photo popup and account menu dismissal — 2026-10-29

Production: https://d3bjir1z2hduu0.cloudfront.net

Implementation: `7f699db`, pushed to `gusrb03/explore-details`.

- Clicking the profile photo in **내 계정 → 계정 정보** opens a popup showing the current photo, with **사진 변경** and **기본 사진** actions below it. The standalone photo fieldset and file-format/size introduction were removed from account settings. Existing file validation remains enforced.
- A successful photo upload or default-photo reset closes the popup and updates both the account photo and header avatar. Errors remain visible in the popup for retry. Dismissing the popup preserves unsaved account form values and returns focus to the avatar trigger.
- The account dropdown closes on an outside mouse click or touch, outside keyboard focus, and Escape. Selecting account information closes the dropdown before opening settings; focus returns to its summary when settings close.
- Dialog headings use unique IDs so the account and nested photo dialogs retain separate accessible names. Escape and backdrop clicks close the top photo popup while leaving account settings open. Existing in-flight mutation guards remain in place.
- Account form buttons can wrap on narrow screens to prevent horizontal overflow.

## Verification

- Cloud build and TypeScript compilation of isolated commit contents passed.
- Production web suite: **62 / 62** tests passed.
- Local and production browser checks passed at **1440, 1000, 768, 390 and 320** px. Checks covered avatar-based opening, current-photo preview, removal of standalone controls/introduction, unique dialog labels, backdrop/Escape dismissal, focus restoration, preservation of unsaved form values, inside/outside menu clicks, touch dismissal, and horizontal containment.
- The browser launched a native file chooser, simulated a failed upload, retried the same file successfully, rejected an invalid file, restored the default avatar, and checked the header/settings state after reload. Browser API calls were mocked; real account photos and passwords were not changed by these checks.
- Screenshots and test results are in ignored `artifacts/account-photo/`.

## Manual web-only release

- Web: `/var/www/upscale-lab/releases/account-photo-20261029-100540`
- Reviewed sources: `/home/ubuntu/upscale-web-sources/account-photo-20261029-100540-reviewed`
- JavaScript: `/assets/index-aK3cYCOh.js`
- CSS: `/assets/index-DIaKRvPn.css`
- Deployment manifest and previous release: `/home/ubuntu/deploy-backups/account-photo-20261029-100540`
- API unchanged: `/home/ubuntu/upscale-api-releases/account-devices-20261029-080757`

The complete web source was compared with the preceding reviewed snapshot; only `AccountSettings.tsx`, `Workspace.tsx`, `ui.tsx` and `styles.css` changed. Baseline sources and static hashes were verified before the atomic web switch. CloudFront, API health, services and regression tests passed afterward. Existing image/download directories and previous immutable assets were retained. No API, database, credential, companion release or CI/CD change was made. Owned uploads and staging were removed.
