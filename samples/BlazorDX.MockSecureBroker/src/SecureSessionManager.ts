import { webcrypto } from 'node:crypto';
import { SessionCache } from './SessionCache.js';

/**
 * ECDH over the NIST P-256 curve, via Node's Web Crypto API implementation
 * (`node:crypto`'s `webcrypto` export -- the same `SubtleCrypto` surface a
 * browser or a Blazor WebAssembly core would call). No third-party crypto
 * library is used anywhere in this SDK.
 */
const ECDH_PARAMS = { name: 'ECDH', namedCurve: 'P-256' } as const;

// @types/node declares these under the `webcrypto` namespace rather than as
// DOM globals; aliasing them here keeps the rest of the file reading like
// standard Web Crypto API code without pulling in the "DOM" lib (which would
// add unrelated browser globals to a Node/Express project).
type JsonWebKey = webcrypto.JsonWebKey;
type CryptoKey = webcrypto.CryptoKey;
type CryptoKeyPair = webcrypto.CryptoKeyPair;

export interface InitializeSecureSessionRequest {
  /** The client's (BlazorDX Wasm core's) ephemeral ECDH public key, JWK-encoded. */
  clientPublicKeyJwk: JsonWebKey;
  /** Bearer-style user auth token identifying who is opening this session. */
  authToken: string;
}

export interface InitializeSecureSessionResult {
  /** Opaque session identifier the client uses on all subsequent calls
   *  (payload delivery, telemetry webhooks) for this exchange. */
  sessionId: string;
  /** The server's ephemeral ECDH public key, JWK-encoded, so the client can
   *  derive the identical shared secret on its side. */
  serverPublicKeyJwk: JsonWebKey;
  /** Epoch-ms timestamp after which the session's shared secret is evicted
   *  from the volatile cache and can no longer be used. */
  expiresAt: number;
}

export type AuthTokenVerifier = (authToken: string) => boolean | Promise<boolean>;

export class AuthTokenError extends Error {
  constructor(message = 'Invalid or missing user auth token') {
    super(message);
    this.name = 'AuthTokenError';
  }
}

export class InvalidClientKeyError extends Error {
  constructor(
    message = 'Client public key is not a valid ECDH P-256 JWK',
    options?: ErrorOptions,
  ) {
    super(message, options);
    this.name = 'InvalidClientKeyError';
  }
}

/**
 * Default auth-token verifier: accepts `Bearer <token>` where `<token>` is
 * at least 16 characters. This is a MOCK stand-in for the real broker's
 * identity-provider check (JWT signature validation, introspection
 * endpoint, etc). A production implementation MUST replace this via the
 * `authTokenVerifier` constructor option -- it is intentionally injectable
 * for exactly that reason.
 */
export const defaultAuthTokenVerifier: AuthTokenVerifier = (authToken) => {
  if (typeof authToken !== 'string') return false;
  return /^Bearer\s+(.{16,})$/.test(authToken.trim());
};

export interface SecureSessionManagerOptions {
  authTokenVerifier?: AuthTokenVerifier;
  /** Overridable session-id generator, primarily for deterministic tests. */
  sessionIdFactory?: () => string;
}

/**
 * Implements the `initialize_secure_session` MCP tool: verifies the
 * caller's auth token, accepts the client's ephemeral ECDH public key,
 * generates the server's own ephemeral ECDH keypair, derives the shared
 * secret via ECDH, and stores it in a short-lived volatile cache keyed by
 * a freshly minted session id. Nothing here is persisted to disk.
 */
export class SecureSessionManager {
  private readonly verifyAuthToken: AuthTokenVerifier;
  private readonly nextSessionId: () => string;

  constructor(
    private readonly cache: SessionCache,
    options: SecureSessionManagerOptions = {},
  ) {
    this.verifyAuthToken = options.authTokenVerifier ?? defaultAuthTokenVerifier;
    this.nextSessionId = options.sessionIdFactory ?? (() => webcrypto.randomUUID());
  }

  async initializeSecureSession(
    request: InitializeSecureSessionRequest,
  ): Promise<InitializeSecureSessionResult> {
    const authorized = await this.verifyAuthToken(request.authToken);
    if (!authorized) {
      throw new AuthTokenError();
    }

    let clientPublicKey: CryptoKey;
    try {
      clientPublicKey = await webcrypto.subtle.importKey(
        'jwk',
        request.clientPublicKeyJwk,
        ECDH_PARAMS,
        false,
        [],
      );
    } catch (cause) {
      throw new InvalidClientKeyError(undefined, { cause });
    }

    // Fresh ephemeral keypair per session -- never reused, never persisted.
    const serverKeyPair = (await webcrypto.subtle.generateKey(ECDH_PARAMS, true, [
      'deriveBits',
    ])) as CryptoKeyPair;

    const sharedSecretBits = await webcrypto.subtle.deriveBits(
      { name: 'ECDH', public: clientPublicKey },
      serverKeyPair.privateKey,
      256,
    );

    const serverPublicKeyJwk = await webcrypto.subtle.exportKey('jwk', serverKeyPair.publicKey);

    const sessionId = this.nextSessionId();
    const record = this.cache.put(sessionId, new Uint8Array(sharedSecretBits));

    return {
      sessionId,
      serverPublicKeyJwk,
      expiresAt: record.expiresAt,
    };
  }
}
