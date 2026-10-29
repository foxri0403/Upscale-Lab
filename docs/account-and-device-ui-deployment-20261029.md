# Account settings and device preview deployment — 2026-10-29

Production: https://d3bjir1z2hduu0.cloudfront.net

Implementation commits: `17677f0` and mobile checkbox refinement `00e0141`, pushed to `gusrb03/explore-details`.

## Result

- Explore details use the page scroll without a nested scrolling region. Explore cards no longer show the lower-right menu. The cloud connection badge and sidebar Settings entry were removed.
- Settings are reached through **내 계정 → 계정 정보**, with a person icon and the user's profile photo when present.
- Users can change their login username after confirming their password. Profile photos accept actual JPEG/PNG/WebP images up to 5 MiB and 16 megapixels; the server strips metadata, fits the first frame within 512 × 512 pixels, and stores a PNG in the user's S3 prefix. Replaced photos are cleaned asynchronously.
- Password changes reuse the existing email verification flow, create a fresh BCrypt hash and revoke existing account sessions. Passwords are never stored by the web UI.
- Account deletion requires the current password and the exact phrase `계정 삭제`. A pending deletion disables login/session access immediately. The worker removes the captured Cognito subject, owned relational records and known owned S3 keys, with durable retries and a second object sweep. Active image processing must finish first. The worker never deletes a later account merely because it reuses an email address.
- Device lists sort by ascending registration timestamp with a deterministic ID tie break, independently of heartbeat/refresh order. Existing immutable registration timestamps are preserved.
- Android cards display **잠금 화면 → 홈 화면** simultaneously, including unknown assignments. The unknown-image placeholder fills its phone frame. Device card bottoms stretch to the same height within a grid row; actions sit at the bottom.

## Verification

- Full working backend: **196 / 196** tests passed.
- Isolated commit contents, excluding pre-existing uncommitted work: **106 / 106** backend tests passed; TypeScript compilation and new device layout tests passed.
- Production web tests: **62 / 62** passed, including authentication boundaries, routing and unchanged signed Android/Windows download delivery.
- Browser checks against both local and deployed bundles passed at widths **1920, 1440, 1000, 768 and 390**. Checks covered stable device order after reversed refresh responses, equal card bottoms per row, both phone previews, placeholder fill, no horizontal overflow, removal of obsolete controls, profile/name forms, password reset and deletion confirmation. Destructive browser actions used mocked APIs.
- A disposable `@example.invalid` account verified the real HTTPS API: username update, S3 photo upload/read/removal, immediate session denial after deletion, PostgreSQL deletion across image/project/device relations, optional inpainting table queries, both known-key S3 cleanup passes and completion of old-photo tasks. Only this fixture's records and objects were created/deleted. No real user's wallpaper or GPU job was changed. Email delivery and entering a real user's verification code were not exercised.
- The initial probe incorrectly forwarded the API bearer header to the presigned S3 URL; it was corrected to use a separate unauthenticated HTTP client. A subsequent assertion also included old-photo tasks while invoking only the account task; the probe now processes all of its own task IDs explicitly. Both issues were in the probe. Final real API verification passed.

Local verification evidence is under ignored `artifacts/account-settings/`: test logs, five device screenshots, account screenshots, `ui-local.json`, `ui-production.json` and release manifests. These files contain fixture credentials only and are not committed.

## Manual production release

- API: `/home/ubuntu/upscale-api-releases/account-devices-20261029-080757`
- Web: `/var/www/upscale-lab/releases/account-devices-20261029-080757`
- JavaScript: `/assets/index-BvCY8EXz.js`
- CSS: `/assets/index-DPvYVV_u.css`
- Reviewed full source snapshot: `/home/ubuntu/upscale-api-sources/account-devices-20261029-080757-reviewed`; the corresponding web source snapshot points to the same directory.
- Private backup and verification logs: `/home/ubuntu/deploy-backups/account-devices-20261029-080757`, including `database-before.dump`, deployment manifests and `api-verification-final.log`.

The deployment first compared the current sources to the preceding reviewed production sources and verified unchanged runtime dependency hashes. Only the four application assemblies and their symbols were replaced. An additive SQL migration, `20261028225336_AddAccountManagement`, added two nullable user columns and `account_cleanup_tasks`; unrelated pending migrations were not applied. The database backup was completed before migration. Existing images, companion downloads and previous immutable web assets were preserved. No credentials, CI/CD settings or mobile releases changed.

API health, Nginx health and CloudFront delivery were verified after the web switch. Temporary deployment uploads and staging directories were removed. Previous releases remain available for recovery. **If reverting the web UI, keep the new API and its account cleanup worker running after accepting any deletion requests**; the old API does not enforce the new pending-deletion flag. Restoring the database would require a separate reviewed recovery decision because it can restore deleted account data.
