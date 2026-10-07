import { handleImageUpload } from "./image-ingress.js";
import { isBrokerAuthorized, isStreamProviderConfigured, StreamUploadBroker } from "./stream-broker.js";
import { fromBase64Url } from "./capability.js";
import type { Environment } from "./types.js";

export { StreamUploadBroker };

const ASSET_ID = /^[0-9a-f]{32}$/iu;

const worker = {
  async fetch(request: Request, env: Environment): Promise<Response> {
    const url = new URL(request.url);
    const deliveryMatch = /^\/v1\/delivery\/images\/([^/]+)$/u.exec(url.pathname);
    if (deliveryMatch) {
      const assetId = deliveryMatch[1].toLowerCase();
      if (!ASSET_ID.test(assetId)) return problem(404, "Not found.");
      if (request.method !== "GET") return problem(405, "Method not allowed.");
      return deliverImage(request, assetId, env);
    }
    const imageMatch = /^\/v1\/images\/([^/]+)$/u.exec(url.pathname);
    if (imageMatch) {
      const assetId = imageMatch[1].toLowerCase();
      if (!ASSET_ID.test(assetId)) return problem(404, "Not found.");
      if (request.method === "GET") return deliverImage(request, assetId, env);
      const cors = corsHeaders(request, env);
      if (request.headers.has("Origin") && !cors) return problem(403, "Origin is not allowed.");
      if (request.method === "OPTIONS") return cors ? new Response(null, { status: 204, headers: cors }) : problem(403, "Origin is not allowed.");
      if (request.method !== "PUT") {
        const response = await handleImageUpload(request, assetId, env);
        return addHeaders(response, cors ?? new Headers());
      }
      const response = await handleImageUpload(request, assetId, env);
      return addHeaders(response, cors ?? new Headers());
    }

    const assetDelete = /^\/v1\/provider\/assets\/([^/]+)\/delete$/u.exec(url.pathname);
    if (assetDelete) {
      const assetId = assetDelete[1].toLowerCase();
      if (!ASSET_ID.test(assetId)) return problem(404, "Not found.");
      if (request.method !== "POST") return problem(405, "Method not allowed.");
      if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", env.BROKER_AUTH_KEY))
        return problem(401, "Broker authorization is required.");
      if (request.body || request.headers.has("Content-Length") && request.headers.get("Content-Length") !== "0")
        return problem(400, "Deletion requests do not accept a body.");
      return routeAssetRequest(request, assetId, env, "/delete");
    }

    const assetInspection = /^\/v1\/provider\/assets\/([^/]+)\/inspection$/u.exec(url.pathname);
    if (assetInspection) {
      const assetId = assetInspection[1].toLowerCase();
      if (!ASSET_ID.test(assetId)) return problem(404, "Not found.");
      if (request.method !== "GET") return problem(405, "Method not allowed.");
      if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", env.BROKER_AUTH_KEY))
        return problem(401, "Broker authorization is required.");
      if (request.body || request.headers.has("Content-Length") && request.headers.get("Content-Length") !== "0")
        return problem(400, "Inspection requests do not accept a body.");
      const kind = url.searchParams.get("kind");
      if (kind === "image") {
        try {
          const object = await env.MEDIA_BUCKET.head(`creator/${assetId}.webp`);
          const exists = object !== null;
          const valid = Boolean(object && object.customMetadata?.assetId === assetId &&
            object.customMetadata?.normalized === "true" && object.httpMetadata?.contentType?.toLowerCase() === "image/webp" && object.size > 0);
          return Response.json({ assetId, exists, valid }, { headers: { "Cache-Control": "no-store" } });
        } catch { return problem(502, "Image inspection is unavailable."); }
      }
      if (kind !== "video") return problem(400, "A supported media kind is required.");
      return routeAssetRequest(request, assetId, env, "/inspect-presence");
    }

    if (url.pathname === "/v1/stream/playback-sessions") {
      if (request.method !== "POST") return problem(405, "Method not allowed.");
      if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", env.BROKER_AUTH_KEY))
        return problem(401, "Broker authorization is required.");
      if (!isStreamProviderConfigured(env)) return problem(503, "Stream playback is not configured.");
      const body = await readBoundedBody(request, 4096);
      if (body === null) return problem(413, "Broker request body is too large.");
      let input: unknown;
      try { input = JSON.parse(new TextDecoder().decode(body)); } catch { return problem(400, "Invalid request."); }
      const assetId = typeof input === "object" && input !== null && "assetId" in input
        ? String((input as { assetId: unknown }).assetId).toLowerCase() : "";
      const exp = typeof input === "object" && input !== null && "exp" in input
        ? Number((input as { exp: unknown }).exp) : NaN;
      if (!ASSET_ID.test(assetId) || !Number.isSafeInteger(exp)) return problem(400, "Invalid playback request.");
      const id = env.STREAM_BROKER.idFromName(assetId);
      return env.STREAM_BROKER.get(id).fetch("https://broker.invalid/playback", {
        method: "POST", headers: { "Content-Type": "application/json", "X-Broker-Authorization": request.headers.get("X-Broker-Authorization") ?? "" },
        body: JSON.stringify({ assetId, exp }),
      });
    }

    if (url.pathname === "/v1/stream/upload-capabilities") {
      if (request.method !== "POST") return problem(405, "Method not allowed.");
      if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", env.BROKER_AUTH_KEY)) {
        return problem(401, "Broker authorization is required.");
      }
      if (!isStreamProviderConfigured(env)) return problem(503, "Stream provisioning is not configured.");
      const rawBody = await readBoundedBody(request, 4096);
      if (rawBody === null) return problem(413, "Broker request body is too large.");
      let input: unknown;
      try {
        input = JSON.parse(new TextDecoder().decode(rawBody));
      } catch {
        return problem(400, "Invalid request.");
      }
      const assetId = typeof input === "object" && input !== null && "assetId" in input
        ? String((input as { assetId: unknown }).assetId).toLowerCase() : "";
      if (!ASSET_ID.test(assetId)) return problem(400, "Invalid asset identifier.");
      const id = env.STREAM_BROKER.idFromName(assetId);
      return env.STREAM_BROKER.get(id).fetch("https://broker.invalid/provision", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-Broker-Authorization": request.headers.get("X-Broker-Authorization") ?? "" },
        body: JSON.stringify(input),
      });
    }

    const imageInspection = /^\/v1\/provider\/inspections\/images\/([^/]+)$/u.exec(url.pathname);
    const videoInspection = /^\/v1\/provider\/inspections\/videos\/([^/]+)$/u.exec(url.pathname);
    if (imageInspection || videoInspection) {
      if (request.method !== "GET") return problem(405, "Method not allowed.");
      if (!isBrokerAuthorized(request.headers.get("X-Broker-Authorization") ?? "", env.BROKER_AUTH_KEY)) {
        return problem(401, "Broker authorization is required.");
      }
      if (request.body || (request.headers.get("Content-Length") && request.headers.get("Content-Length") !== "0")) {
        return problem(400, "Inspection requests do not accept a body.");
      }
      const assetId = (imageInspection ?? videoInspection)![1].toLowerCase();
      if (!ASSET_ID.test(assetId)) return problem(404, "Not found.");
      if (imageInspection) return inspectImage(assetId, env);
      if (!isStreamProviderConfigured(env)) return problem(503, "Stream inspection is not configured.");
      const id = env.STREAM_BROKER.idFromName(assetId);
      return env.STREAM_BROKER.get(id).fetch("https://broker.invalid/inspect", {
        method: "GET",
        headers: { "X-Broker-Authorization": request.headers.get("X-Broker-Authorization") ?? "" },
      });
    }

    return problem(404, "Not found.");
  },
};

