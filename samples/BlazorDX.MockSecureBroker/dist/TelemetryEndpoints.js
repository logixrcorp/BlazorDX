import { webcrypto } from 'node:crypto';
import express from 'express';
// Same HKDF-over-shared-secret pattern as SecurePayloadWrapper, but with a
// distinct `info` string so the derived HMAC key is cryptographically
// independent of the AES payload key, even though both come from the same
// ECDH shared secret.
const HKDF_SALT = new TextEncoder().encode('BlazorDX.MockSecureBroker');
const HKDF_INFO = new TextEncoder().encode('BlazorDX.MockSecureBroker.telemetry-key.v1');
/** In-memory audit trail of verified telemetry events. A real broker would
 *  write these to durable, tamper-evident storage; this mock just keeps
 *  them in a list so tests (and manual `GET`-less curl inspection) can
 *  confirm what was recorded. */
export class AuditLog {
    events = [];
    record(event) {
        this.events.push(event);
    }
    list() {
        return this.events;
    }
    clear() {
        this.events.length = 0;
    }
}
async function deriveHmacKey(sharedSecret) {
    const hkdfKeyMaterial = await webcrypto.subtle.importKey('raw', sharedSecret, 'HKDF', false, [
        'deriveKey',
    ]);
    return webcrypto.subtle.deriveKey({ name: 'HKDF', hash: 'SHA-256', salt: HKDF_SALT, info: HKDF_INFO }, hkdfKeyMaterial, { name: 'HMAC', hash: 'SHA-256', length: 256 }, false, ['sign', 'verify']);
}
function canonicalMessage(payload) {
    return new TextEncoder().encode(`${payload.sessionId}:${payload.tokenId}:${payload.event}:${payload.timestamp}`);
}
/**
 * Computes the cryptographic proof a client Wasm core attaches to a
 * telemetry webhook call: an HMAC-SHA256 (keyed by a value derived from the
 * session's ECDH shared secret) over the event's canonical fields. Only a
 * party that completed the ECDH exchange for this session can produce a
 * valid proof, which is what lets the broker trust that the payload really
 * was read (`access`) or securely zeroed (`destruction`) by the legitimate
 * client -- not merely that *some* HTTP caller says so.
 *
 * Exported so tests -- and real client implementations -- can construct
 * valid proofs the same way.
 */
export async function computeTelemetryProof(sharedSecret, payload) {
    const key = await deriveHmacKey(sharedSecret);
    const signature = await webcrypto.subtle.sign('HMAC', key, canonicalMessage(payload));
    return Buffer.from(signature).toString('base64url');
}
async function verifyTelemetryProof(sharedSecret, payload, proof) {
    let signature;
    try {
        signature = new Uint8Array(Buffer.from(proof, 'base64url'));
    }
    catch {
        return false;
    }
    const key = await deriveHmacKey(sharedSecret);
    // SubtleCrypto.verify performs a constant-time comparison internally.
    return webcrypto.subtle.verify('HMAC', key, signature, canonicalMessage(payload));
}
function isNonEmptyString(value) {
    return typeof value === 'string' && value.length > 0;
}
async function handleTelemetryEvent(event, cache, auditLog, req, res) {
    const { sessionId, tokenId, timestamp, proof } = req.body ?? {};
    if (!isNonEmptyString(sessionId) ||
        !isNonEmptyString(tokenId) ||
        !isNonEmptyString(proof) ||
        typeof timestamp !== 'number') {
        res
            .status(400)
            .json({ error: 'sessionId, tokenId, timestamp (number), and proof are required' });
        return;
    }
    const record = cache.get(sessionId);
    if (!record) {
        res.status(404).json({ error: `No active secure session for sessionId "${sessionId}"` });
        return;
    }
    const payload = { sessionId, tokenId, event, timestamp };
    const isValid = await verifyTelemetryProof(record.sharedSecret, payload, proof);
    if (!isValid) {
        res.status(401).json({ error: 'Cryptographic proof did not verify against the session key' });
        return;
    }
    auditLog.record({ ...payload, recordedAt: Date.now() });
    res.status(202).json({ status: 'recorded', event });
}
/**
 * Express router exposing the two telemetry webhooks a client Wasm core
 * calls to prove -- with a cryptographic signature, not just a claim -- that
 * a delivered payload was read (`/access`) or securely zeroed
 * (`/destruction`). Mount under `/api/telemetry`.
 */
export function createTelemetryRouter(cache, auditLog) {
    const router = express.Router();
    router.use(express.json());
    router.post('/access', (req, res, next) => {
        handleTelemetryEvent('access', cache, auditLog, req, res).catch(next);
    });
    router.post('/destruction', (req, res, next) => {
        handleTelemetryEvent('destruction', cache, auditLog, req, res).catch(next);
    });
    return router;
}
