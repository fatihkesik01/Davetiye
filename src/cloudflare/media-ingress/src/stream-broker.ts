import { base64Url, fromBase64Url } from "./capability.js";
import type { Environment, StoredAssetTombstone, StoredStreamProvision, StreamCapability, StreamProvisionRequest } from "./types.js";

const encoder = new TextEncoder();

export function isBrokerAuthorized(header: string, configuredSecret: string): boolean {
  if (configuredSecret.trim().length < 32) return false;
  return constantTimeEqual(header, `Bearer ${configuredSecret}`);
}

export function isStreamProviderConfigured(env: Environment): boolean {
  try {
    const baseUrl = new URL(env.STREAM_API_BASE);
    const encryptionKey = fromBase64Url(env.TUS_LOCATION_ENCRYPTION_KEY_B64);
    return baseUrl.protocol === "https:" && baseUrl.hostname === "api.cloudflare.com" &&
      (baseUrl.port === "" || baseUrl.port === "443") && !baseUrl.username && !baseUrl.password &&
      baseUrl.pathname === "/" && !baseUrl.search && !baseUrl.hash &&
      /^[0-9a-f]{32}$/iu.test(env.CLOUDFLARE_ACCOUNT_ID) &&
      env.CLOUDFLARE_STREAM_API_TOKEN.trim().length >= 32 && encryptionKey.byteLength === 32;
  } catch {
    return false;
  }
}

export class StreamUploadBroker {
  constructor(private readonly state: DurableObjectState, private readonly env: Environment) {}

  fetch(request: Request): Promise<Response> {
    const path = new URL(request.url).pathname;
    if (request.method === "POST" && path === "/provision") {
      return this.state.blockConcurrencyWhile(() => this.provision(request));
    }
    if (request.method === "POST" && path === "/delete") {
      return this.state.blockConcurrencyWhile(() => this.deleteAsset(request));
    }
    if (request.method === "GET" && path === "/inspect-presence") {
      return this.state.blockConcurrencyWhile(() => this.inspectPresence(request));
    }
    if (request.method === "GET" && path === "/inspect") {
      return this.state.blockConcurrencyWhile(() => this.inspect(request));
    }
    if (request.method === "POST" && path === "/playback") {
      return this.state.blockConcurrencyWhile(() => this.createPlaybackSession(request));
    }
    return Promise.resolve(problem(404, "Not found."));
  }

  private async provision(request: Request): Promise<Response> {
    if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", this.env.BROKER_AUTH_KEY)) {
      return problem(401, "Broker authorization is required.");
    }
    if (!isStreamProviderConfigured(this.env)) return problem(503, "Stream provisioning is not configured.");

    let input: StreamProvisionRequest;
    try {
      input = await request.json() as StreamProvisionRequest;
    } catch {
      return problem(400, "Invalid request.");
    }

    const now = this.env.now?.() ?? Date.now();
    const valid = validateProvisionRequest(input, now, parsePositiveInteger(this.env.MAX_VIDEO_BYTES),
      parsePositiveInteger(this.env.MAX_VIDEO_DURATION_SECONDS));
    if (!valid) return problem(400, "Invalid upload constraints.");
    if (await this.state.storage.get<StoredAssetTombstone>("deleted"))
      return problem(410, "Media asset has been permanently purged.");
    const fingerprint = await requestFingerprint(input);
    const stored = await this.state.storage.get<StoredStreamProvision>("provision");

    if (stored) {
      if (stored.fingerprint !== fingerprint) return problem(409, "Asset upload parameters conflict with its existing reservation.");
      if (stored.state !== "ready" || !stored.encryptedLocation || !stored.expiresAt) {
        return problem(503, "Upload provisioning has an ambiguous provider state; automatic retry is disabled.");
      }
      if (Date.parse(stored.expiresAt) <= now) return problem(410, "Upload capability expired.");
      try {
        const uploadUrl = await decryptLocation(stored.encryptedLocation, this.env.TUS_LOCATION_ENCRYPTION_KEY_B64);
        return Response.json({ uploadUrl, expiresAt: stored.expiresAt } satisfies StreamCapability, {
          headers: { "Cache-Control": "no-store" },
        });
      } catch {
        return problem(503, "Stored upload capability cannot be decrypted.");
      }
    }

