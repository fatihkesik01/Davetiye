import { verifyImageCapability } from "./capability.js";
import type { Environment } from "./types.js";

const OUTPUT_FORMATS = new Set(["image/webp"]);

export async function handleImageUpload(request: Request, assetId: string, env: Environment): Promise<Response> {
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

  const claims = await verifyImageCapability(capability, env.CAPABILITY_SIGNING_KEY_B64, assetId,
    Math.floor((env.now?.() ?? Date.now()) / 1000), maxImageBytes);
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
      return isNormalizedObject(existing, assetId)
        ? new Response(null, { status: 204, headers: { "Cache-Control": "no-store" } })
        : problem(409, "This asset already has an incompatible stored object.");
    }

    const sniffed = await sniffRasterBody(request.body);
    if (!sniffed) return problem(415, "Only supported raster image formats are accepted.");
    const limitedBody = countAndLimit(sniffed.body, claims.maxBytes, () => { limitExceeded = true; });
    const format = env.IMAGE_OUTPUT_FORMAT;
    if (!OUTPUT_FORMATS.has(format)) {
      await cancelBody(request, "Unsupported image output format.");
      return problem(503, "Image output format is not configured.");
    }

    // Re-encoding to a raster format prevents source metadata from being copied to R2.
    // The provider contract must still be verified against stored bytes in the non-production account.
    const optimized = await env.IMAGES.input(limitedBody).output({ format: format as "image/webp" });
    const output = optimized.response();
    if (!output.ok || !output.body || output.headers.get("Content-Type")?.toLowerCase() !== "image/webp") {
      return problem(422, "Image decoding or normalization failed.");
    }

    const stored = await env.MEDIA_BUCKET.put(key, output.body, {
      onlyIf: { etagDoesNotMatch: "*" },
      httpMetadata: {
        contentType: output.headers.get("Content-Type") ?? "image/webp",
        cacheControl: "private, no-store",
      },
      customMetadata: { assetId, normalized: "true" },
    });
    if (!stored) {
      const winner = await env.MEDIA_BUCKET.head(key);
      return winner && isNormalizedObject(winner, assetId)
        ? new Response(null, { status: 204, headers: { "Cache-Control": "no-store" } })
        : problem(409, "This asset has already received an upload.");
    }
    return new Response(null, { status: 204, headers: { "Cache-Control": "no-store" } });
  } catch (error) {
    // A single R2 put is atomic. If the provider committed the object but its response was lost,
    // report the stored success; never delete by key here because a concurrent retry may own it.
    const committed = await env.MEDIA_BUCKET.head(key).catch(() => null);
    if (committed && isNormalizedObject(committed, assetId)) {
      return new Response(null, { status: 204, headers: { "Cache-Control": "no-store" } });
    }
    if (limitExceeded || error instanceof UploadTooLargeError) return problem(413, "Upload exceeds its byte ceiling.");
    if (error instanceof UploadInterruptedError) return problem(400, "Upload stream was interrupted.");
    return problem(422, "Image could not be normalized or stored.");
  }
}

function isNormalizedObject(object: R2Object, assetId: string): boolean {
  return object.customMetadata?.assetId === assetId && object.customMetadata.normalized === "true" &&
    object.httpMetadata?.contentType === "image/webp";
}

async function cancelBody(request: Request, reason: string): Promise<void> {
  await request.body?.cancel(reason).catch(() => undefined);
}

export function countAndLimit(body: ReadableStream<Uint8Array>, maximumBytes: number, onExceeded?: () => void): ReadableStream<Uint8Array> {
  let received = 0;
  return body.pipeThrough(new TransformStream<Uint8Array, Uint8Array>({
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
      // pipeThrough propagates cancellation to the client request body.
    },
  }));
}

type RasterFormat = "image/jpeg" | "image/png" | "image/gif" | "image/webp";

interface SniffedRasterBody {
  contentType: RasterFormat;
  body: ReadableStream<Uint8Array>;
}

/** Reads only enough bytes to classify a raster signature, then streams the untouched source onward. */
export async function sniffRasterBody(body: ReadableStream<Uint8Array>): Promise<SniffedRasterBody | null> {
  const reader = body.getReader();
  const prefix = new Uint8Array(12);
  let prefixLength = 0;
  let trailingChunk: Uint8Array | null = null;
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
      await reader.cancel("Unsupported or truncated raster signature.").catch(() => undefined);
      reader.releaseLock();
      return null;
    }

    let prefixPending = true;
    let trailingPending = trailingChunk !== null;
    const replay = new ReadableStream<Uint8Array>({
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
        await reader.cancel(reason).catch(() => undefined);
        reader.releaseLock();
      },
    });
    return { contentType, body: replay };
  } catch (error) {
    await reader.cancel(error).catch(() => undefined);
    reader.releaseLock();
    throw error;
  }
}

function classifyRasterPrefix(prefix: Uint8Array, length: number): RasterFormat | null {
  if (length >= 3 && prefix[0] === 0xff && prefix[1] === 0xd8 && prefix[2] === 0xff) return "image/jpeg";
  if (length >= 8 && prefix[0] === 0x89 && ascii(prefix, 1, 3) === "PNG" &&
      prefix[4] === 0x0d && prefix[5] === 0x0a && prefix[6] === 0x1a && prefix[7] === 0x0a) return "image/png";
  if (length >= 6 && (ascii(prefix, 0, 6) === "GIF87a" || ascii(prefix, 0, 6) === "GIF89a")) return "image/gif";
  if (length >= 12 && ascii(prefix, 0, 4) === "RIFF" && ascii(prefix, 8, 4) === "WEBP") return "image/webp";
  return null;
}

function ascii(bytes: Uint8Array, start: number, length: number): string {
  return String.fromCharCode(...bytes.subarray(start, start + length));
}

export class UploadTooLargeError extends Error {}
export class UploadInterruptedError extends Error {}

function parsePositiveInteger(value: string): number | null {
  if (!/^\d+$/u.test(value)) return null;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
}

function problem(status: number, title: string): Response {
  return Response.json({ title, status }, { status, headers: { "Cache-Control": "no-store" } });
}
