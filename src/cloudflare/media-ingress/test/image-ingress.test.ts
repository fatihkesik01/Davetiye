import { describe, expect, it } from "vitest";
import { signImageCapability } from "../src/capability.js";
import { handleImageUpload, sniffRasterBody } from "../src/image-ingress.js";
import type { Environment, ImagesBinding } from "../src/types.js";

const assetId = "1234567890abcdef1234567890abcdef";
const signingKey = encode(new Uint8Array(32).fill(7));
const ceiling = 8;

describe("photo ingress byte boundary", () => {
  it("accepts exactly the ceiling and stores the normalized output only", async () => {
    const state = imageEnvironment();
    const source = jpegBytes(ceiling);
    const response = await upload(state.env, bytes(source), ceiling);
    expect(response.status).toBe(204);
    expect([...state.bucket.objects.get(`creator/${assetId}.webp`)!]).toEqual([250, ...source]);
    expect(state.bucket.options[0].onlyIf).toEqual({ etagDoesNotMatch: "*" });
    expect(state.bucket.options[0].customMetadata?.normalized).toBe("true");
  });

  it.each([
    ["honest content length", ceiling + 1, String(ceiling + 1)],
    ["dishonest smaller content length", ceiling + 1, "1"],
    ["missing content length", ceiling + 1, null],
    ["chunked request", ceiling + 1, null],
  ])("rejects a body above the ceiling with %s", async (_label, bodySize, declaredLength) => {
    const state = imageEnvironment();
    const body = bytes(jpegBytes(bodySize as number));
    const response = await upload(state.env, body, ceiling, declaredLength as string | null);
    expect(response.status).toBe(413);
    expect(state.bucket.objects.size).toBe(0);
    expect(state.bucket.putCount).toBe(0);
  });

  it("rejects interrupted upload streams without creating raw or partial output", async () => {
    const state = imageEnvironment();
    const broken = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new Uint8Array([0xff, 0xd8, 0xff]));
        controller.error(new Error("connection interrupted"));
      },
    });
    const response = await upload(state.env, broken, ceiling, null);
    expect(response.status).toBe(422);
    expect(state.bucket.objects.size).toBe(0);
    expect(state.bucket.putCount).toBe(0);
  });

  it("stores nothing when decode or transformation fails", async () => {
    const state = imageEnvironment({ failTransform: true });
    const response = await upload(state.env, bytes(jpegBytes(3)), ceiling);
    expect(response.status).toBe(422);
    expect(state.bucket.objects.size).toBe(0);
    expect(state.bucket.putCount).toBe(0);
  });

  it("recovers when R2 committed the object but its success response was lost", async () => {
    const state = imageEnvironment({ bucketFailsAfterWrite: true });
    const response = await upload(state.env, bytes(jpegBytes(3)), ceiling);
    expect(response.status).toBe(204);
    expect(state.bucket.objects.size).toBe(1);
    expect(state.bucket.deleteCount).toBe(0);
  });

  it("returns success for a replay after the original upload response was lost", async () => {
    const state = imageEnvironment();
    const capability = await token(Math.floor(Date.now() / 1000) + 120, ceiling);
    expect((await upload(state.env, bytes(jpegBytes(3)), ceiling, String(ceiling), capability)).status).toBe(204);
    expect((await upload(state.env, bytes(jpegBytes(3, 9)), ceiling, String(ceiling), capability)).status).toBe(204);
    expect(state.bucket.objects.size).toBe(1);
    expect(state.transformCount()).toBe(1);
  });

  it("does not delete a concurrent winner when the first R2 put fails before commit", async () => {
    const state = imageEnvironment();
    let markFirstPutEntered!: () => void;
    const firstPutEntered = new Promise<void>((resolve) => { markFirstPutEntered = resolve; });
    let releaseFirstPut!: () => void;
    const firstPutGate = new Promise<void>((resolve) => { releaseFirstPut = resolve; });
    state.bucket.beforeFirstPut = async () => {
      markFirstPutEntered();
      await firstPutGate;
    };
    const capability = await token(Math.floor(Date.now() / 1000) + 120, ceiling);

    const first = upload(state.env, bytes(jpegBytes(3)), ceiling, String(ceiling), capability);
    await firstPutEntered;
    const retry = await upload(state.env, bytes(jpegBytes(3, 9)), ceiling, String(ceiling), capability);
    expect(retry.status).toBe(204);
    releaseFirstPut();

    expect((await first).status).toBe(204);
    expect(state.bucket.objects.size).toBe(1);
    expect(state.bucket.deleteCount).toBe(0);
  });

  it("cancels an unauthenticated request body before consuming it", async () => {
    const state = imageEnvironment();
    let cancelled = false;
    const body = new ReadableStream<Uint8Array>({
      cancel() { cancelled = true; },
    });
    const request = new Request(`https://media.test/v1/images/${assetId}`, {
      method: "PUT", body, duplex: "half",
    } as RequestInit & { duplex: "half" });

    expect((await handleImageUpload(request, assetId, state.env)).status).toBe(401);
    expect(cancelled).toBe(true);
  });

  it("uses create-only storage so concurrent capability reuse cannot overwrite an asset", async () => {
    const state = imageEnvironment();
    const responses = await Promise.all([
      upload(state.env, bytes(jpegBytes(3)), ceiling),
      upload(state.env, bytes(jpegBytes(3, 9)), ceiling),
    ]);
    expect(responses.map((response) => response.status)).toEqual([204, 204]);
    expect(state.bucket.objects.size).toBe(1);
    expect(state.bucket.deleteCount).toBe(0);
  });

  it("does not replace a permanent-purge tombstone with a late image upload", async () => {
    const state = imageEnvironment();
    const key = `creator/${assetId}.webp`;
    state.bucket.objects.set(key, new Uint8Array());
    state.bucket.metadata.set(key, { assetId, tombstone: "true" });
    const response = await upload(state.env, bytes(jpegBytes(3)), ceiling);
    expect(response.status).toBe(409);
    expect(state.bucket.objects.get(key)).toEqual(new Uint8Array());
    expect(state.bucket.metadata.get(key)).toEqual({ assetId, tombstone: "true" });
    expect(state.transformCount()).toBe(0);
    expect(state.bucket.putCount).toBe(0);
  });

  it("loses atomically when permanent purge writes its tombstone during image normalization", async () => {
    const state = imageEnvironment();
    const key = `creator/${assetId}.webp`;
    state.bucket.beforeConditionalPut = () => {
      state.bucket.objects.set(key, new Uint8Array());
      state.bucket.metadata.set(key, { assetId, tombstone: "true" });
    };
    const response = await upload(state.env, bytes(jpegBytes(3)), ceiling);
    expect(response.status).toBe(409);
    expect(state.bucket.objects.get(key)).toEqual(new Uint8Array());
    expect(state.bucket.metadata.get(key)).toEqual({ assetId, tombstone: "true" });
  });

  it("rejects invalid, expired and over-configured capabilities", async () => {
    const state = imageEnvironment();
    const expired = await token(0, ceiling);
    const request = new Request(`https://media.test/v1/images/${assetId}`, {
      method: "PUT", body: bytes(jpegBytes(3)), headers: { "X-Media-Capability": expired }, duplex: "half",
    } as RequestInit & { duplex: "half" });
    expect((await handleImageUpload(request, assetId, state.env)).status).toBe(401);
    expect(state.bucket.putCount).toBe(0);
  });

  it("recognizes the supported raster signature allowlist and preserves the stream", async () => {
    const formats = [
      ["image/jpeg", [0xff, 0xd8, 0xff]],
      ["image/png", [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]],
      ["image/gif", [0x47, 0x49, 0x46, 0x38, 0x39, 0x61]],
      ["image/webp", [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50]],
    ] as const;

    for (const [contentType, signature] of formats) {
      const source = Uint8Array.from([...signature, 0x01, 0x02]);
      const result = await sniffRasterBody(bytes(source));
      expect(result?.contentType).toBe(contentType);
      expect(await readAll(result!.body)).toEqual([...source]);
    }
  });

  it.each([
    ["SVG with a misleading JPEG content type", "image/jpeg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"],
    ["HTML with a misleading PNG content type", "image/png", "<!doctype html><script>alert(1)</script>"],
  ])("rejects %s before invoking the Images binding", async (_label, contentType, source) => {
    const state = imageEnvironment();
    const response = await upload(state.env, bytes(new TextEncoder().encode(source as string)), ceiling,
      null, undefined, contentType as string);
    expect(response.status).toBe(415);
    expect(state.transformCount()).toBe(0);
    expect(state.bucket.putCount).toBe(0);
  });

  it("uses bytes rather than Content-Type when a valid raster body has a mismatched type", async () => {
    const state = imageEnvironment();
    const response = await upload(state.env, bytes(jpegBytes(3)), ceiling, "3", undefined, "image/svg+xml");
    expect(response.status).toBe(204);
    expect(state.transformCount()).toBe(1);
  });

  it.each([
    ["partial JPEG marker", [0xff, 0xd8]],
    ["truncated PNG signature", [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a]],
  ])("rejects a short body with %s before transformation", async (_label, prefix) => {
    const state = imageEnvironment();
    const response = await upload(state.env, bytes(prefix as number[]), ceiling, String((prefix as number[]).length));
    expect(response.status).toBe(415);
    expect(state.transformCount()).toBe(0);
    expect(state.bucket.putCount).toBe(0);
  });
});

