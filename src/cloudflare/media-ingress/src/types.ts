export interface ImagesBinding {
  input(stream: ReadableStream<Uint8Array>): {
    output(options: { format: "image/webp" }): Promise<{ response(): Response }>;
  };
}

export interface Environment {
  MEDIA_BUCKET: R2Bucket;
  IMAGES: ImagesBinding;
  STREAM_BROKER: DurableObjectNamespace;
  CAPABILITY_SIGNING_KEY_B64: string;
  BROKER_AUTH_KEY: string;
  TUS_LOCATION_ENCRYPTION_KEY_B64: string;
  CLOUDFLARE_ACCOUNT_ID: string;
  CLOUDFLARE_STREAM_API_TOKEN: string;
  MAX_IMAGE_BYTES: string;
  IMAGE_OUTPUT_FORMAT: string;
  STREAM_API_BASE: string;
  MAX_VIDEO_BYTES: string;
  MAX_VIDEO_DURATION_SECONDS: string;
  MAX_VIDEO_PLAYBACK_SESSION_SECONDS?: string;
  ALLOWED_UPLOAD_ORIGINS: string;
  fetcher?: typeof fetch;
  now?: () => number;
}

export interface StreamProvisionRequest {
  assetId: string;
  declaredByteLength: number;
  maximumBytes: number;
  expiresAt: string;
  maxDurationSeconds: number;
}

export interface StreamCapability {
  uploadUrl: string;
  expiresAt: string;
}

export interface StoredStreamProvision {
  assetId: string;
  fingerprint: string;
  state: "provisioning" | "ready" | "ambiguous";
  encryptedLocation?: string;
  streamUid?: string;
  expiresAt?: string;
}

export interface StoredAssetTombstone {
  assetId: string;
  deletedAt: string;
}

export interface StreamUploadBroker {
  fetch(request: Request): Promise<Response>;
}
