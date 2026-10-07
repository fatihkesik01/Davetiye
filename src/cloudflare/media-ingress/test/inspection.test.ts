import { describe, expect, it } from "vitest";
import worker, { StreamUploadBroker } from "../src/index.js";
import type { Environment, StoredStreamProvision } from "../src/types.js";

const assetId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
const uid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
const brokerSecret = "test-broker-key-with-enough-entropy-123456";

describe("private provider inspection", () => {
  it("returns only normalized R2 HEAD evidence for the requested server-generated asset key", async () => {
    const bucket = new InspectionBucket({
      size: 4321,
      httpMetadata: { contentType: "image/webp" },
      customMetadata: { assetId, normalized: "true" },
    });
    const env = environment(bucket);
    const response = await worker.fetch(inspectionRequest("images"), env);
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ assetId, exists: true, normalized: true, contentType: "image/webp", byteLength: 4321 });
    expect(bucket.headKey).toBe(`creator/${assetId}.webp`);
    expect(bucket.getCalls).toBe(0);
  });

  it("does not call provider storage before authenticating inspection requests", async () => {
    const bucket = new InspectionBucket({ size: 30, httpMetadata: { contentType: "image/webp" }, customMetadata: {} });
    const env = environment(bucket);
    const response = await worker.fetch(new Request(`https://media.test/v1/provider/inspections/images/${assetId}`), env);
    expect(response.status).toBe(401);
    expect(bucket.headKey).toBeNull();
  });

  it("returns 404 when an image object is absent", async () => {
    const response = await worker.fetch(inspectionRequest("images"), environment(new InspectionBucket(null)));
    expect(response.status).toBe(404);
  });

  it("resolves Stream UID from persisted response header and returns provider metadata only", async () => {
    const state = new InspectionState();
    await state.storage.put("provision", {
      assetId,
      fingerprint: "fingerprint",
      state: "ready",
      encryptedLocation: "not-returned-by-inspection",
      streamUid: uid,
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
    } satisfies StoredStreamProvision);
    const seen: string[] = [];
    const env = environment(new InspectionBucket(null), async (input, init) => {
      seen.push(`${String(input)} ${init?.method ?? ""} ${new Headers(init?.headers).get("Authorization")}`);
      return Response.json({
        success: true,
        result: {
          uid,
          creator: assetId,
          readyToStream: true,
          duration: 3.25,
          size: 9_876,
          status: { state: "ready" },
          playback: { hls: "must-not-escape" },
        },
      });
    });
    env.STREAM_BROKER = namespace(new StreamUploadBroker(state as unknown as DurableObjectState, env));

    const response = await worker.fetch(inspectionRequest("videos"), env);
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({
      assetId, uid, state: "ready", readyToStream: true, byteLength: 9_876, durationSeconds: 4, contentType: "video/mp4",
    });
    expect(seen).toEqual([`https://api.cloudflare.com/client/v4/accounts/11111111111111111111111111111111/stream/${uid} GET Bearer stream-token-material-with-at-least-32-characters`]);
  });

  it("returns no content type until Stream confirms both ready state and playback", async () => {
    const state = stateWithProvision();
    const env = environment(new InspectionBucket(null), async () => Response.json({
      success: true,
      result: { uid, creator: assetId, readyToStream: false, duration: -1, size: 4, status: { state: "inprogress" } },
    }));
    env.STREAM_BROKER = namespace(new StreamUploadBroker(state as unknown as DurableObjectState, env));
    const response = await worker.fetch(inspectionRequest("videos"), env);
    expect(response.status).toBe(200);
    const evidence = await response.json() as { contentType: string | null; durationSeconds: number | null; state: string; readyToStream: boolean };
    expect(evidence).toMatchObject({ contentType: null, durationSeconds: null, state: "inprogress", readyToStream: false });
  });

  it("fails closed if provider UID or creator does not match the DO reservation", async () => {
    const state = stateWithProvision();
    const env = environment(new InspectionBucket(null), async () => Response.json({
      success: true,
      result: { uid: "c".repeat(32), creator: assetId, readyToStream: true, duration: 1, size: 1, status: { state: "ready" } },
    }));
    env.STREAM_BROKER = namespace(new StreamUploadBroker(state as unknown as DurableObjectState, env));
    const response = await worker.fetch(inspectionRequest("videos"), env);
    expect(response.status).toBe(502);
    expect(await response.text()).not.toContain("upload.videodelivery.net");
  });

  it("bounds provider response bytes and returns a generic error without exposing upstream body", async () => {
    const state = stateWithProvision();
    const env = environment(new InspectionBucket(null), async () => new Response("provider-secret ".repeat(3000), { status: 200 }));
    env.STREAM_BROKER = namespace(new StreamUploadBroker(state as unknown as DurableObjectState, env));
    const response = await worker.fetch(inspectionRequest("videos"), env);
    expect(response.status).toBe(502);
    expect(await response.text()).not.toContain("provider-secret");
  });

  it("does not follow redirects from Stream metadata inspection", async () => {
    const state = stateWithProvision();
    const calls: Array<{ url: string; redirect?: RequestRedirect }> = [];
    const env = environment(new InspectionBucket(null), async (input, init) => {
      calls.push({ url: String(input), redirect: init?.redirect });
      return new Response("redirect body must not escape", { status: 302, headers: { Location: "https://attacker.example/collect" } });
    });
    env.STREAM_BROKER = namespace(new StreamUploadBroker(state as unknown as DurableObjectState, env));
    const response = await worker.fetch(inspectionRequest("videos"), env);
    expect(response.status).toBe(502);
    expect(await response.text()).not.toContain("redirect body");
    expect(calls).toEqual([{
      url: `https://api.cloudflare.com/client/v4/accounts/11111111111111111111111111111111/stream/${uid}`,
      redirect: "manual",
    }]);
  });

  it("keeps a TUS reservation without UID unavailable to inspection", async () => {
    const state = new InspectionState();
    await state.storage.put("provision", { assetId, fingerprint: "fp", state: "ready", encryptedLocation: "encrypted" });
    const env = environment(new InspectionBucket(null));
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, env);
    const response = await broker.fetch(new Request("https://broker.invalid/inspect", {
      method: "GET", headers: { "X-Broker-Authorization": `Bearer ${brokerSecret}` },
    }));
    expect(response.status).toBe(404);
  });
});