function routeAssetRequest(request: Request, assetId: string, env: Environment, path: string): Promise<Response> {
  const id = env.STREAM_BROKER.idFromName(assetId);
  const url = `https://broker.invalid${path}?assetId=${encodeURIComponent(assetId)}`;
  const forwarded = new Request(url, {
    method: request.method,
    headers: request.headers,
    ...(request.body ? { body: request.body, duplex: "half" } : {}),
  } as RequestInit & { duplex?: "half" });
  return env.STREAM_BROKER.get(id).fetch(forwarded);
}

export default worker;

function corsHeaders(request: Request, env: Environment): Headers | null {
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
    "Cache-Control": "no-store",
  });
}

async function readBoundedBody(request: Request, maximumBytes: number): Promise<Uint8Array | null> {
  const declaredLength = request.headers.get("Content-Length");
  if (declaredLength !== null && (!/^\d+$/u.test(declaredLength) || Number(declaredLength) > maximumBytes)) {
    await request.body?.cancel("Broker request exceeds its body ceiling.").catch(() => undefined);
    return null;
  }
  if (!request.body) return new Uint8Array();
  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let total = 0;
  try {
    while (true) {
      const result = await reader.read();
      if (result.done) break;
      total += result.value.byteLength;
      if (total > maximumBytes) {
        await reader.cancel("Broker request exceeds its body ceiling.").catch(() => undefined);
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

function addHeaders(response: Response, headers: Headers): Response {
  const merged = new Headers(response.headers);
  headers.forEach((value, key) => merged.set(key, value));
  return new Response(response.body, { status: response.status, statusText: response.statusText, headers: merged });
}

function problem(status: number, title: string): Response {
  return Response.json({ title, status }, { status, headers: { "Cache-Control": "no-store" } });
}

async function deliverImage(request: Request, assetId: string, env: Environment): Promise<Response> {
  if (request.body || request.headers.has("Content-Length") && request.headers.get("Content-Length") !== "0")
    return problem(400, "Image delivery does not accept a body.");
  const token = new URL(request.url).searchParams.get("capability") ?? "";
  const claims = await verifyImageDeliveryCapability(token, assetId, env.CAPABILITY_SIGNING_KEY_B64, env.now?.() ?? Date.now());
  if (!claims) return problem(404, "Not found.");
  try {
    const object = await env.MEDIA_BUCKET.get(`creator/${assetId}.webp`);
    if (!object || object.customMetadata?.assetId !== assetId || object.customMetadata?.normalized !== "true" ||
        object.httpMetadata?.contentType?.toLowerCase() !== "image/webp" || object.size <= 0) return problem(404, "Not found.");
    const transformed = await env.IMAGES.input(object.body).output({ format: "image/webp" });
    const image = transformed.response();
    const headers = new Headers(image.headers);
    headers.set("Content-Type", "image/webp");
    headers.set("X-Content-Type-Options", "nosniff");
    headers.set("Referrer-Policy", "no-referrer");
    headers.set("Cache-Control", `private, max-age=${Math.min(30, Math.max(0, claims.exp - Math.floor((env.now?.() ?? Date.now()) / 1000)))}, must-revalidate`);
    headers.set("Vary", "Cookie, Authorization");
    return new Response(image.body, { status: image.status, headers });
  } catch { return problem(502, "Image delivery is unavailable."); }
}

interface ImageDeliveryClaims { v: 1; aud: "davetiye-image-delivery"; assetId: string; exp: number }
async function verifyImageDeliveryCapability(token: string, assetId: string, keyValue: string, nowMs: number): Promise<ImageDeliveryClaims | null> {
  const [encoded, suppliedSignature, extra] = token.split(".");
  if (!encoded || !suppliedSignature || extra !== undefined) return null;
  try {
    const key = await crypto.subtle.importKey("raw", toBufferSource(fromBase64Url(keyValue)), { name: "HMAC", hash: "SHA-256" }, false, ["verify"]);
    const valid = await crypto.subtle.verify("HMAC", key, toBufferSource(fromBase64Url(suppliedSignature)), toBufferSource(new TextEncoder().encode(encoded)));
    if (!valid) return null;
    const claims = JSON.parse(new TextDecoder().decode(fromBase64Url(encoded))) as ImageDeliveryClaims;
    const now = Math.floor(nowMs / 1000);
    if (claims.v !== 1 || claims.aud !== "davetiye-image-delivery" || claims.assetId !== assetId ||
        !Number.isSafeInteger(claims.exp) || claims.exp <= now || claims.exp > now + 60) return null;
    return claims;
  } catch { return null; }
}

function toBufferSource(bytes: Uint8Array): ArrayBuffer { return Uint8Array.from(bytes).buffer; }

async function inspectImage(assetId: string, env: Environment): Promise<Response> {
  try {
    const object = await env.MEDIA_BUCKET.head(`creator/${assetId}.webp`);
    if (!object) return problem(404, "Image asset was not found.");
    const contentType = object.httpMetadata?.contentType ?? null;
    const normalized = object.customMetadata?.assetId === assetId && object.customMetadata?.normalized === "true" &&
      contentType?.toLowerCase() === "image/webp" && object.size > 0;
    return Response.json({
      assetId,
      exists: true,
      normalized,
      contentType,
      byteLength: object.size,
    }, { headers: { "Cache-Control": "no-store" } });
  } catch {
    return problem(502, "Image inspection is unavailable.");
  }
}
