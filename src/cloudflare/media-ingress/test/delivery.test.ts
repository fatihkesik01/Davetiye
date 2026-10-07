import { describe, expect, it } from "vitest";
import worker from "../src/index.js";
import type { Environment } from "../src/types.js";

const assetId = "1234567890abcdef1234567890abcdef";
const key = new Uint8Array(32).fill(7);

describe("private media delivery", () => {
  it("rejects expired capabilities before looking up the private object", async () => {
    let reads = 0;
    const env = deliveryEnvironment(() => { reads++; return null; });
    const token = await capability(Math.floor(Date.now() / 1000) - 1);
    const response = await worker.fetch(new Request(`https://media.test/v1/delivery/images/${assetId}?capability=${token}`), env);
    expect(response.status).toBe(404);
    expect(reads).toBe(0);
  });

  it("transforms a verified private R2 object and bounds cache life by the remaining signature TTL", async () => {
    let reads = 0;
    let transforms = 0;
    const env = deliveryEnvironment(() => {
      reads++;
      return {
        size: 1024,
        body: new ReadableStream<Uint8Array>({ start(controller) { controller.enqueue(new Uint8Array([1])); controller.close(); } }),
        customMetadata: { assetId, normalized: "true" },
        httpMetadata: { contentType: "image/webp" },
      };
    });
    env.IMAGES = {
      input: () => ({ output: async () => ({ response: () => {
        transforms++;
        return new Response(new Uint8Array([2, 3]), { headers: { "Content-Type": "image/webp" } });
      } }) }),
    };
    env.now = () => 1_000_000;
    const token = await capability(1005);
    const response = await worker.fetch(new Request(`https://media.test/v1/delivery/images/${assetId}?capability=${token}`), env);
    expect(response.status).toBe(200);
    expect(response.headers.get("Content-Type")).toBe("image/webp");
    expect(response.headers.get("X-Content-Type-Options")).toBe("nosniff");
    expect(response.headers.get("Cache-Control")).toContain("max-age=5");
    expect(await response.bytes()).toEqual(new Uint8Array([2, 3]));
    expect(reads).toBe(1);
    expect(transforms).toBe(1);
  });

  it("does not deliver an object whose trusted R2 metadata does not match the asset", async () => {
    let transforms = 0;
    const env = deliveryEnvironment(() => ({ size: 1, body: new ReadableStream(),
      customMetadata: { assetId: "a".repeat(32), normalized: "true" }, httpMetadata: { contentType: "image/webp" } }));
    env.IMAGES = { input: () => ({ output: async () => { transforms++; return { response: () => new Response() }; } }) };
    env.now = () => 1_000_000;
    const token = await capability(1060);
    const response = await worker.fetch(new Request(`https://media.test/v1/delivery/images/${assetId}?capability=${token}`), env);
    expect(response.status).toBe(404);
    expect(transforms).toBe(0);
  });
});

function deliveryEnvironment(get: () => unknown): Environment {
  return {
    CAPABILITY_SIGNING_KEY_B64: encode(key),
    MEDIA_BUCKET: { get } as unknown as R2Bucket,
    IMAGES: { input: () => ({ output: async () => ({ response: () => new Response() }) }) },
  } as unknown as Environment;
}

async function capability(exp: number): Promise<string> {
  const claims = { v: 1, aud: "davetiye-image-delivery", assetId, exp };
  const encoded = encode(new TextEncoder().encode(JSON.stringify(claims)));
  const cryptoKey = await crypto.subtle.importKey("raw", key, { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const signature = await crypto.subtle.sign("HMAC", cryptoKey, new TextEncoder().encode(encoded));
  return `${encoded}.${encode(new Uint8Array(signature))}`;
}

function encode(bytes: Uint8Array): string {
  let value = "";
  for (const byte of bytes) value += String.fromCharCode(byte);
  return btoa(value).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}