    // Durable state is committed before any provider request. A crash or ambiguous network failure
    // leaves this key non-retriable, preventing a duplicate Stream upload resource.
    const reservation: StoredStreamProvision = { assetId: input.assetId.toLowerCase(), fingerprint, state: "provisioning" };
    await this.state.storage.put("provision", reservation);

    try {
      const provisioned = await createTusUploadLocation(input, this.env);
      const encryptedLocation = await encryptLocation(provisioned.location, this.env.TUS_LOCATION_ENCRYPTION_KEY_B64);
      const ready: StoredStreamProvision = {
        fingerprint,
        assetId: input.assetId.toLowerCase(),
        state: "ready",
        encryptedLocation,
        streamUid: provisioned.streamUid,
        expiresAt: input.expiresAt,
      };
      await this.state.storage.put("provision", ready);
      return Response.json({ uploadUrl: provisioned.location, expiresAt: input.expiresAt } satisfies StreamCapability, {
        headers: { "Cache-Control": "no-store" },
      });
    } catch {
      await this.state.storage.put("provision", { assetId: input.assetId.toLowerCase(), fingerprint, state: "ambiguous" } satisfies StoredStreamProvision);
      return problem(503, "Upload provisioning failed; automatic retry is disabled to avoid duplicate provider uploads.");
    }
  }

  private async deleteAsset(request: Request): Promise<Response> {
    if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", this.env.BROKER_AUTH_KEY))
      return problem(401, "Broker authorization is required.");
    if (request.body || request.headers.has("Content-Length") && request.headers.get("Content-Length") !== "0")
      return problem(400, "Deletion requests do not accept a body.");
    const assetId = new URL(request.url).searchParams.get("assetId") ?? "";
    if (!/^[0-9a-f]{32}$/iu.test(assetId)) return problem(404, "Not found.");
    const normalizedAssetId = assetId.toLowerCase();
    await this.state.storage.put("deleted", {
      assetId: normalizedAssetId,
      deletedAt: new Date(this.env.now?.() ?? Date.now()).toISOString(),
    } satisfies StoredAssetTombstone);

    try {
      // The same R2 key acts as a durable write fence. Image ingress uses create-only put: if
      // upload commits first this marker replaces its bytes; if this marker wins first the late
      // upload cannot overwrite it. Unlike a DO lock, this remains safe for slow streaming PUTs.
      const objectKey = `creator/${normalizedAssetId}.webp`;
      const previousImage = await this.env.MEDIA_BUCKET.head(objectKey);
      const imageWasPresent = previousImage !== null && previousImage.customMetadata?.tombstone !== "true";
      await this.env.MEDIA_BUCKET.put(objectKey, new Uint8Array(), {
        httpMetadata: { contentType: "application/x-davetiye-tombstone", cacheControl: "private, no-store" },
        customMetadata: { assetId: normalizedAssetId, tombstone: "true" },
      });
      if (!isStreamProviderConfigured(this.env)) return problem(503, "Media deletion is not configured.");
      const streamWasPresent = await deleteStreamByAssetId(normalizedAssetId, this.env,
        await this.state.storage.get<StoredStreamProvision>("provision"));
      return new Response(null, { status: 204, headers: {
        "Cache-Control": "no-store",
        "X-Media-Deletion-Result": imageWasPresent || streamWasPresent ? "deleted" : "already-absent",
      } });
    } catch {
      return problem(502, "Media provider deletion is unavailable.");
    }
  }

  private async inspectPresence(request: Request): Promise<Response> {
    if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", this.env.BROKER_AUTH_KEY))
      return problem(401, "Broker authorization is required.");
    const assetId = new URL(request.url).searchParams.get("assetId") ?? "";
    if (!/^[0-9a-f]{32}$/iu.test(assetId)) return problem(404, "Not found.");
    if (await this.isDeleted(assetId)) return problem(404, "Not found.");
    if (!isStreamProviderConfigured(this.env)) return problem(503, "Stream inspection is not configured.");
    try {
      const videos = await listStreamVideosByCreator(assetId.toLowerCase(), this.env);
      return Response.json({ assetId: assetId.toLowerCase(), exists: videos.length > 0,
        valid: videos.some(video => video.status.state === "ready" && video.readyToStream) },
      { headers: { "Cache-Control": "no-store" } });
    } catch { return problem(502, "Stream inspection is unavailable."); }
  }

  private async inspect(request: Request): Promise<Response> {
    if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", this.env.BROKER_AUTH_KEY)) {
      return problem(401, "Broker authorization is required.");
    }
    const persistedProvision = await this.state.storage.get<StoredStreamProvision>("provision");
    if (persistedProvision && await this.isDeleted(persistedProvision.assetId)) return problem(404, "Not found.");
    if (!isStreamProviderConfigured(this.env)) return problem(503, "Stream inspection is not configured.");

    const stored = persistedProvision;
    if (!stored || stored.state !== "ready" || !stored.encryptedLocation || !stored.streamUid || !stored.expiresAt) {
      return problem(404, "Video asset is not available for inspection.");
    }

    try {
      const response = await fetchStreamVideo(stored.streamUid, this.env);
      if (response.status === 404) return problem(404, "Video asset was not found.");
      if (!response.ok) return problem(502, "Video inspection is unavailable.");
      const payload = await readProviderJson(response, 32_768);
      const result = providerVideoResult(payload);
      if (!result || result.uid !== stored.streamUid || result.creator !== stored.assetId) {
        return problem(502, "Video inspection returned invalid provider metadata.");
      }
      return Response.json({
        assetId: stored.assetId,
        uid: result.uid,
        state: result.status.state,
        readyToStream: result.readyToStream,
        byteLength: result.size,
        durationSeconds: normalizeDuration(result.duration),
        contentType: result.status.state === "ready" && result.readyToStream ? "video/mp4" : null,
      }, { headers: { "Cache-Control": "no-store" } });
    } catch {
      return problem(502, "Video inspection is unavailable.");
    }
  }

  private async createPlaybackSession(request: Request): Promise<Response> {
    if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", this.env.BROKER_AUTH_KEY))
      return problem(401, "Broker authorization is required.");
    if (!isStreamProviderConfigured(this.env)) return problem(503, "Stream playback is not configured.");
    let input: { assetId?: unknown; exp?: unknown };
    try { input = await request.json() as { assetId?: unknown; exp?: unknown }; } catch { return problem(400, "Invalid request."); }
    const assetId = typeof input.assetId === "string" ? input.assetId.toLowerCase() : "";
    const exp = Number(input.exp);
    const now = Math.floor((this.env.now?.() ?? Date.now()) / 1000);
    const maximum = parsePositiveInteger(this.env.MAX_VIDEO_PLAYBACK_SESSION_SECONDS ?? "");
    if (!/^[0-9a-f]{32}$/iu.test(assetId) || !Number.isSafeInteger(exp) || maximum === null || exp <= now || exp > now + maximum)
      return problem(400, "Invalid playback expiry.");
    if (await this.isDeleted(assetId)) return problem(404, "Not found.");
    const stored = await this.state.storage.get<StoredStreamProvision>("provision");
    if (!stored || stored.state !== "ready" || stored.assetId !== assetId || !stored.streamUid)
      return problem(404, "Video asset is not available for playback.");
    try {
      const endpoint = new URL(`/client/v4/accounts/${encodeURIComponent(this.env.CLOUDFLARE_ACCOUNT_ID)}/stream/${encodeURIComponent(stored.streamUid)}/token`, this.env.STREAM_API_BASE);
      const response = await (this.env.fetcher ?? fetch)(endpoint, {
        method: "POST", redirect: "manual",
        headers: { Authorization: `Bearer ${this.env.CLOUDFLARE_STREAM_API_TOKEN}`, "Content-Type": "application/json" },
        body: JSON.stringify({ exp, downloadable: false }),
      });
      if (!response.ok) return problem(502, "Stream playback session could not be created.");
      const payload = await readProviderJson(response, 8192);
      const token = streamToken(payload);
      if (!token) return problem(502, "Stream returned an invalid playback session.");
      return Response.json({ token, expiresAt: exp }, { headers: { "Cache-Control": "no-store" } });
    } catch { return problem(502, "Stream playback session could not be created."); }
  }

  private async isDeleted(assetId: string): Promise<boolean> {
    const tombstone = await this.state.storage.get<StoredAssetTombstone>("deleted");
    return tombstone?.assetId === assetId.toLowerCase();
  }

}

