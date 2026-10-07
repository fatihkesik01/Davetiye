import { describe, expect, it } from "vitest";
import { StreamUploadBroker } from "../src/stream-broker.js";
import type { Environment, StoredStreamProvision } from "../src/types.js";

const now = Date.parse("2026-10-04T12:00:00.000Z");
const baseInput = {
  assetId: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  declaredByteLength: 12_345,
  maximumBytes: 50_000,
  expiresAt: new Date(now + 5 * 60_000).toISOString(),
  maxDurationSeconds: 120,
};

describe("Stream TUS capability broker", () => {
  it("creates a bounded playback token with an explicit expiry and no download permission", async () => {
    const state = new FakeState();
    await state.storage.put("provision", {
      assetId: baseInput.assetId, fingerprint: "f".repeat(64), state: "ready",
      encryptedLocation: "encrypted", streamUid: "b".repeat(32), expiresAt: baseInput.expiresAt,
    } satisfies StoredStreamProvision);
    let sent: Request | undefined;
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, environment(async (input, init) => {
      sent = new Request(input, init);
      return Response.json({ success: true, result: { token: "signed.jwt.token" } });
    }));
    const exp = Math.floor(now / 1000) + 900;
    const result = await broker.fetch(new Request("https://broker.local/playback", {
      method: "POST", headers: { "X-Broker-Authorization": "Bearer test-broker-key-with-enough-entropy-123456", "Content-Type": "application/json" },
      body: JSON.stringify({ assetId: baseInput.assetId, exp }),
    }));
    expect(result.status).toBe(200);
    expect(await result.json()).toEqual({ token: "signed.jwt.token", expiresAt: exp });
    expect(sent?.url).toBe(`https://api.cloudflare.com/client/v4/accounts/${"a".repeat(32)}/stream/${"b".repeat(32)}/token`);
    expect(sent?.redirect).toBe("manual");
    expect(await sent?.json()).toEqual({ exp, downloadable: false });
  });

  it("rejects a playback session above the configured hard expiry before contacting Stream", async () => {
    const state = new FakeState();
    await state.storage.put("provision", {
      assetId: baseInput.assetId, fingerprint: "f".repeat(64), state: "ready",
      encryptedLocation: "encrypted", streamUid: "b".repeat(32), expiresAt: baseInput.expiresAt,
    } satisfies StoredStreamProvision);
    let calls = 0;
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, environment(async () => { calls++; return Response.json({}); }));
    const response = await broker.fetch(new Request("https://broker.local/playback", {
      method: "POST", headers: { "X-Broker-Authorization": "Bearer test-broker-key-with-enough-entropy-123456", "Content-Type": "application/json" },
      body: JSON.stringify({ assetId: baseInput.assetId, exp: Math.floor(now / 1000) + 1801 }),
    }));
    expect(response.status).toBe(400);
    expect(calls).toBe(0);
  });

  it("persists before provisioning and returns the same encrypted TUS capability on retry", async () => {
    const state = new FakeState();
    const calls: Request[] = [];
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, environment(async (input, init) => {
      calls.push(new Request(input, init));
      return new Response(null, { status: 201, headers: { Location: "https://upload.videodelivery.net/one-time-abc", "stream-media-id": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } });
    }));
    const first = await broker.fetch(brokerRequest(baseInput));
    const firstBody = await first.json() as { uploadUrl: string };
    expect(first.status).toBe(200);
    expect(firstBody.uploadUrl).toBe("https://upload.videodelivery.net/one-time-abc");
    expect(state.persistedBeforeFetch).toBe(true);
    expect(calls).toHaveLength(1);
    expect(calls[0].headers.get("Upload-Length")).toBe(String(baseInput.declaredByteLength));
    expect(calls[0].headers.get("Tus-Resumable")).toBe("1.0.0");
    expect(calls[0].headers.get("Upload-Metadata")).toContain("requiresignedurls");
    const stored = await state.storage.get<StoredStreamProvision>("provision");
    expect(stored?.state).toBe("ready");
    expect(stored?.encryptedLocation).not.toContain("upload.videodelivery.net");
    expect(stored?.streamUid).toBe("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

    const replay = await broker.fetch(brokerRequest(baseInput));
    expect(replay.status).toBe(200);
    expect(await replay.json()).toEqual(firstBody);
    expect(calls).toHaveLength(1);
  });

  it("rejects a same-asset request with changed size or expiry", async () => {
    const state = new FakeState();
    let calls = 0;
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, environment(async () => {
      calls++;
      return new Response(null, { status: 201, headers: { Location: "https://upload.videodelivery.net/one-time-abc", "stream-media-id": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } });
    }));
    expect((await broker.fetch(brokerRequest(baseInput))).status).toBe(200);
    expect((await broker.fetch(brokerRequest({ ...baseInput, declaredByteLength: 12_346 }))).status).toBe(409);
    expect(calls).toBe(1);
  });

  it("serializes concurrent requests so one asset creates one provider resource", async () => {
    const state = new FakeState();
    let calls = 0;
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, environment(async () => {
      calls++;
      await Promise.resolve();
      return new Response(null, { status: 201, headers: { Location: "https://upload.videodelivery.net/one-time-abc", "stream-media-id": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } });
    }));
    const responses = await Promise.all([broker.fetch(brokerRequest(baseInput)), broker.fetch(brokerRequest(baseInput))]);
    expect(responses.map((response) => response.status)).toEqual([200, 200]);
    expect(calls).toBe(1);
  });

  it("marks provider/network failure ambiguous and never repeats the external POST", async () => {
    const state = new FakeState();
    let calls = 0;
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, environment(async () => {
      calls++;
      throw new Error("response lost after provider accepted");
    }));
    expect((await broker.fetch(brokerRequest(baseInput))).status).toBe(503);
    expect((await state.storage.get<StoredStreamProvision>("provision"))?.state).toBe("ambiguous");
    expect((await broker.fetch(brokerRequest(baseInput))).status).toBe(503);
    expect(calls).toBe(1);
  });

  it("does not provision when declared size exceeds configured entitlement ceiling", async () => {
    const state = new FakeState();
    let calls = 0;
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, environment(async () => {
      calls++;
      return new Response(null, { status: 201, headers: { Location: "https://upload.videodelivery.net/id", "stream-media-id": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } });
    }));
    const response = await broker.fetch(brokerRequest({ ...baseInput, declaredByteLength: 50_001 }));
    expect(response.status).toBe(400);
    expect(calls).toBe(0);
  });

  it("fails closed when broker auth is missing", async () => {
    const state = new FakeState();
    let calls = 0;
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, environment(async () => {
      calls++;
      return new Response(null, { status: 201, headers: { Location: "https://upload.videodelivery.net/id", "stream-media-id": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } });
    }));
    const request = new Request("https://broker.local/provision", {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(baseInput),
    });
    expect((await broker.fetch(request)).status).toBe(401);
    expect(calls).toBe(0);
  });

  it("never sends the Stream token to a deceptive hostname suffix", async () => {
    const state = new FakeState();
    let calls = 0;
    const env = environment(async () => {
      calls++;
      return new Response(null, { status: 201, headers: { Location: "https://upload.videodelivery.net/id", "stream-media-id": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } });
    });
    env.STREAM_API_BASE = "https://evilcloudflare.com";
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, env);

    expect((await broker.fetch(brokerRequest(baseInput))).status).toBe(503);
    expect(calls).toBe(0);
    expect(await state.storage.get<StoredStreamProvision>("provision")).toBeUndefined();
  });

  it("does not follow a redirect from the configured Stream API origin", async () => {
    const state = new FakeState();
    const requests: Array<{ url: string; redirect?: RequestRedirect }> = [];
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, environment(async (input, init) => {
      requests.push({ url: String(input), redirect: init?.redirect });
      return new Response(null, { status: 302, headers: { Location: "https://attacker.example/collect" } });
    }));

    expect((await broker.fetch(brokerRequest(baseInput))).status).toBe(503);
    expect(requests).toEqual([{
      url: "https://api.cloudflare.com/client/v4/accounts/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/stream?direct_user=true",
      redirect: "manual",
    }]);
    expect((await state.storage.get<StoredStreamProvision>("provision"))?.state).toBe("ambiguous");
  });

  it("tombstones first, retries transient deletion by asset identity, and blocks late provisioning", async () => {
    const state = new FakeState();
    const bucket = new MemoryBucket();
    const urls: string[] = [];
    let listAttempts = 0;
    const env = environment(async (input, init) => {
      const url = String(input);
      urls.push(`${init?.method ?? "GET"} ${url}`);
      if (url.includes("creator=")) {
        listAttempts++;
        if (listAttempts === 1) return new Response("unavailable", { status: 503 });
        return Response.json({ success: true, result: [{
          uid: "b".repeat(32), creator: baseInput.assetId, readyToStream: true, duration: 60, size: 1024,
          status: { state: "ready" },
        }] });
      }
      if (init?.method === "DELETE") return new Response(null, { status: 404 });
      throw new Error(`Unexpected provider request: ${init?.method} ${url}`);
    });
    env.MEDIA_BUCKET = bucket as unknown as R2Bucket;
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, env);
    const deletion = () => broker.fetch(new Request(`https://broker.local/delete?assetId=${baseInput.assetId}`, {
      method: "POST", headers: { "X-Broker-Authorization": `Bearer ${env.BROKER_AUTH_KEY}` },
    }));

    expect((await deletion()).status).toBe(502);
    expect(await state.storage.get("deleted")).toMatchObject({ assetId: baseInput.assetId });
    expect(bucket.objects.get(`creator/${baseInput.assetId}.webp`)).toMatchObject({
      metadata: { assetId: baseInput.assetId, tombstone: "true" },
    });

    const retry = await deletion();
    expect(retry.status).toBe(204); // Provider 404 is idempotent absence, not a reconciliation error.
    expect(retry.headers.get("X-Media-Deletion-Result")).toBe("already-absent");
    expect(urls.filter(url => url.startsWith("DELETE "))).toHaveLength(1);
    expect((await broker.fetch(brokerRequest(baseInput))).status).toBe(410);
    expect(urls.filter(url => url.startsWith("POST "))).toHaveLength(0);
  });

  it("does not allow unauthenticated provider deletion", async () => {
    const state = new FakeState();
    const bucket = new MemoryBucket();
    const env = environment(async () => { throw new Error("provider must not be called"); });
    env.MEDIA_BUCKET = bucket as unknown as R2Bucket;
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, env);
    expect((await broker.fetch(new Request(`https://broker.local/delete?assetId=${baseInput.assetId}`, { method: "POST" }))).status).toBe(401);
    expect(await state.storage.get("deleted")).toBeUndefined();
    expect(bucket.objects.size).toBe(0);
  });

  it("denies playback and both inspection paths after a purge tombstone without provider calls", async () => {
    const state = new FakeState();
    await state.storage.put("deleted", { assetId: baseInput.assetId, deletedAt: new Date(now).toISOString() });
    await state.storage.put("provision", {
      assetId: baseInput.assetId, fingerprint: "f".repeat(64), state: "ready",
      encryptedLocation: "encrypted", streamUid: "b".repeat(32), expiresAt: baseInput.expiresAt,
    } satisfies StoredStreamProvision);
    let providerCalls = 0;
    const broker = new StreamUploadBroker(state as unknown as DurableObjectState, environment(async () => {
      providerCalls++;
      return Response.json({ success: true, result: { token: "must-not-issue" } });
    }));
    const headers = { "X-Broker-Authorization": "Bearer test-broker-key-with-enough-entropy-123456" };
    const responses = await Promise.all([
      broker.fetch(new Request("https://broker.local/inspect", { headers })),
      broker.fetch(new Request(`https://broker.local/inspect-presence?assetId=${baseInput.assetId}`, { headers })),
      broker.fetch(new Request("https://broker.local/playback", {
        method: "POST", headers: { ...headers, "Content-Type": "application/json" },
        body: JSON.stringify({ assetId: baseInput.assetId, exp: Math.floor(now / 1000) + 60 }),
      })),
    ]);
    expect(responses.map(response => response.status)).toEqual([404, 404, 404]);
    expect(providerCalls).toBe(0);
  });
});