function inspectionRequest(kind: "images" | "videos") {
  return new Request(`https://media.test/v1/provider/inspections/${kind}/${assetId}`, {
    method: "GET", headers: { "X-Broker-Authorization": `Bearer ${brokerSecret}` },
  });
}

function environment(bucket: InspectionBucket, fetcher: typeof fetch = async () => new Response()) {
  return {
    MEDIA_BUCKET: bucket as unknown as R2Bucket,
    BROKER_AUTH_KEY: brokerSecret,
    STREAM_API_BASE: "https://api.cloudflare.com",
    TUS_LOCATION_ENCRYPTION_KEY_B64: encode(new Uint8Array(32).fill(8)),
    CLOUDFLARE_ACCOUNT_ID: "11111111111111111111111111111111",
    CLOUDFLARE_STREAM_API_TOKEN: "stream-token-material-with-at-least-32-characters",
    MAX_VIDEO_BYTES: "1048576000",
    MAX_VIDEO_DURATION_SECONDS: "900",
    MAX_VIDEO_PLAYBACK_SESSION_SECONDS: "1800",
    STREAM_BROKER: namespace({ fetch: async () => new Response("unexpected DO request", { status: 500 }) }),
    fetcher,
  } as Environment;
}

function stateWithProvision() {
  const state = new InspectionState();
  void state.storage.put("provision", {
    assetId,
    fingerprint: "fingerprint",
    state: "ready",
    encryptedLocation: "encrypted-location",
    streamUid: uid,
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
  } satisfies StoredStreamProvision);
  return state;
}

function namespace(broker: { fetch(request: Request): Promise<Response> }): DurableObjectNamespace {
  return {
    idFromName: () => ({} as DurableObjectId),
    get: () => ({ fetch: (input: RequestInfo | URL, init?: RequestInit) => broker.fetch(new Request(input, init)) }),
  } as unknown as DurableObjectNamespace;
}

class InspectionState {
  storage = new InspectionStorage();
  async blockConcurrencyWhile<T>(callback: () => Promise<T>): Promise<T> { return callback(); }
}

class InspectionStorage {
  private readonly values = new Map<string, unknown>();
  async get<T>(key: string) { return this.values.get(key) as T | undefined; }
  async put(key: string, value: unknown) { this.values.set(key, structuredClone(value)); }
}

class InspectionBucket {
  headKey: string | null = null;
  getCalls = 0;
  constructor(private readonly response: { size: number; httpMetadata?: { contentType?: string }; customMetadata?: Record<string, string> } | null) {}
  async head(key: string) {
    this.headKey = key;
    return this.response ? { key, size: this.response.size, httpMetadata: this.response.httpMetadata, customMetadata: this.response.customMetadata } : null;
  }
  async get() { this.getCalls++; return null; }
}

function encode(bytes: Uint8Array) {
  let value = "";
  for (const byte of bytes) value += String.fromCharCode(byte);
  return btoa(value).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}