async function createTusUploadLocation(input: StreamProvisionRequest, env: Environment): Promise<{ location: string; streamUid: string }> {
  const baseUrl = new URL(env.STREAM_API_BASE);
  if (!isStreamProviderConfigured(env)) {
    throw new Error("Invalid Stream API base URL.");
  }

  const uploadMetadata = [
    `maxDurationSeconds ${base64Url(encoder.encode(String(input.maxDurationSeconds)))}`,
    `expiry ${base64Url(encoder.encode(input.expiresAt))}`,
    "requiresignedurls",
    `name ${base64Url(encoder.encode(input.assetId))}`,
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
      "Upload-Creator": input.assetId,
    },
  });
  if (!response.ok) throw new Error("Stream TUS provisioning returned a non-success status.");

  const location = response.headers.get("Location");
  if (!location) throw new Error("Stream TUS provisioning omitted Location.");
  const streamUid = response.headers.get("stream-media-id");
  if (!streamUid || !/^[0-9a-f]{32}$/iu.test(streamUid)) throw new Error("Stream TUS provisioning omitted media ID.");
  const parsed = new URL(location);
  if (parsed.protocol !== "https:" || parsed.hostname !== "upload.videodelivery.net" ||
      (parsed.port !== "" && parsed.port !== "443") || parsed.username || parsed.password) {
    throw new Error("Stream TUS Location was invalid.");
  }
  return { location: parsed.toString(), streamUid: streamUid.toLowerCase() };
}