function brokerRequest(body: unknown) {
  return new Request("https://broker.local/provision", {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Broker-Authorization": "Bearer test-broker-key-with-enough-entropy-123456" },
    body: JSON.stringify(body),
  });
}

function environment(fetcher: typeof fetch): Environment {
  return {
    BROKER_AUTH_KEY: "test-broker-key-with-enough-entropy-123456",
    TUS_LOCATION_ENCRYPTION_KEY_B64: encode(new Uint8Array(32).fill(9)),
    CLOUDFLARE_ACCOUNT_ID: "a".repeat(32),
    CLOUDFLARE_STREAM_API_TOKEN: "test-token-material-with-at-least-32-characters",
    STREAM_API_BASE: "https://api.cloudflare.com",
    MAX_VIDEO_BYTES: "50000",
    MAX_VIDEO_DURATION_SECONDS: "900",
    MAX_VIDEO_PLAYBACK_SESSION_SECONDS: "1800",
    now: () => now,
    fetcher,
  } as Environment;
}

class FakeState {
  storage = new FakeStorage(this);
  persistedBeforeFetch = false;
  private queue = Promise.resolve();

  async blockConcurrencyWhile<T>(callback: () => Promise<T>): Promise<T> {
    const previous = this.queue;
    let unlock!: () => void;
    this.queue = new Promise<void>((resolve) => { unlock = resolve; });
    await previous;
    try { return await callback(); } finally { unlock(); }
  }
}

