var __defProp = Object.defineProperty;
var __name = (target, value) => __defProp(target, "name", { value, configurable: true });

// src/capability.ts
var encoder = new TextEncoder();
function encodeBase64Url(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}
__name(encodeBase64Url, "encodeBase64Url");
function decodeBase64Url(value) {
  const base64 = value.replaceAll("-", "+").replaceAll("_", "/");
  const binary = atob(base64 + "=".repeat((4 - base64.length % 4) % 4));
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}
__name(decodeBase64Url, "decodeBase64Url");
async function verifyImageCapability(token, secret, expectedAssetId, nowSeconds, maxCeiling) {
  const parts = token.split(".");
  if (parts.length !== 2 || !parts[0] || !parts[1]) return null;
  try {
    const key = await importHmacKey(secret);
    const verified = await crypto.subtle.verify("HMAC", key, toBufferSource(decodeBase64Url(parts[1])), toBufferSource(encoder.encode(parts[0])));
    if (!verified) return null;
    const claims = JSON.parse(new TextDecoder().decode(decodeBase64Url(parts[0])));
    if (!isImageCapabilityClaims(claims) || claims.assetId !== expectedAssetId || claims.exp <= nowSeconds || claims.maxBytes <= 0 || claims.maxBytes > maxCeiling) {
      return null;
    }
    return claims;
  } catch {
    return null;
  }
}
__name(verifyImageCapability, "verifyImageCapability");
async function importHmacKey(secret) {
  const raw = decodeBase64Url(secret);
  if (raw.byteLength < 32) throw new Error("Capability signing key must contain at least 256 bits.");
  return crypto.subtle.importKey("raw", toBufferSource(raw), { name: "HMAC", hash: "SHA-256" }, false, ["sign", "verify"]);
}
__name(importHmacKey, "importHmacKey");
function toBufferSource(bytes) {
  return Uint8Array.from(bytes).buffer;
}
__name(toBufferSource, "toBufferSource");
function isImageCapabilityClaims(value) {
  if (typeof value !== "object" || value === null) return false;
  const claims = value;
  return claims.v === 1 && claims.aud === "davetiye-image-ingress" && typeof claims.assetId === "string" && /^[0-9a-f]{32}$/iu.test(claims.assetId) && Number.isSafeInteger(claims.maxBytes) && Number.isSafeInteger(claims.exp);
}
__name(isImageCapabilityClaims, "isImageCapabilityClaims");
function base64Url(bytes) {
  return encodeBase64Url(bytes);
}
__name(base64Url, "base64Url");
function fromBase64Url(value) {
  return decodeBase64Url(value);
}
__name(fromBase64Url, "fromBase64Url");