async function upload(env: Environment, body: ReadableStream<Uint8Array>, maxBytes: number, contentLength: string | null = String(ceiling), capabilityOverride?: string, sourceContentType?: string) {
  const capability = capabilityOverride ?? await token(Math.floor(Date.now() / 1000) + 120, maxBytes);
  const headers = new Headers({ "X-Media-Capability": capability });
  if (sourceContentType) headers.set("Content-Type", sourceContentType);
  if (contentLength !== null) headers.set("Content-Length", contentLength);
  return handleImageUpload(new Request(`https://media.test/v1/images/${assetId}`, {
    method: "PUT", headers, body, duplex: "half",
  } as RequestInit & { duplex: "half" }), assetId, env);
}

async function token(exp: number, maxBytes: number) {
  return signImageCapability({ v: 1, aud: "davetiye-image-ingress", assetId, maxBytes, exp }, signingKey);
}

function imageEnvironment(options: { failTransform?: boolean; bucketFailsAfterWrite?: boolean } = {}) {
  const bucket = new MemoryBucket(options.bucketFailsAfterWrite ?? false);
  let transformCount = 0;
  const images: ImagesBinding = {
    input: (stream) => {
      transformCount++;
      return ({
      output: async () => {
        if (options.failTransform) {
          const reader = stream.getReader();
          try { while (!(await reader.read()).done) { /* drain */ } } finally { reader.releaseLock(); }
          throw new Error("decode failure");
        }
        const reader = stream.getReader();
        const raw: number[] = [];
        try {
          while (true) {
            const result = await reader.read();
            if (result.done) break;
            raw.push(...result.value);
          }
        } finally {
          reader.releaseLock();
        }
        return { response: () => new Response(new Uint8Array([250, ...raw]), { headers: { "Content-Type": "image/webp" } }) };
      },
    });
    },
  };
  const env = {
    MEDIA_BUCKET: bucket as unknown as R2Bucket,
    IMAGES: images,
    CAPABILITY_SIGNING_KEY_B64: signingKey,
    MAX_IMAGE_BYTES: String(ceiling),
    IMAGE_OUTPUT_FORMAT: "image/webp",
    now: () => Date.now(),
  } as Environment;
  return { env, bucket, transformCount: () => transformCount };
}

