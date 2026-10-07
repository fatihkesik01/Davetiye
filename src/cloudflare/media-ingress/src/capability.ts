export interface ImageCapabilityClaims {
  v: 1;
  aud: "davetiye-image-ingress";
  assetId: string;
  maxBytes: number;
  exp: number;
}

const encoder = new TextEncoder();

function encodeBase64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}

function decodeBase64Url(value: string): Uint8Array {
  const base64 = value.replaceAll("-", "+").replaceAll("_", "/");
  const binary = atob(base64 + "=".repeat((4 - (base64.length % 4)) % 4));
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}

export function encodeClaims(claims: ImageCapabilityClaims): string {
  return encodeBase64Url(encoder.encode(JSON.stringify(claims)));
}

export async function signImageCapability(claims: ImageCapabilityClaims, secret: string): Promise<string> {
  const encoded = encodeClaims(claims);
  const key = await importHmacKey(secret);
  const signature = await crypto.subtle.sign("HMAC", key, encoder.encode(encoded));
  return `${encoded}.${encodeBase64Url(new Uint8Array(signature))}`;
}

export async function verifyImageCapability(
  token: string,
  secret: string,
  expectedAssetId: string,
  nowSeconds: number,
  maxCeiling: number,
): Promise<ImageCapabilityClaims | null> {
  const parts = token.split(".");
  if (parts.length !== 2 || !parts[0] || !parts[1]) return null;

  try {
    const key = await importHmacKey(secret);
    const verified = await crypto.subtle.verify("HMAC", key, toBufferSource(decodeBase64Url(parts[1])), toBufferSource(encoder.encode(parts[0])));
    if (!verified) return null;

    const claims: unknown = JSON.parse(new TextDecoder().decode(decodeBase64Url(parts[0])));
    if (!isImageCapabilityClaims(claims) || claims.assetId !== expectedAssetId || claims.exp <= nowSeconds ||
        claims.maxBytes <= 0 || claims.maxBytes > maxCeiling) {
      return null;
    }
    return claims;
  } catch {
    return null;
  }
}

async function importHmacKey(secret: string): Promise<CryptoKey> {
  const raw = decodeBase64Url(secret);
  if (raw.byteLength < 32) throw new Error("Capability signing key must contain at least 256 bits.");
  return crypto.subtle.importKey("raw", toBufferSource(raw), { name: "HMAC", hash: "SHA-256" }, false, ["sign", "verify"]);
}

function toBufferSource(bytes: Uint8Array): ArrayBuffer {
  return Uint8Array.from(bytes).buffer;
}

function isImageCapabilityClaims(value: unknown): value is ImageCapabilityClaims {
  if (typeof value !== "object" || value === null) return false;
  const claims = value as Record<string, unknown>;
  return claims.v === 1 && claims.aud === "davetiye-image-ingress" && typeof claims.assetId === "string" &&
    /^[0-9a-f]{32}$/iu.test(claims.assetId) && Number.isSafeInteger(claims.maxBytes) &&
    Number.isSafeInteger(claims.exp);
}

export function base64Url(bytes: Uint8Array): string {
  return encodeBase64Url(bytes);
}

export function fromBase64Url(value: string): Uint8Array {
  return decodeBase64Url(value);
}