// src/image-ingress.ts
var OUTPUT_FORMATS = /* @__PURE__ */ new Set(["image/webp"]);
async function handleImageUpload(request, assetId, env) {
  if (request.method !== "PUT" || !request.body) {
    await cancelBody(request, "Unsupported upload request.");
    return problem(405, "A non-empty PUT body is required.");
  }
  const maxImageBytes = parsePositiveInteger(env.MAX_IMAGE_BYTES);
  if (maxImageBytes === null) {
    await cancelBody(request, "Image ingress is not configured.");
    return problem(503, "Image ingress is not configured.");
  }
  const capability = request.headers.get("X-Media-Capability");
  if (!capability) {
    await cancelBody(request, "Upload capability is required.");
    return problem(401, "Upload capability is required.");
  }
  const claims = await verifyImageCapability(
    capability,
    env.CAPABILITY_SIGNING_KEY_B64,
    assetId,
    Math.floor((env.now?.() ?? Date.now()) / 1e3),
    maxImageBytes
  );
  if (!claims) {
    await cancelBody(request, "Upload capability is invalid or expired.");
    return problem(401, "Upload capability is invalid or expired.");
  }
  const key = `creator/${assetId}.webp`;
  const declaredLength = request.headers.get("Content-Length");
  if (declaredLength !== null && (!/^\d+$/u.test(declaredLength) || Number(declaredLength) > claims.maxBytes)) {
    await cancelBody(request, "Declared upload length exceeds capability ceiling.");
    return problem(413, "Upload exceeds its byte ceiling.");
  }
  let limitExceeded = false;
  try {
    const existing = await env.MEDIA_BUCKET.head(key);
    if (existing) {
      await cancelBody(request, "This asset already has a stored upload.");
      return isNormalizedObject(existing, assetId) ? new Response(null, { status: 204, headers: { "Cache-Control": "no-store" } }) : problem(409, "This asset already has an incompatible stored object.");
    }
    const sniffed = await sniffRasterBody(request.body);
    if (!sniffed) return problem(415, "Only supported raster image formats are accepted.");
    const limitedBody = countAndLimit(sniffed.body, claims.maxBytes, () => {
      limitExceeded = true;
    });
    const format = env.IMAGE_OUTPUT_FORMAT;
    if (!OUTPUT_FORMATS.has(format)) {
      await cancelBody(request, "Unsupported image output format.");
      return problem(503, "Image output format is not configured.");
    }
    const optimized = await env.IMAGES.input(limitedBody).output({ format });
    const output = optimized.response();
    if (!output.ok || !output.body || output.headers.get("Content-Type")?.toLowerCase() !== "image/webp") {
      return problem(422, "Image decoding or normalization failed.");
    }
    const stored = await env.MEDIA_BUCKET.put(key, output.body, {
      onlyIf: { etagDoesNotMatch: "*" },
      httpMetadata: {
        contentType: output.headers.get("Content-Type") ?? "image/webp",
        cacheControl: "private, no-store"
      },
      customMetadata: { assetId, normalized: "true" }
    });
    if (!stored) {
      const winner = await env.MEDIA_BUCKET.head(key);
      return winner && isNormalizedObject(winner, assetId) ? new Response(null, { status: 204, headers: { "Cache-Control": "no-store" } }) : problem(409, "This asset has already received an upload.");
    }
    return new Response(null, { status: 204, headers: { "Cache-Control": "no-store" } });
  } catch (error) {
    const committed = await env.MEDIA_BUCKET.head(key).catch(() => null);
    if (committed && isNormalizedObject(committed, assetId)) {
      return new Response(null, { status: 204, headers: { "Cache-Control": "no-store" } });
    }
    if (limitExceeded || error instanceof UploadTooLargeError) return problem(413, "Upload exceeds its byte ceiling.");
    if (error instanceof UploadInterruptedError) return problem(400, "Upload stream was interrupted.");
    return problem(422, "Image could not be normalized or stored.");
  }
}
__name(handleImageUpload, "handleImageUpload");
function isNormalizedObject(object, assetId) {
  return object.customMetadata?.assetId === assetId && object.customMetadata.normalized === "true" && object.httpMetadata?.contentType === "image/webp";
}
__name(isNormalizedObject, "isNormalizedObject");
async function cancelBody(request, reason) {
  await request.body?.cancel(reason).catch(() => void 0);
}
__name(cancelBody, "cancelBody");
function countAndLimit(body, maximumBytes, onExceeded) {
  let received = 0;
  return body.pipeThrough(new TransformStream({
    transform(chunk, controller) {
      received += chunk.byteLength;
      if (received > maximumBytes) {
        onExceeded?.();
        controller.error(new UploadTooLargeError());
        return;
      }
      controller.enqueue(chunk);
    },
    cancel() {
    }
  }));
}
__name(countAndLimit, "countAndLimit");
async function sniffRasterBody(body) {
  const reader = body.getReader();
  const prefix = new Uint8Array(12);
  let prefixLength = 0;
  let trailingChunk = null;
  let sourceEnded = false;
  try {
    while (prefixLength < prefix.byteLength) {
      const next = await reader.read();
      if (next.done) {
        sourceEnded = true;
        break;
      }
      const copied = Math.min(next.value.byteLength, prefix.byteLength - prefixLength);
      prefix.set(next.value.subarray(0, copied), prefixLength);
      prefixLength += copied;
      if (copied < next.value.byteLength) {
        trailingChunk = next.value.subarray(copied);
      }
    }
    const contentType = classifyRasterPrefix(prefix, prefixLength);
    if (!contentType) {
      await reader.cancel("Unsupported or truncated raster signature.").catch(() => void 0);
      reader.releaseLock();
      return null;
    }
    let prefixPending = true;
    let trailingPending = trailingChunk !== null;
    const replay = new ReadableStream({
      async pull(controller) {
        if (prefixPending) {
          prefixPending = false;
          controller.enqueue(prefix.subarray(0, prefixLength));
          return;
        }
        if (trailingPending && trailingChunk) {
          trailingPending = false;
          controller.enqueue(trailingChunk);
          trailingChunk = null;
          return;
        }
        if (sourceEnded) {
          controller.close();
          reader.releaseLock();
          return;
        }
        try {
          const next = await reader.read();
          if (next.done) {
            sourceEnded = true;
            controller.close();
            reader.releaseLock();
          } else {
            controller.enqueue(next.value);
          }
        } catch (error) {
          controller.error(error);
          reader.releaseLock();
        }
      },
      async cancel(reason) {
        sourceEnded = true;
        trailingChunk = null;
        await reader.cancel(reason).catch(() => void 0);
        reader.releaseLock();
      }
    });
    return { contentType, body: replay };
  } catch (error) {
    await reader.cancel(error).catch(() => void 0);
    reader.releaseLock();
    throw error;
  }
}
__name(sniffRasterBody, "sniffRasterBody");
function classifyRasterPrefix(prefix, length) {
  if (length >= 3 && prefix[0] === 255 && prefix[1] === 216 && prefix[2] === 255) return "image/jpeg";
  if (length >= 8 && prefix[0] === 137 && ascii(prefix, 1, 3) === "PNG" && prefix[4] === 13 && prefix[5] === 10 && prefix[6] === 26 && prefix[7] === 10) return "image/png";
  if (length >= 6 && (ascii(prefix, 0, 6) === "GIF87a" || ascii(prefix, 0, 6) === "GIF89a")) return "image/gif";
  if (length >= 12 && ascii(prefix, 0, 4) === "RIFF" && ascii(prefix, 8, 4) === "WEBP") return "image/webp";
  return null;
}
__name(classifyRasterPrefix, "classifyRasterPrefix");
function ascii(bytes, start, length) {
  return String.fromCharCode(...bytes.subarray(start, start + length));
}
__name(ascii, "ascii");
var UploadTooLargeError = class extends Error {
  static {
    __name(this, "UploadTooLargeError");
  }
};
var UploadInterruptedError = class extends Error {
  static {
    __name(this, "UploadInterruptedError");
  }
};
function parsePositiveInteger(value) {
  if (!/^\d+$/u.test(value)) return null;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
}
__name(parsePositiveInteger, "parsePositiveInteger");
function problem(status, title) {
  return Response.json({ title, status }, { status, headers: { "Cache-Control": "no-store" } });
}
__name(problem, "problem");