class FakeStorage {
  private values = new Map<string, unknown>();
  constructor(private readonly state: FakeState) {}

  async get<T>(key: string) { return this.values.get(key) as T | undefined; }
  async put(key: string, value: unknown) {
    if (key === "provision" && (value as StoredStreamProvision).state === "provisioning") this.state.persistedBeforeFetch = true;
    this.values.set(key, structuredClone(value));
  }
}

class MemoryBucket {
  objects = new Map<string, { metadata?: Record<string, string>; bytes: Uint8Array }>();
  async head(key: string): Promise<R2Object | null> {
    const value = this.objects.get(key);
    return value ? { key, size: value.bytes.byteLength, customMetadata: value.metadata } as R2Object : null;
  }
  async put(key: string, value: ArrayBuffer | ArrayBufferView | string | ReadableStream<Uint8Array>, options?: R2PutOptions) {
    const bytes = typeof value === "string" ? new TextEncoder().encode(value) : value instanceof ReadableStream
      ? new Uint8Array() : value instanceof ArrayBuffer ? new Uint8Array(value) : new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
    this.objects.set(key, { bytes: new Uint8Array(bytes), metadata: options?.customMetadata });
    return { key, size: bytes.byteLength };
  }
}

function encode(bytes: Uint8Array) {
  let value = "";
  for (const byte of bytes) value += String.fromCharCode(byte);
  return btoa(value).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}
