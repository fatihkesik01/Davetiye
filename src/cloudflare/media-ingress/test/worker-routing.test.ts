import { describe, expect, it } from "vitest";
import worker from "../src/index.js";
import type { Environment } from "../src/types.js";

describe("Worker routing guards", () => {
  it("authenticates the Stream broker before creating/routing to a Durable Object", async () => {
    let allocations = 0;
    const env = routingEnvironment(() => { allocations++; return { fetch: async () => Response.json({ ok: true }) }; });
    const response = await worker.fetch(new Request("https://media.test/v1/stream/upload-capabilities", {
      method: "POST", body: JSON.stringify({ assetId: "a".repeat(32) }),
    }), env);
    expect(response.status).toBe(401);
    expect(allocations).toBe(0);
  });

  it("does not allocate a Durable Object for unauthenticated video inspection", async () => {
    let allocations = 0;
    const env = routingEnvironment(() => { allocations++; return { fetch: async () => Response.json({ ok: true }) }; });
    const response = await worker.fetch(new Request(`https://media.test/v1/provider/inspections/videos/${"a".repeat(32)}`), env);
    expect(response.status).toBe(401);
    expect(allocations).toBe(0);
  });

  it("authenticates permanent provider deletion before allocating a Durable Object", async () => {
    let allocations = 0;
    const env = routingEnvironment(() => { allocations++; return { fetch: async () => Response.json({ ok: true }) }; });
    const response = await worker.fetch(new Request(`https://media.test/v1/provider/assets/${"a".repeat(32)}/delete`, {
      method: "POST",
    }), env);
    expect(response.status).toBe(401);
    expect(allocations).toBe(0);
  });

  it("authenticates playback-session issuance before allocating a Durable Object", async () => {
    let allocations = 0;
    const env = routingEnvironment(() => { allocations++; return { fetch: async () => Response.json({ ok: true }) }; });
    const response = await worker.fetch(new Request("https://media.test/v1/stream/playback-sessions", {
      method: "POST", body: JSON.stringify({ assetId: "a".repeat(32), exp: Math.floor(Date.now() / 1000) + 60 }),
    }), env);
    expect(response.status).toBe(401);
    expect(allocations).toBe(0);
  });

  it("rejects actual broker bytes over 4 KiB even without a length header", async () => {
    let allocations = 0;
    const env = routingEnvironment(() => { allocations++; return { fetch: async () => Response.json({ ok: true }) }; });
    const body = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new Uint8Array(4097));
        controller.close();
      },
    });
    const response = await worker.fetch(new Request("https://media.test/v1/stream/upload-capabilities", {
      method: "POST",
      headers: { "X-Broker-Authorization": `Bearer ${env.BROKER_AUTH_KEY}` },
      body,
      duplex: "half",
    } as RequestInit & { duplex: "half" }), env);
    expect(response.status).toBe(413);
    expect(allocations).toBe(0);
  });

  it("denies an unapproved browser origin before consuming the photo body", async () => {
    const env = routingEnvironment(() => ({ fetch: async () => new Response() }));
    const response = await worker.fetch(new Request(`https://media.test/v1/images/${"a".repeat(32)}`, {
      method: "PUT",
      headers: { Origin: "https://attacker.test" },
      body: "x",
    }), env);
    expect(response.status).toBe(403);
  });
});

function routingEnvironment(createObject: () => { fetch(request: Request): Promise<Response> }): Environment {
  const key = "worker-broker-test-key-with-at-least-32-characters";
  return {
    BROKER_AUTH_KEY: key,
    STREAM_BROKER: {
      idFromName: () => ({} as DurableObjectId),
      get: () => { createObject(); return { fetch: async (input: RequestInfo | URL, init?: RequestInit) => new Response(JSON.stringify({ routed: true }), { status: 200 }) }; },
    } as unknown as DurableObjectNamespace,
    ALLOWED_UPLOAD_ORIGINS: "https://app.test",
    STREAM_API_BASE: "https://api.cloudflare.com",
    CLOUDFLARE_ACCOUNT_ID: "a".repeat(32),
    CLOUDFLARE_STREAM_API_TOKEN: "test-token-material-with-at-least-32-characters",
    TUS_LOCATION_ENCRYPTION_KEY_B64: "CQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQk",
  } as Environment;
}
