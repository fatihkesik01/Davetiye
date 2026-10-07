# Davetiye media ingress Worker

This isolated Cloudflare Worker package provides photo ingress and a server-to-server Stream TUS provisioning broker. It keeps media bytes out of the Davetiye API/VPS. The R2 binding must point at a private bucket; this Worker exposes no object read route.

## Routes and .NET contract

- `PUT /v1/images/{assetId}` accepts the binary image body. Send the signed capability in `X-Media-Capability`; the bearer value is never placed in the URL. The URI is stable per asset. The Worker returns `204` after storing the normalized WebP; a same-capability retry also returns `204` if the stored object is already verified by its private object metadata. A failed or ambiguous put is resolved with `HEAD`; cleanup never deletes by asset key because a concurrent retry may own the winning object.
- Image capability wire format: `base64url(UTF8(JSON claims)) + "." + base64url(HMAC-SHA256(signingKey, encodedClaims))`. Claims are `{ "v":1, "aud":"davetiye-image-ingress", "assetId":"<32 lowercase hex>", "maxBytes":<positive integer>, "exp":<Unix seconds> }`. The API must sign the declared byte ceiling after it has validated it against the grant and set `IngressUri` to `https://<worker-host>/v1/images/<assetId>`; return the token separately for the `X-Media-Capability` header. HMAC secret is base64url-encoded with at least 32 random bytes.
- `POST /v1/stream/upload-capabilities` is server-to-server only and requires `X-Broker-Authorization: Bearer <BROKER_AUTH_KEY>`. JSON body is `{ assetId, declaredByteLength, maximumBytes, expiresAt, maxDurationSeconds }`. The broker authenticates and enforces a 4 KiB request cap before allocating a Durable Object, then pins TUS `Upload-Length` to `declaredByteLength`, after checking it does not exceed `maximumBytes` or configured `MAX_VIDEO_BYTES`. The response is `{ uploadUrl, expiresAt }`; `uploadUrl` is the one-time Stream TUS capability and must be returned to the browser only through the authenticated Creator intent response.

The Stream broker assigns one Durable Object per asset. It stores a canonical request fingerprint and `provisioning` state before calling Cloudflare. A successful provider `Location` is encrypted with AES-GCM before durable storage. Same-parameter retries decrypt and return that same URL. Changed parameters conflict. Network/provider failures are terminal `ambiguous` state and never trigger another provider POST, since the first POST may have created a resource despite losing its response.

## Local verification

```powershell
npm install
npm test
npm run typecheck
npm run dev
```

Tests use local fake Images/R2/Stream bindings; they do not claim Cloudflare acceptance. The Images binding input limit is 20 MB, so keep `MAX_IMAGE_BYTES` at or below the provider limit. The Worker counts the stream regardless of `Content-Length`, transforms to WebP, and uses an R2 create-only conditional write. A single R2 object write is not multipart. Per Fatih's 2026-10-04 decision, account-level limits, actual failure/commit behavior, retry semantics, and stored EXIF/GPS inspection are deferred to Phase 11 P11-M6/M8; Phase 4 tests verify local contracts only.

## Runtime bindings and secrets

Wrangler binds `IMAGES`, private `MEDIA_BUCKET`, and SQLite-backed `STREAM_BROKER`. Set secrets out of band with `wrangler secret put`:

- `CAPABILITY_SIGNING_KEY_B64` — base64url 256-bit-or-larger HMAC key shared with the API signer.
- `BROKER_AUTH_KEY` — high-entropy (at least 32-character) server-to-server broker credential shared with the API; the API sends it as `Bearer <secret>`.
- `TUS_LOCATION_ENCRYPTION_KEY_B64` — separate base64url 256-bit AES-GCM key.
- `CLOUDFLARE_ACCOUNT_ID` and `CLOUDFLARE_STREAM_API_TOKEN` — Stream API account and narrowly scoped token.

Configure `ALLOWED_UPLOAD_ORIGINS` as a comma-separated exact origin allowlist. `MAX_IMAGE_BYTES`, `MAX_VIDEO_BYTES`, and `MAX_VIDEO_DURATION_SECONDS` are operational hard ceilings; resolved plan limits remain authoritative and must be passed by the API. Confirm account-level body limits, R2 conditional semantics, Stream TUS byte enforcement, and cleanup behavior in the real Cloudflare account during Phase 11 before production media is enabled.