async function fetchStreamVideo(uid: string, env: Environment): Promise<Response> {
  const endpoint = new URL(`/client/v4/accounts/${encodeURIComponent(env.CLOUDFLARE_ACCOUNT_ID)}/stream/${encodeURIComponent(uid)}`, env.STREAM_API_BASE);
  return (env.fetcher ?? fetch)(endpoint, {
    method: "GET",
    redirect: "manual",
    headers: { Authorization: `Bearer ${env.CLOUDFLARE_STREAM_API_TOKEN}` },
  });
}

async function listStreamVideosByCreator(assetId: string, env: Environment): Promise<ProviderVideoResult[]> {
  const endpoint = new URL(`/client/v4/accounts/${encodeURIComponent(env.CLOUDFLARE_ACCOUNT_ID)}/stream`, env.STREAM_API_BASE);
  endpoint.searchParams.set("creator", assetId);
  endpoint.searchParams.set("limit", "1000");
  const response = await (env.fetcher ?? fetch)(endpoint, {
    method: "GET", redirect: "manual",
    headers: { Authorization: `Bearer ${env.CLOUDFLARE_STREAM_API_TOKEN}` },
  });
  if (response.status === 404) return [];
  if (!response.ok) throw new Error("Stream asset reconciliation failed.");
  const payload = await readProviderJson(response, 1024 * 1024);
  if (typeof payload !== "object" || payload === null || !("success" in payload) || payload.success !== true ||
      !("result" in payload) || !Array.isArray(payload.result))
    throw new Error("Stream asset reconciliation response was invalid.");
  const videos: ProviderVideoResult[] = [];
  for (const item of payload.result) {
    if (typeof item !== "object" || item === null || !("creator" in item) || typeof item.creator !== "string" ||
        item.creator.toLowerCase() !== assetId) continue;
    const video = providerVideoResult({ success: true, result: item });
    if (!video) throw new Error("Stream asset reconciliation response contained invalid metadata.");
    videos.push(video);
  }
  return videos;
}

