import { webcrypto } from 'node:crypto';
/**
 * ECDH over the NIST P-256 curve, via Node's Web Crypto API implementation
 * (`node:crypto`'s `webcrypto` export -- the same `SubtleCrypto` surface a
 * browser or a Blazor WebAssembly core would call). No third-party crypto
 * library is used anywhere in this SDK.
 */
const ECDH_PARAMS = { name: 'ECDH', namedCurve: 'P-256' };
export class AuthTokenError extends Error {
    constructor(message = 'Invalid or missing user auth token') {
        super(message);
        this.name = 'AuthTokenError';
    }
}
export class InvalidClientKeyError extends Error {
    constructor(message = 'Client public key is not a valid ECDH P-256 JWK', options) {
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
export const defaultAuthTokenVerifier = (authToken) => {
    if (typeof authToken !== 'string')
        return false;
    return /^Bearer\s+(.{16,})$/.test(authToken.trim());
};
/**
 * Implements the `initialize_secure_session` MCP tool: verifies the
 * caller's auth token, accepts the client's ephemeral ECDH public key,
 * generates the server's own ephemeral ECDH keypair, derives the shared
 * secret via ECDH, and stores it in a short-lived volatile cache keyed by
 * a freshly minted session id. Nothing here is persisted to disk.
 */
export class SecureSessionManager {
    cache;
    verifyAuthToken;
    nextSessionId;
    constructor(cache, options = {}) {
        this.cache = cache;
        this.verifyAuthToken = options.authTokenVerifier ?? defaultAuthTokenVerifier;
        this.nextSessionId = options.sessionIdFactory ?? (() => webcrypto.randomUUID());
    }
    async initializeSecureSession(request) {
        const authorized = await this.verifyAuthToken(request.authToken);
        if (!authorized) {
            throw new AuthTokenError();
        }
        let clientPublicKey;
        try {
            clientPublicKey = await webcrypto.subtle.importKey('jwk', request.clientPublicKeyJwk, ECDH_PARAMS, false, []);
        }
        catch (cause) {
            throw new InvalidClientKeyError(undefined, { cause });
        }
        // Fresh ephemeral keypair per session -- never reused, never persisted.
        const serverKeyPair = (await webcrypto.subtle.generateKey(ECDH_PARAMS, true, [
            'deriveBits',
        ]));
        const sharedSecretBits = await webcrypto.subtle.deriveBits({ name: 'ECDH', public: clientPublicKey }, serverKeyPair.privateKey, 256);
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