// src/stream-broker.ts
var encoder2 = new TextEncoder();
function isBrokerAuthorized(header, configuredSecret) {
  if (configuredSecret.trim().length < 32) return false;
  return constantTimeEqual(header, `Bearer ${configuredSecret}`);
}
__name(isBrokerAuthorized, "isBrokerAuthorized");
function isStreamProviderConfigured(env) {
  try {
    const baseUrl = new URL(env.STREAM_API_BASE);
    const encryptionKey = fromBase64Url(env.TUS_LOCATION_ENCRYPTION_KEY_B64);
    return baseUrl.protocol === "https:" && baseUrl.hostname === "api.cloudflare.com" && (baseUrl.port === "" || baseUrl.port === "443") && !baseUrl.username && !baseUrl.password && baseUrl.pathname === "/" && !baseUrl.search && !baseUrl.hash && /^[0-9a-f]{32}$/iu.test(env.CLOUDFLARE_ACCOUNT_ID) && env.CLOUDFLARE_STREAM_API_TOKEN.trim().length >= 32 && encryptionKey.byteLength === 32;
  } catch {
    return false;
  }
}
__name(isStreamProviderConfigured, "isStreamProviderConfigured");
var StreamUploadBroker = class {
  constructor(state, env) {
    this.state = state;
    this.env = env;
  }
  state;
  env;
  static {
    __name(this, "StreamUploadBroker");
  }
  fetch(request) {
    const path = new URL(request.url).pathname;
    if (request.method === "POST" && path === "/provision") {
      return this.state.blockConcurrencyWhile(() => this.provision(request));
    }
    if (request.method === "GET" && path === "/inspect") {
      return this.state.blockConcurrencyWhile(() => this.inspect(request));
    }
    if (request.method === "POST" && path === "/playback") {
      return this.state.blockConcurrencyWhile(() => this.createPlaybackSession(request));
    }
    return Promise.resolve(problem2(404, "Not found."));
  }
  async provision(request) {
    if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", this.env.BROKER_AUTH_KEY)) {
      return problem2(401, "Broker authorization is required.");
    }
    if (!isStreamProviderConfigured(this.env)) return problem2(503, "Stream provisioning is not configured.");
    let input;
    try {
      input = await request.json();
    } catch {
      return problem2(400, "Invalid request.");
    }
    const now = this.env.now?.() ?? Date.now();
    const valid = validateProvisionRequest(
      input,
      now,
      parsePositiveInteger2(this.env.MAX_VIDEO_BYTES),
      parsePositiveInteger2(this.env.MAX_VIDEO_DURATION_SECONDS)
    );
    if (!valid) return problem2(400, "Invalid upload constraints.");
    const fingerprint = await requestFingerprint(input);
    const stored = await this.state.storage.get("provision");
    if (stored) {
      if (stored.fingerprint !== fingerprint) return problem2(409, "Asset upload parameters conflict with its existing reservation.");
      if (stored.state !== "ready" || !stored.encryptedLocation || !stored.expiresAt) {
        return problem2(503, "Upload provisioning has an ambiguous provider state; automatic retry is disabled.");
      }
      if (Date.parse(stored.expiresAt) <= now) return problem2(410, "Upload capability expired.");
      try {
        const uploadUrl = await decryptLocation(stored.encryptedLocation, this.env.TUS_LOCATION_ENCRYPTION_KEY_B64);
        return Response.json({ uploadUrl, expiresAt: stored.expiresAt }, {
          headers: { "Cache-Control": "no-store" }
        });
      } catch {
        return problem2(503, "Stored upload capability cannot be decrypted.");
      }
    }
    const reservation = { assetId: input.assetId.toLowerCase(), fingerprint, state: "provisioning" };
    await this.state.storage.put("provision", reservation);
    try {
      const provisioned = await createTusUploadLocation(input, this.env);
      const encryptedLocation = await encryptLocation(provisioned.location, this.env.TUS_LOCATION_ENCRYPTION_KEY_B64);
      const ready = {
        fingerprint,
        assetId: input.assetId.toLowerCase(),
        state: "ready",
        encryptedLocation,
        streamUid: provisioned.streamUid,
        expiresAt: input.expiresAt
      };
      await this.state.storage.put("provision", ready);
      return Response.json({ uploadUrl: provisioned.location, expiresAt: input.expiresAt }, {
        headers: { "Cache-Control": "no-store" }
      });
    } catch {
      await this.state.storage.put("provision", { assetId: input.assetId.toLowerCase(), fingerprint, state: "ambiguous" });
      return problem2(503, "Upload provisioning failed; automatic retry is disabled to avoid duplicate provider uploads.");
    }
  }
  async inspect(request) {
    if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", this.env.BROKER_AUTH_KEY)) {
      return problem2(401, "Broker authorization is required.");
    }
    if (!isStreamProviderConfigured(this.env)) return problem2(503, "Stream inspection is not configured.");
    const stored = await this.state.storage.get("provision");
    if (!stored || stored.state !== "ready" || !stored.encryptedLocation || !stored.streamUid || !stored.expiresAt) {
      return problem2(404, "Video asset is not available for inspection.");
    }
    try {
      const response = await fetchStreamVideo(stored.streamUid, this.env);
      if (response.status === 404) return problem2(404, "Video asset was not found.");
      if (!response.ok) return problem2(502, "Video inspection is unavailable.");
      const payload = await readProviderJson(response, 32768);
      const result = providerVideoResult(payload);
      if (!result || result.uid !== stored.streamUid || result.creator !== stored.assetId) {
        return problem2(502, "Video inspection returned invalid provider metadata.");
      }
      return Response.json({
        assetId: stored.assetId,
        uid: result.uid,
        state: result.status.state,
        readyToStream: result.readyToStream,
        byteLength: result.size,
        durationSeconds: normalizeDuration(result.duration),
        contentType: result.status.state === "ready" && result.readyToStream ? "video/mp4" : null
      }, { headers: { "Cache-Control": "no-store" } });
    } catch {
      return problem2(502, "Video inspection is unavailable.");
    }
  }
  async createPlaybackSession(request) {
    if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", this.env.BROKER_AUTH_KEY))
      return problem2(401, "Broker authorization is required.");
    if (!isStreamProviderConfigured(this.env)) return problem2(503, "Stream playback is not configured.");
    let input;
    try {
      input = await request.json();
    } catch {
      return problem2(400, "Invalid request.");
    }
    const assetId = typeof input.assetId === "string" ? input.assetId.toLowerCase() : "";
    const exp = Number(input.exp);
    const now = Math.floor((this.env.now?.() ?? Date.now()) / 1e3);
    const maximum = parsePositiveInteger2(this.env.MAX_VIDEO_PLAYBACK_SESSION_SECONDS ?? "");
    if (!/^[0-9a-f]{32}$/iu.test(assetId) || !Number.isSafeInteger(exp) || maximum === null || exp <= now || exp > now + maximum)
      return problem2(400, "Invalid playback expiry.");
    const stored = await this.state.storage.get("provision");
    if (!stored || stored.state !== "ready" || stored.assetId !== assetId || !stored.streamUid)
      return problem2(404, "Video asset is not available for playback.");
    try {
      const endpoint = new URL(`/client/v4/accounts/${encodeURIComponent(this.env.CLOUDFLARE_ACCOUNT_ID)}/stream/${encodeURIComponent(stored.streamUid)}/token`, this.env.STREAM_API_BASE);
      const response = await (this.env.fetcher ?? fetch)(endpoint, {
        method: "POST",
        redirect: "manual",
        headers: { Authorization: `Bearer ${this.env.CLOUDFLARE_STREAM_API_TOKEN}`, "Content-Type": "application/json" },
        body: JSON.stringify({ exp, downloadable: false })
      });
      if (!response.ok) return problem2(502, "Stream playback session could not be created.");
      const payload = await readProviderJson(response, 8192);
      const token = streamToken(payload);
      if (!token) return problem2(502, "Stream returned an invalid playback session.");
      return Response.json({ token, expiresAt: exp }, { headers: { "Cache-Control": "no-store" } });
    } catch {
      return problem2(502, "Stream playback session could not be created.");
    }
  }
};
async function createTusUploadLocation(input, env) {
  const baseUrl = new URL(env.STREAM_API_BASE);
  if (!isStreamProviderConfigured(env)) {
    throw new Error("Invalid Stream API base URL.");
  }
  const uploadMetadata = [
    `maxDurationSeconds ${base64Url(encoder2.encode(String(input.maxDurationSeconds)))}`,
    `expiry ${base64Url(encoder2.encode(input.expiresAt))}`,
    "requiresignedurls",
    `name ${base64Url(encoder2.encode(input.assetId))}`
  ].join(",");
  const endpoint = new URL(`/client/v4/accounts/${encodeURIComponent(env.CLOUDFLARE_ACCOUNT_ID)}/stream?direct_user=true`, baseUrl);
  const response = await (env.fetcher ?? fetch)(endpoint, {
    method: "POST",
    redirect: "manual",
    headers: {
      Authorization: `Bearer ${env.CLOUDFLARE_STREAM_API_TOKEN}`,
      "Tus-Resumable": "1.0.0",
      "Upload-Length": String(input.declaredByteLength),
      "Upload-Metadata": uploadMetadata,
      "Upload-Creator": input.assetId
    }
  });
  if (!response.ok) throw new Error("Stream TUS provisioning returned a non-success status.");
  const location = response.headers.get("Location");
  if (!location) throw new Error("Stream TUS provisioning omitted Location.");
  const streamUid = response.headers.get("stream-media-id");
  if (!streamUid || !/^[0-9a-f]{32}$/iu.test(streamUid)) throw new Error("Stream TUS provisioning omitted media ID.");
  const parsed = new URL(location);
  if (parsed.protocol !== "https:" || parsed.hostname !== "upload.videodelivery.net" || parsed.port !== "" && parsed.port !== "443" || parsed.username || parsed.password) {
    throw new Error("Stream TUS Location was invalid.");
  }
  return { location: parsed.toString(), streamUid: streamUid.toLowerCase() };
}
__name(createTusUploadLocation, "createTusUploadLocation");
async function fetchStreamVideo(uid, env) {
  const endpoint = new URL(`/client/v4/accounts/${encodeURIComponent(env.CLOUDFLARE_ACCOUNT_ID)}/stream/${encodeURIComponent(uid)}`, env.STREAM_API_BASE);
  return (env.fetcher ?? fetch)(endpoint, {
    method: "GET",
    redirect: "manual",
    headers: { Authorization: `Bearer ${env.CLOUDFLARE_STREAM_API_TOKEN}` }
  });
}
__name(fetchStreamVideo, "fetchStreamVideo");
async function readProviderJson(response, maximumBytes) {
  const length = response.headers.get("Content-Length");
  if (length !== null && (!/^\d+$/u.test(length) || Number(length) > maximumBytes)) throw new Error("Provider response too large.");
  if (!response.body) throw new Error("Provider response body missing.");
  const reader = response.body.getReader();
  const chunks = [];
  let total = 0;
  try {
    while (true) {
      const next = await reader.read();
      if (next.done) break;
      total += next.value.byteLength;
      if (total > maximumBytes) {
        await reader.cancel("Provider response too large.").catch(() => void 0);
        throw new Error("Provider response too large.");
      }
      chunks.push(next.value);
    }
  } finally {
    reader.releaseLock();
  }
  const bytes = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return JSON.parse(new TextDecoder().decode(bytes));
}
__name(readProviderJson, "readProviderJson");
function providerVideoResult(value) {
  if (typeof value !== "object" || value === null || !("success" in value) || value.success !== true || !("result" in value) || typeof value.result !== "object" || value.result === null) return null;
  const result = value.result;
  const status = result.status;
  const allowedStates = /* @__PURE__ */ new Set(["pendingupload", "downloading", "queued", "inprogress", "ready", "error", "live-inprogress"]);
  if (typeof result.uid !== "string" || !/^[0-9a-f]{32}$/iu.test(result.uid) || typeof result.creator !== "string" || !/^[0-9a-f]{32}$/iu.test(result.creator) || typeof result.readyToStream !== "boolean" || !Number.isSafeInteger(result.size) || result.size < 0 || !status || typeof status.state !== "string" || !allowedStates.has(status.state) || result.duration !== void 0 && result.duration !== null && typeof result.duration !== "number") return null;
  const duration = typeof result.duration === "number" && Number.isFinite(result.duration) && result.duration >= 0 ? result.duration : null;
  return {
    uid: result.uid.toLowerCase(),
    creator: result.creator.toLowerCase(),
    readyToStream: result.readyToStream,
    duration,
    size: result.size,
    status: { state: status.state }
  };
}
__name(providerVideoResult, "providerVideoResult");
function streamToken(value) {
  if (typeof value !== "object" || value === null || !("success" in value) || value.success !== true || !("result" in value) || typeof value.result !== "object" || value.result === null || !("token" in value.result)) return null;
  const token = value.result.token;
  return typeof token === "string" && token.length > 0 && token.length <= 4096 && !token.includes("/") && !token.includes("\\") && !token.includes("?") && !token.includes("#") && !/[\u0000-\u001f\u007f]/u.test(token) ? token : null;
}
__name(streamToken, "streamToken");
function normalizeDuration(duration) {
  return duration === null ? null : Math.ceil(duration);
}
__name(normalizeDuration, "normalizeDuration");
function validateProvisionRequest(input, now, hardMax, durationHardMax) {
  if (!input || !/^[0-9a-f]{32}$/iu.test(input.assetId) || hardMax === null || durationHardMax === null || !Number.isSafeInteger(input.declaredByteLength) || input.declaredByteLength <= 0 || !Number.isSafeInteger(input.maximumBytes) || input.maximumBytes <= 0 || input.maximumBytes > hardMax || input.declaredByteLength > input.maximumBytes || !Number.isSafeInteger(input.maxDurationSeconds) || input.maxDurationSeconds <= 0 || input.maxDurationSeconds > durationHardMax || typeof input.expiresAt !== "string") {
    return false;
  }
  const expiresAt = Date.parse(input.expiresAt);
  return Number.isFinite(expiresAt) && expiresAt > now && expiresAt <= now + 15 * 6e4 + 3e4;
}
__name(validateProvisionRequest, "validateProvisionRequest");
async function requestFingerprint(input) {
  const canonical = [
    input.assetId.toLowerCase(),
    input.declaredByteLength,
    input.maximumBytes,
    new Date(input.expiresAt).toISOString(),
    input.maxDurationSeconds
  ].join("|");
  const digest = await crypto.subtle.digest("SHA-256", toBufferSource2(encoder2.encode(canonical)));
  return base64Url(new Uint8Array(digest));
}
__name(requestFingerprint, "requestFingerprint");
async function encryptLocation(location, keyValue) {
  const key = await importAesKey(keyValue);
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const ciphertext = await crypto.subtle.encrypt({ name: "AES-GCM", iv: toBufferSource2(iv) }, key, toBufferSource2(encoder2.encode(location)));
  return `${base64Url(iv)}.${base64Url(new Uint8Array(ciphertext))}`;
}
__name(encryptLocation, "encryptLocation");
async function decryptLocation(value, keyValue) {
  const [iv, ciphertext, extra] = value.split(".");
  if (!iv || !ciphertext || extra !== void 0) throw new Error("Invalid encrypted location.");
  const plaintext = await crypto.subtle.decrypt(
    { name: "AES-GCM", iv: toBufferSource2(fromBase64Url(iv)) },
    await importAesKey(keyValue),
    toBufferSource2(fromBase64Url(ciphertext))
  );
  const location = new TextDecoder().decode(plaintext);
  const parsed = new URL(location);
  if (parsed.protocol !== "https:" || parsed.hostname !== "upload.videodelivery.net" || parsed.port !== "" && parsed.port !== "443" || parsed.username || parsed.password) {
    throw new Error("Invalid TUS URL.");
  }
  return location;
}
__name(decryptLocation, "decryptLocation");
async function importAesKey(value) {
  const bytes = fromBase64Url(value);
  if (bytes.byteLength !== 32) throw new Error("Encryption key must be 256 bits.");
  return crypto.subtle.importKey("raw", toBufferSource2(bytes), "AES-GCM", false, ["encrypt", "decrypt"]);
}
__name(importAesKey, "importAesKey");
function toBufferSource2(bytes) {
  return Uint8Array.from(bytes).buffer;
}
__name(toBufferSource2, "toBufferSource");
function constantTimeEqual(left, right) {
  const a = encoder2.encode(left);
  const b = encoder2.encode(right);
  let difference = a.length ^ b.length;
  const length = Math.max(a.length, b.length);
  for (let index = 0; index < length; index++) difference |= (a[index] ?? 0) ^ (b[index] ?? 0);
  return difference === 0;
}
__name(constantTimeEqual, "constantTimeEqual");
function parsePositiveInteger2(value) {
  if (!/^\d+$/u.test(value)) return null;
  const number = Number(value);
  return Number.isSafeInteger(number) && number > 0 ? number : null;
}
__name(parsePositiveInteger2, "parsePositiveInteger");
function problem2(status, title) {
  return Response.json({ title, status }, { status, headers: { "Cache-Control": "no-store" } });
}
__name(problem2, "problem");

