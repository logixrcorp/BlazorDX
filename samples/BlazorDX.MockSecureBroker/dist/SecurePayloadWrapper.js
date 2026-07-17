import { webcrypto } from 'node:crypto';
const IV_LENGTH_BYTES = 12; // 96-bit IV, the AES-GCM recommendation.
const TOKEN_VERSION = 1;
// Domain-separation constants for HKDF-derived AES key material. Using HKDF
// (rather than the raw ECDH secret directly) means the AES key is never the
// same bytes as the shared secret itself, and a future telemetry HMAC key
// (see TelemetryEndpoints.ts) derived from the *same* shared secret with a
// *different* info string is cryptographically independent of this one.
const HKDF_SALT = new TextEncoder().encode('BlazorDX.MockSecureBroker');
const HKDF_INFO = new TextEncoder().encode('BlazorDX.MockSecureBroker.payload-key.v1');
export class SessionNotFoundError extends Error {
    constructor(sessionId) {
        super(`No active secure session for sessionId "${sessionId}" (missing or expired)`);
        this.name = 'SessionNotFoundError';
    }
}
export class TokenDecryptionError extends Error {
    constructor(message = 'Payload token failed authentication (tampered, wrong session, or corrupt)', options) {
        super(message, options);
        this.name = 'TokenDecryptionError';
    }
}
async function deriveAesKey(sharedSecret, usages) {
    const hkdfKeyMaterial = await webcrypto.subtle.importKey('raw', sharedSecret, 'HKDF', false, [
        'deriveKey',
    ]);
    return webcrypto.subtle.deriveKey({ name: 'HKDF', hash: 'SHA-256', salt: HKDF_SALT, info: HKDF_INFO }, hkdfKeyMaterial, { name: 'AES-GCM', length: 256 }, false, usages);
}
function toBase64Url(bytes) {
    return Buffer.from(bytes).toString('base64url');
}
function fromBase64Url(value) {
    return new Uint8Array(Buffer.from(value, 'base64url'));
}
/**
 * Encrypts and decrypts payload plaintext (HTML/Markdown) using AES-256-GCM
 * with a key derived (via HKDF-SHA256) from a session's ECDH shared secret.
 * Every call to `encryptPayload` uses a fresh random IV; GCM's authentication
 * tag is carried inline in the ciphertext produced by SubtleCrypto and
 * verified automatically on decrypt -- a tampered token fails closed.
 */
export class SecurePayloadWrapper {
    cache;
    constructor(cache) {
        this.cache = cache;
    }
    getSharedSecret(sessionId) {
        const record = this.cache.get(sessionId);
        if (!record)
            throw new SessionNotFoundError(sessionId);
        return record.sharedSecret;
    }
    /**
     * Encrypts `plaintext` (HTML or Markdown) for the given session and
     * returns the opaque token string. This is the value this mock broker's
     * `wrap_secure_payload` tool call would hand back for delivery through
     * BlazorDX's McpProxyEndpoint at POST /mcp-proxy/deliver.
     */
    async encryptPayload(sessionId, plaintext) {
        const sharedSecret = this.getSharedSecret(sessionId);
        const key = await deriveAesKey(sharedSecret, ['encrypt']);
        const iv = webcrypto.getRandomValues(new Uint8Array(IV_LENGTH_BYTES));
        const plaintextBytes = new TextEncoder().encode(plaintext);
        const ciphertextWithTag = new Uint8Array(await webcrypto.subtle.encrypt({ name: 'AES-GCM', iv, tagLength: 128 }, key, plaintextBytes));
        // Frame: [version:1][iv:12][ciphertext + 128-bit auth tag]
        const framed = new Uint8Array(1 + iv.length + ciphertextWithTag.length);
        framed[0] = TOKEN_VERSION;
        framed.set(iv, 1);
        framed.set(ciphertextWithTag, 1 + iv.length);
        return { token: toBase64Url(framed) };
    }
    /**
     * Reverses `encryptPayload`. Included so this reference implementation
     * documents (and its tests can verify) the exact wire format a real
     * consumer would need to reproduce. BlazorDX's McpProxyEndpoint itself
     * never needs to call this -- it only relays the opaque token to the
     * client, which decrypts using its own copy of the shared secret.
     */
    async decryptPayload(sessionId, token) {
        const sharedSecret = this.getSharedSecret(sessionId);
        const framed = fromBase64Url(token);
        if (framed.length < 1 + IV_LENGTH_BYTES) {
            throw new TokenDecryptionError('Token is too short to contain a version byte, IV, and ciphertext');
        }
        const version = framed[0];
        if (version !== TOKEN_VERSION) {
            throw new TokenDecryptionError(`Unsupported token version ${version}`);
        }
        const iv = framed.slice(1, 1 + IV_LENGTH_BYTES);
        const ciphertextWithTag = framed.slice(1 + IV_LENGTH_BYTES);
        const key = await deriveAesKey(sharedSecret, ['decrypt']);
        try {
            const plaintextBytes = await webcrypto.subtle.decrypt({ name: 'AES-GCM', iv, tagLength: 128 }, key, ciphertextWithTag);
            return new TextDecoder().decode(plaintextBytes);
        }
        catch (cause) {
            throw new TokenDecryptionError(undefined, { cause });
        }
    }
}
