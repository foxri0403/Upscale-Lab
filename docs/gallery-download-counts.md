# Gallery download counts

Gallery cards and shared-image previews display the existing `downloadCount` field as a number, defaulting to `0` for older responses. It is a cumulative download counter, not a count of distinct people. Thumbnail requests and ordinary preview URL requests do not change it.

## Current mobile flow

1. `POST /api/gallery/{id}/download-url?countDownload=false` obtains a short-lived S3 URL without incrementing the counter.
2. Android downloads the original into its working cache.
3. Only after the download succeeds, the app calls `POST /api/gallery/{id}/download-complete`.
4. The response is `{ "downloadCount": 1 }`. The card and open preview use the returned persisted value. Gallery list/detail responses return the same field after refresh.

The counter request runs independently of opening the downloaded image. A failed metric request does not discard a downloaded file or block preview; a later refresh reads the persisted count. Cancelling library import after downloading still represents one completed download. Failed file downloads never report completion. A completion report represents one download; it is not an idempotent retry endpoint, so clients must not blindly replay a report after an uncertain response.

The service checks the same public/owner accessibility rules as the existing download endpoint. PostgreSQL increments the counter with one atomic update, preventing concurrent increments from overwriting each other. Mutable timestamps use UTC.

## Compatibility and rollout

The existing `GalleryPost.DownloadCount` column and gallery response field are reused; no database migration is required. Historical values are preserved. Older clients omitting `countDownload=false` retain the previous count-on-URL-issuance behavior. They must not also send completion reports.

Deploy the matching API manually using the existing deployment procedure. Do not introduce automatic deployment. A new client can still open images against an older API: the legacy URL endpoint counts its request, and an unsupported completion report does not interrupt preview. Exact completed-download reporting requires the updated API.

## Validation

- Backend service tests cover persisted counts in list/detail responses, uncounted previews/URL issuance, legacy behavior, and rejection of inaccessible or missing posts.
- Client tests cover API requests, parsing/default zero, reporting after successful file receipt, and no report after download failure.