// src/index.ts
var ASSET_ID = /^[0-9a-f]{32}$/iu;
var worker = {
  async fetch(request, env) {
    const url = new URL(request.url);
    const deliveryMatch = /^\/v1\/delivery\/images\/([^/]+)$/u.exec(url.pathname);
    if (deliveryMatch) {
      const assetId = deliveryMatch[1].toLowerCase();
      if (!ASSET_ID.test(assetId)) return problem3(404, "Not found.");
      if (request.method !== "GET") return problem3(405, "Method not allowed.");
      return deliverImage(request, assetId, env);
    }
    const imageMatch = /^\/v1\/images\/([^/]+)$/u.exec(url.pathname);
    if (imageMatch) {
      const assetId = imageMatch[1].toLowerCase();
      if (!ASSET_ID.test(assetId)) return problem3(404, "Not found.");
      if (request.method === "GET") return deliverImage(request, assetId, env);
      const cors = corsHeaders(request, env);
      if (request.headers.has("Origin") && !cors) return problem3(403, "Origin is not allowed.");
      if (request.method === "OPTIONS") return cors ? new Response(null, { status: 204, headers: cors }) : problem3(403, "Origin is not allowed.");
      const response = await handleImageUpload(request, assetId, env);
      return addHeaders(response, cors ?? new Headers());
    }
    if (url.pathname === "/v1/stream/playback-sessions") {
      if (request.method !== "POST") return problem3(405, "Method not allowed.");
      if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", env.BROKER_AUTH_KEY))
        return problem3(401, "Broker authorization is required.");
      if (!isStreamProviderConfigured(env)) return problem3(503, "Stream playback is not configured.");
      const body = await readBoundedBody(request, 4096);
      if (body === null) return problem3(413, "Broker request body is too large.");
      let input;
      try {
        input = JSON.parse(new TextDecoder().decode(body));
      } catch {
        return problem3(400, "Invalid request.");
      }
      const assetId = typeof input === "object" && input !== null && "assetId" in input ? String(input.assetId).toLowerCase() : "";
      const exp = typeof input === "object" && input !== null && "exp" in input ? Number(input.exp) : NaN;
      if (!ASSET_ID.test(assetId) || !Number.isSafeInteger(exp)) return problem3(400, "Invalid playback request.");
      const id = env.STREAM_BROKER.idFromName(assetId);
      return env.STREAM_BROKER.get(id).fetch("https://broker.invalid/playback", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-Broker-Authorization": request.headers.get("X-Broker-Authorization") ?? "" },
        body: JSON.stringify({ assetId, exp })
      });
    }
    if (url.pathname === "/v1/stream/upload-capabilities") {
      if (request.method !== "POST") return problem3(405, "Method not allowed.");
      if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", env.BROKER_AUTH_KEY)) {
        return problem3(401, "Broker authorization is required.");
      }
      if (!isStreamProviderConfigured(env)) return problem3(503, "Stream provisioning is not configured.");
      const rawBody = await readBoundedBody(request, 4096);
      if (rawBody === null) return problem3(413, "Broker request body is too large.");
      let input;
      try {
        input = JSON.parse(new TextDecoder().decode(rawBody));
      } catch {
        return problem3(400, "Invalid request.");
      }
      const assetId = typeof input === "object" && input !== null && "assetId" in input ? String(input.assetId).toLowerCase() : "";
      if (!ASSET_ID.test(assetId)) return problem3(400, "Invalid asset identifier.");
      const id = env.STREAM_BROKER.idFromName(assetId);
      return env.STREAM_BROKER.get(id).fetch("https://broker.invalid/provision", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-Broker-Authorization": request.headers.get("X-Broker-Authorization") ?? "" },
        body: JSON.stringify(input)
      });
    }
    const imageInspection = /^\/v1\/provider\/inspections\/images\/([^/]+)$/u.exec(url.pathname);
    const videoInspection = /^\/v1\/provider\/inspections\/videos\/([^/]+)$/u.exec(url.pathname);
    if (imageInspection || videoInspection) {
      if (request.method !== "GET") return problem3(405, "Method not allowed.");
      if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", env.BROKER_AUTH_KEY)) {
        return problem3(401, "Broker authorization is required.");
      }
      if (request.body || request.headers.get("Content-Length") && request.headers.get("Content-Length") !== "0") {
        return problem3(400, "Inspection requests do not accept a body.");
      }
      const assetId = (imageInspection ?? videoInspection)[1].toLowerCase();
      if (!ASSET_ID.test(assetId)) return problem3(404, "Not found.");
      if (imageInspection) return inspectImage(assetId, env);
      if (!isStreamProviderConfigured(env)) return problem3(503, "Stream inspection is not configured.");
      const id = env.STREAM_BROKER.idFromName(assetId);
      return env.STREAM_BROKER.get(id).fetch("https://broker.invalid/inspect", {
        method: "GET",
        headers: { "X-Broker-Authorization": request.headers.get("X-Broker-Authorization") ?? "" }
      });
    }
    return problem3(404, "Not found.");
  }
};
var index_default = worker;
function corsHeaders(request, env) {
  const origin = request.headers.get("Origin");
  if (!origin) return new Headers({ "Cache-Control": "no-store" });
  const allowed = new Set((env.ALLOWED_UPLOAD_ORIGINS ?? "").split(",").map((value) => value.trim()).filter(Boolean));
  if (!allowed.has(origin)) return null;
  return new Headers({
    "Access-Control-Allow-Origin": origin,
    "Access-Control-Allow-Methods": "PUT, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type, X-Media-Capability",
    "Access-Control-Max-Age": "300",
    Vary: "Origin",
    "Cache-Control": "no-store"
  });
}
__name(corsHeaders, "corsHeaders");
async function readBoundedBody(request, maximumBytes) {
  const declaredLength = request.headers.get("Content-Length");
  if (declaredLength !== null && (!/^\d+$/u.test(declaredLength) || Number(declaredLength) > maximumBytes)) {
    await request.body?.cancel("Broker request exceeds its body ceiling.").catch(() => void 0);
    return null;
  }
  if (!request.body) return new Uint8Array();
  const reader = request.body.getReader();
  const chunks = [];
  let total = 0;
  try {
    while (true) {
      const result = await reader.read();
      if (result.done) break;
      total += result.value.byteLength;
      if (total > maximumBytes) {
        await reader.cancel("Broker request exceeds its body ceiling.").catch(() => void 0);
        return null;
      }
      chunks.push(result.value);
    }
  } finally {
    reader.releaseLock();
  }
  const body = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    body.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return body;
}
__name(readBoundedBody, "readBoundedBody");
function addHeaders(response, headers) {
  const merged = new Headers(response.headers);
  headers.forEach((value, key) => merged.set(key, value));
  return new Response(response.body, { status: response.status, statusText: response.statusText, headers: merged });
}
__name(addHeaders, "addHeaders");
function problem3(status, title) {
  return Response.json({ title, status }, { status, headers: { "Cache-Control": "no-store" } });
}
__name(problem3, "problem");
async function deliverImage(request, assetId, env) {
  if (request.body || request.headers.has("Content-Length") && request.headers.get("Content-Length") !== "0")
    return problem3(400, "Image delivery does not accept a body.");
  const token = new URL(request.url).searchParams.get("capability") ?? "";
  const claims = await verifyImageDeliveryCapability(token, assetId, env.CAPABILITY_SIGNING_KEY_B64, env.now?.() ?? Date.now());
  if (!claims) return problem3(404, "Not found.");
  try {
    const object = await env.MEDIA_BUCKET.get(`creator/${assetId}.webp`);
    if (!object || object.customMetadata?.assetId !== assetId || object.customMetadata?.normalized !== "true" || object.httpMetadata?.contentType?.toLowerCase() !== "image/webp" || object.size <= 0) return problem3(404, "Not found.");
    const transformed = await env.IMAGES.input(object.body).output({ format: "image/webp" });
    const image = transformed.response();
    const headers = new Headers(image.headers);
    headers.set("Content-Type", "image/webp");
    headers.set("X-Content-Type-Options", "nosniff");
    headers.set("Cache-Control", `private, max-age=${Math.min(30, Math.max(0, claims.exp - Math.floor((env.now?.() ?? Date.now()) / 1e3)))}, must-revalidate`);
    headers.set("Vary", "Cookie, Authorization");
    return new Response(image.body, { status: image.status, headers });
  } catch {
    return problem3(502, "Image delivery is unavailable.");
  }
}
__name(deliverImage, "deliverImage");
async function verifyImageDeliveryCapability(token, assetId, keyValue, nowMs) {
  const [encoded, suppliedSignature, extra] = token.split(".");
  if (!encoded || !suppliedSignature || extra !== void 0) return null;
  try {
    const key = await crypto.subtle.importKey("raw", toBufferSource3(fromBase64Url(keyValue)), { name: "HMAC", hash: "SHA-256" }, false, ["verify"]);
    const valid = await crypto.subtle.verify("HMAC", key, toBufferSource3(fromBase64Url(suppliedSignature)), toBufferSource3(new TextEncoder().encode(encoded)));
    if (!valid) return null;
    const claims = JSON.parse(new TextDecoder().decode(fromBase64Url(encoded)));
    const now = Math.floor(nowMs / 1e3);
    if (claims.v !== 1 || claims.aud !== "davetiye-image-delivery" || claims.assetId !== assetId || !Number.isSafeInteger(claims.exp) || claims.exp <= now || claims.exp > now + 60) return null;
    return claims;
  } catch {
    return null;
  }
}
__name(verifyImageDeliveryCapability, "verifyImageDeliveryCapability");
function toBufferSource3(bytes) {
  return Uint8Array.from(bytes).buffer;
}
__name(toBufferSource3, "toBufferSource");
async function inspectImage(assetId, env) {
  try {
    const object = await env.MEDIA_BUCKET.head(`creator/${assetId}.webp`);
    if (!object) return problem3(404, "Image asset was not found.");
    const contentType = object.httpMetadata?.contentType ?? null;
    const normalized = object.customMetadata?.assetId === assetId && object.customMetadata?.normalized === "true" && contentType?.toLowerCase() === "image/webp" && object.size > 0;
    return Response.json({
      assetId,
      exists: true,
      normalized,
      contentType,
      byteLength: object.size
    }, { headers: { "Cache-Control": "no-store" } });
  } catch {
    return problem3(502, "Image inspection is unavailable.");
  }
}
__name(inspectImage, "inspectImage");
export {
  StreamUploadBroker,
  index_default as default
};
//# sourceMappingURL=index.js.map
