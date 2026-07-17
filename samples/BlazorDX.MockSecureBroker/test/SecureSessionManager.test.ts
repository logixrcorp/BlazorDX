import { webcrypto } from 'node:crypto';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionCache } from '../src/SessionCache.js';
import {
  AuthTokenError,
  InvalidClientKeyError,
  SecureSessionManager,
  defaultAuthTokenVerifier,
} from '../src/SecureSessionManager.js';

// @types/node declares these under the `webcrypto` namespace, not as DOM globals.
type JsonWebKey = webcrypto.JsonWebKey;
type CryptoKeyPair = webcrypto.CryptoKeyPair;

const ECDH_PARAMS = { name: 'ECDH', namedCurve: 'P-256' } as const;

async function generateClientKeyPair(): Promise<CryptoKeyPair> {
  return webcrypto.subtle.generateKey(ECDH_PARAMS, true, ['deriveBits']) as Promise<CryptoKeyPair>;
}

async function generateClientJwk(): Promise<JsonWebKey> {
  const { publicKey } = await generateClientKeyPair();
  return webcrypto.subtle.exportKey('jwk', publicKey);
}

describe('defaultAuthTokenVerifier', () => {
  it('accepts a well-formed Bearer token', () => {
    expect(defaultAuthTokenVerifier('Bearer abcdefghijklmnopqrstuvwxyz')).toBe(true);
  });

  it('rejects a missing Bearer prefix', () => {
    expect(defaultAuthTokenVerifier('abcdefghijklmnopqrstuvwxyz')).toBe(false);
  });

  it('rejects a token shorter than 16 characters', () => {
    expect(defaultAuthTokenVerifier('Bearer short')).toBe(false);
  });

  it('rejects a non-string token', () => {
    // @ts-expect-error - exercising the runtime guard against non-string input
    expect(defaultAuthTokenVerifier(undefined)).toBe(false);
  });
});

describe('SecureSessionManager.initializeSecureSession', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('rejects an invalid auth token before touching crypto', async () => {
    const cache = new SessionCache();
    const manager = new SecureSessionManager(cache);
    const clientPublicKeyJwk = await generateClientJwk();
    const generateKeySpy = vi.spyOn(webcrypto.subtle, 'generateKey');

    await expect(
      manager.initializeSecureSession({ clientPublicKeyJwk, authToken: 'nope' }),
    ).rejects.toThrow(AuthTokenError);

    expect(generateKeySpy).not.toHaveBeenCalled();
    expect(cache.size).toBe(0);
  });

  it('rejects a malformed client public key', async () => {
    const cache = new SessionCache();
    const manager = new SecureSessionManager(cache);

    await expect(
      manager.initializeSecureSession({
        clientPublicKeyJwk: { kty: 'not-a-real-key' } as unknown as JsonWebKey,
        authToken: 'Bearer 0123456789abcdef',
      }),
    ).rejects.toThrow(InvalidClientKeyError);

    expect(cache.size).toBe(0);
  });

  it('creates a session with a fresh ECDH keypair and stores the correct shared secret', async () => {
    const cache = new SessionCache();
    const manager = new SecureSessionManager(cache);
    const clientKeyPair = await generateClientKeyPair();
    const clientPublicKeyJwk = await webcrypto.subtle.exportKey('jwk', clientKeyPair.publicKey);

    const result = await manager.initializeSecureSession({
      clientPublicKeyJwk,
      authToken: 'Bearer 0123456789abcdef',
    });

    expect(result.sessionId).toMatch(/^[0-9a-f-]{36}$/i);
    expect(result.serverPublicKeyJwk.kty).toBe('EC');
    expect(result.expiresAt).toBeGreaterThan(Date.now());

    const record = cache.get(result.sessionId);
    expect(record).toBeDefined();
    expect(record?.sharedSecret).toBeInstanceOf(Uint8Array);
    expect(record?.sharedSecret.length).toBe(32); // 256-bit ECDH shared secret

    // The client independently derives the same shared secret from the
    // server's public key + its own private key -- the crux of ECDH.
    const serverPublicKey = await webcrypto.subtle.importKey(
      'jwk',
      result.serverPublicKeyJwk,
      ECDH_PARAMS,
      false,
      [],
    );
    const clientSideSecret = new Uint8Array(
      await webcrypto.subtle.deriveBits(
        { name: 'ECDH', public: serverPublicKey },
        clientKeyPair.privateKey,
        256,
      ),
    );
    expect(clientSideSecret).toEqual(record?.sharedSecret);
  });

  it('uses a custom auth token verifier when supplied', async () => {
    const cache = new SessionCache();
    const verifier = vi.fn().mockReturnValue(true);
    const manager = new SecureSessionManager(cache, { authTokenVerifier: verifier });
    const clientPublicKeyJwk = await generateClientJwk();

    await manager.initializeSecureSession({ clientPublicKeyJwk, authToken: 'whatever' });

    expect(verifier).toHaveBeenCalledWith('whatever');
  });

  it('generates a distinct session id for every call via an injectable factory', async () => {
    const cache = new SessionCache();
    let counter = 0;
    const manager = new SecureSessionManager(cache, {
      sessionIdFactory: () => `fixed-session-${++counter}`,
    });
    const clientPublicKeyJwk = await generateClientJwk();

    const first = await manager.initializeSecureSession({
      clientPublicKeyJwk,
      authToken: 'Bearer 0123456789abcdef',
    });
    const second = await manager.initializeSecureSession({
      clientPublicKeyJwk,
      authToken: 'Bearer 0123456789abcdef',
    });

    expect(first.sessionId).toBe('fixed-session-1');
    expect(second.sessionId).toBe('fixed-session-2');
  });

  it('propagates unexpected crypto failures instead of swallowing them', async () => {
    const cache = new SessionCache();
    const manager = new SecureSessionManager(cache);
    const clientPublicKeyJwk = await generateClientJwk();

    vi.spyOn(webcrypto.subtle, 'generateKey').mockRejectedValueOnce(
      new Error('simulated HSM outage'),
    );

    await expect(
      manager.initializeSecureSession({
        clientPublicKeyJwk,
        authToken: 'Bearer 0123456789abcdef',
      }),
    ).rejects.toThrow('simulated HSM outage');
  });
});