async function deleteStreamByAssetId(assetId: string, env: Environment,
  stored: StoredStreamProvision | undefined): Promise<boolean> {
  // The Stream creator tag is server-set to the immutable application asset id. Listing by that
  // tag recovers uploads whose create response was lost before the Durable Object stored its UID.
  const videos = await listStreamVideosByCreator(assetId, env);
  const videoUids = new Set(videos.filter(video => video.creator === assetId).map(video => video.uid));
  if (stored?.streamUid && /^[0-9a-f]{32}$/iu.test(stored.streamUid)) videoUids.add(stored.streamUid);
  let deleted = false;
  for (const videoUid of videoUids) {
    const endpoint = new URL(`/client/v4/accounts/${encodeURIComponent(env.CLOUDFLARE_ACCOUNT_ID)}/stream/${encodeURIComponent(videoUid)}`,
      env.STREAM_API_BASE);
    const response = await (env.fetcher ?? fetch)(endpoint, {
      method: "DELETE", redirect: "manual",
      headers: { Authorization: `Bearer ${env.CLOUDFLARE_STREAM_API_TOKEN}` },
    });
    if (!response.ok && response.status !== 404) throw new Error("Stream deletion returned a non-success status.");
    if (response.status !== 404) deleted = true;
  }
  return deleted;
}