class MemoryBucket {
  objects = new Map<string, Uint8Array>();
  metadata = new Map<string, Record<string, string>>();
  options: R2PutOptions[] = [];
  putCount = 0;
  deleteCount = 0;
  beforeFirstPut?: () => Promise<void>;
  beforeConditionalPut?: () => void;

  constructor(private readonly failAfterWrite: boolean) {}

  async head(key: string): Promise<R2Object | null> {
    if (!this.objects.has(key)) return null;
    return {
      key,
      size: this.objects.get(key)!.byteLength,
      customMetadata: this.metadata.get(key),
      httpMetadata: { contentType: "image/webp" },
    } as R2Object;
  }

  async put(key: string, value: ReadableStream<Uint8Array>, options: R2PutOptions) {
    this.putCount++;
    this.options.push(options);
    if (this.putCount === 1 && this.beforeFirstPut) {
      await this.beforeFirstPut();
      throw new Error("synthetic failure before R2 commit");
    }
    const chunks: number[] = [];
    const reader = value.getReader();
    try {
      while (true) {
        const result = await reader.read();
        if (result.done) break;
        chunks.push(...result.value);
      }
    } finally {
      reader.releaseLock();
    }
    this.beforeConditionalPut?.();
    const onlyIf = options.onlyIf;
    if (onlyIf && !(onlyIf instanceof Headers) && onlyIf.etagDoesNotMatch === "*" && this.objects.has(key)) return null;
    const data = new Uint8Array(chunks);
    this.objects.set(key, data);
    this.metadata.set(key, options.customMetadata ?? {});
    if (this.failAfterWrite) throw new Error("synthetic provider disconnect after write");
    return { key, size: data.byteLength };
  }

  async delete(key: string) {
    this.deleteCount++;
    this.objects.delete(key);
    this.metadata.delete(key);
  }
}

function bytes(values: ArrayLike<number>) {
  return new ReadableStream<Uint8Array>({
    start(controller) {
      controller.enqueue(new Uint8Array(values));
      controller.close();
    },
  });
}

function jpegBytes(length: number, filler = 1): number[] {
  return [0xff, 0xd8, 0xff, ...Array.from({ length: Math.max(0, length - 3) }, () => filler)];
}

async function readAll(stream: ReadableStream<Uint8Array>): Promise<number[]> {
  const reader = stream.getReader();
  const output: number[] = [];
  try {
    while (true) {
      const next = await reader.read();
      if (next.done) return output;
      output.push(...next.value);
    }
  } finally {
    reader.releaseLock();
  }
}

function encode(bytes: Uint8Array) {
  let value = "";
  for (const byte of bytes) value += String.fromCharCode(byte);
  return btoa(value).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}
