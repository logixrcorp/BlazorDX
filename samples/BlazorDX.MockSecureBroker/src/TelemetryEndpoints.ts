import { webcrypto } from 'node:crypto';
import express, { type Request, type Response, type Router } from 'express';
import { SessionCache } from './SessionCache.js';

// See SecureSessionManager.ts for why this is aliased from the `webcrypto`
// namespace instead of relying on DOM-lib globals.
type CryptoKey = webcrypto.CryptoKey;

// Same HKDF-over-shared-secret pattern as SecurePayloadWrapper, but with a
// distinct `info` string so the derived HMAC key is cryptographically
// independent of the AES payload key, even though both come from the same
// ECDH shared secret.
const HKDF_SALT = new TextEncoder().encode('BlazorDX.MockSecureBroker');
const HKDF_INFO = new TextEncoder().encode('BlazorDX.MockSecureBroker.telemetry-key.v1');

export type TelemetryEventType = 'access' | 'destruction';

export interface TelemetryProofPayload {
  sessionId: string;
  tokenId: string;
  event: TelemetryEventType;
  timestamp: number;
}

export interface AuditEvent extends TelemetryProofPayload {
  /** When the broker itself recorded the event (may differ from the
   *  client-supplied `timestamp`, which is attacker-controlled input). */
  recordedAt: number;
}

/** In-memory audit trail of verified telemetry events. A real broker would
 *  write these to durable, tamper-evident storage; this mock just keeps
 *  them in a list so tests (and manual `GET`-less curl inspection) can
 *  confirm what was recorded. */
export class AuditLog {
  private readonly events: AuditEvent[] = [];

  record(event: AuditEvent): void {
    this.events.push(event);
  }

  list(): readonly AuditEvent[] {
    return this.events;
  }

  clear(): void {
    this.events.length = 0;
  }
}

async function deriveHmacKey(sharedSecret: Uint8Array): Promise<CryptoKey> {
  const hkdfKeyMaterial = await webcrypto.subtle.importKey('raw', sharedSecret, 'HKDF', false, [
    'deriveKey',
  ]);
  return webcrypto.subtle.deriveKey(
    { name: 'HKDF', hash: 'SHA-256', salt: HKDF_SALT, info: HKDF_INFO },
    hkdfKeyMaterial,
    { name: 'HMAC', hash: 'SHA-256', length: 256 },
    false,
    ['sign', 'verify'],
  );
}

function canonicalMessage(payload: TelemetryProofPayload): Uint8Array {
  return new TextEncoder().encode(
    `${payload.sessionId}:${payload.tokenId}:${payload.event}:${payload.timestamp}`,
  );
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
export async function computeTelemetryProof(
  sharedSecret: Uint8Array,
  payload: TelemetryProofPayload,
): Promise<string> {
  const key = await deriveHmacKey(sharedSecret);
  const signature = await webcrypto.subtle.sign('HMAC', key, canonicalMessage(payload));
  return Buffer.from(signature).toString('base64url');
}

async function verifyTelemetryProof(
  sharedSecret: Uint8Array,
  payload: TelemetryProofPayload,
  proof: string,
): Promise<boolean> {
  let signature: Uint8Array;
  try {
    signature = new Uint8Array(Buffer.from(proof, 'base64url'));
  } catch {
    return false;
  }
  const key = await deriveHmacKey(sharedSecret);
  // SubtleCrypto.verify performs a constant-time comparison internally.
  return webcrypto.subtle.verify('HMAC', key, signature, canonicalMessage(payload));
}

interface TelemetryRequestBody {
  sessionId?: unknown;
  tokenId?: unknown;
  timestamp?: unknown;
  proof?: unknown;
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

async function handleTelemetryEvent(
  event: TelemetryEventType,
  cache: SessionCache,
  auditLog: AuditLog,
  req: Request<unknown, unknown, TelemetryRequestBody>,
  res: Response,
): Promise<void> {
  const { sessionId, tokenId, timestamp, proof } = req.body ?? {};

  if (
    !isNonEmptyString(sessionId) ||
    !isNonEmptyString(tokenId) ||
    !isNonEmptyString(proof) ||
    typeof timestamp !== 'number'
  ) {
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

  const payload: TelemetryProofPayload = { sessionId, tokenId, event, timestamp };
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
export function createTelemetryRouter(cache: SessionCache, auditLog: AuditLog): Router {
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