async function readProviderJson(response: Response, maximumBytes: number): Promise<unknown> {
  const length = response.headers.get("Content-Length");
  if (length !== null && (!/^\d+$/u.test(length) || Number(length) > maximumBytes)) throw new Error("Provider response too large.");
  if (!response.body) throw new Error("Provider response body missing.");
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let total = 0;
  try {
    while (true) {
      const next = await reader.read();
      if (next.done) break;
      total += next.value.byteLength;
      if (total > maximumBytes) {
        await reader.cancel("Provider response too large.").catch(() => undefined);
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

interface ProviderVideoResult {
  uid: string;
  creator: string;
  readyToStream: boolean;
  duration: number | null;
  size: number;
  status: { state: "pendingupload" | "downloading" | "queued" | "inprogress" | "ready" | "error" | "live-inprogress" };
}

function providerVideoResult(value: unknown): ProviderVideoResult | null {
  if (typeof value !== "object" || value === null || !("success" in value) || value.success !== true ||
      !("result" in value) || typeof value.result !== "object" || value.result === null) return null;
  const result = value.result as Record<string, unknown>;
  const status = result.status as Record<string, unknown> | null;
  const allowedStates = new Set(["pendingupload", "downloading", "queued", "inprogress", "ready", "error", "live-inprogress"]);
  if (typeof result.uid !== "string" || !/^[0-9a-f]{32}$/iu.test(result.uid) ||
      typeof result.creator !== "string" || !/^[0-9a-f]{32}$/iu.test(result.creator) ||
      typeof result.readyToStream !== "boolean" || !Number.isSafeInteger(result.size) || (result.size as number) < 0 ||
      !status || typeof status.state !== "string" || !allowedStates.has(status.state) ||
      (result.duration !== undefined && result.duration !== null && typeof result.duration !== "number")) return null;
  const duration = typeof result.duration === "number" && Number.isFinite(result.duration) && result.duration >= 0
    ? result.duration : null;
  return {
    uid: result.uid.toLowerCase(),
    creator: result.creator.toLowerCase(),
    readyToStream: result.readyToStream,
    duration,
    size: result.size as number,
    status: { state: status.state as ProviderVideoResult["status"]["state"] },
  };
}

function streamToken(value: unknown): string | null {
  if (typeof value !== "object" || value === null || !("success" in value) || value.success !== true ||
      !("result" in value) || typeof value.result !== "object" || value.result === null || !("token" in value.result)) return null;
  const token = (value.result as { token?: unknown }).token;
  return typeof token === "string" && token.length > 0 && token.length <= 4096 && !token.includes("/") && !token.includes("\\") &&
    !token.includes("?") && !token.includes("#") && !/[\u0000-\u001f\u007f]/u.test(token) ? token : null;
}

function normalizeDuration(duration: number | null): number | null {
  return duration === null ? null : Math.ceil(duration);
}

function validateProvisionRequest(input: StreamProvisionRequest, now: number, hardMax: number | null, durationHardMax: number | null): boolean {
  if (!input || !/^[0-9a-f]{32}$/iu.test(input.assetId) || hardMax === null || durationHardMax === null ||
      !Number.isSafeInteger(input.declaredByteLength) || input.declaredByteLength <= 0 ||
      !Number.isSafeInteger(input.maximumBytes) || input.maximumBytes <= 0 || input.maximumBytes > hardMax ||
      input.declaredByteLength > input.maximumBytes || !Number.isSafeInteger(input.maxDurationSeconds) ||
      input.maxDurationSeconds <= 0 || input.maxDurationSeconds > durationHardMax || typeof input.expiresAt !== "string") {
    return false;
  }
  const expiresAt = Date.parse(input.expiresAt);
  return Number.isFinite(expiresAt) && expiresAt > now && expiresAt <= now + 15 * 60_000 + 30_000;
}

async function requestFingerprint(input: StreamProvisionRequest): Promise<string> {
  const canonical = [input.assetId.toLowerCase(), input.declaredByteLength, input.maximumBytes,
    new Date(input.expiresAt).toISOString(), input.maxDurationSeconds].join("|");
  const digest = await crypto.subtle.digest("SHA-256", toBufferSource(encoder.encode(canonical)));
  return base64Url(new Uint8Array(digest));
}

async function encryptLocation(location: string, keyValue: string): Promise<string> {
  const key = await importAesKey(keyValue);
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const ciphertext = await crypto.subtle.encrypt({ name: "AES-GCM", iv: toBufferSource(iv) }, key, toBufferSource(encoder.encode(location)));
  return `${base64Url(iv)}.${base64Url(new Uint8Array(ciphertext))}`;
}

async function decryptLocation(value: string, keyValue: string): Promise<string> {
  const [iv, ciphertext, extra] = value.split(".");
  if (!iv || !ciphertext || extra !== undefined) throw new Error("Invalid encrypted location.");
  const plaintext = await crypto.subtle.decrypt({ name: "AES-GCM", iv: toBufferSource(fromBase64Url(iv)) },
    await importAesKey(keyValue), toBufferSource(fromBase64Url(ciphertext)));
  const location = new TextDecoder().decode(plaintext);
  const parsed = new URL(location);
  if (parsed.protocol !== "https:" || parsed.hostname !== "upload.videodelivery.net" ||
      (parsed.port !== "" && parsed.port !== "443") || parsed.username || parsed.password) {
    throw new Error("Invalid TUS URL.");
  }
  return location;
}

async function importAesKey(value: string): Promise<CryptoKey> {
  const bytes = fromBase64Url(value);
  if (bytes.byteLength !== 32) throw new Error("Encryption key must be 256 bits.");
  return crypto.subtle.importKey("raw", toBufferSource(bytes), "AES-GCM", false, ["encrypt", "decrypt"]);
}

function toBufferSource(bytes: Uint8Array): ArrayBuffer {
  return Uint8Array.from(bytes).buffer;
}

function constantTimeEqual(left: string, right: string): boolean {
  const a = encoder.encode(left);
  const b = encoder.encode(right);
  let difference = a.length ^ b.length;
  const length = Math.max(a.length, b.length);
  for (let index = 0; index < length; index++) difference |= (a[index] ?? 0) ^ (b[index] ?? 0);
  return difference === 0;
}

function parsePositiveInteger(value: string): number | null {
  if (!/^\d+$/u.test(value)) return null;
  const number = Number(value);
  return Number.isSafeInteger(number) && number > 0 ? number : null;
}

function problem(status: number, title: string): Response {
  return Response.json({ title, status }, { status, headers: { "Cache-Control": "no-store" } });
}
