import type { Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import express from 'express';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SessionCache } from '../src/SessionCache.js';
import {
  AuditLog,
  computeTelemetryProof,
  createTelemetryRouter,
  type TelemetryProofPayload,
} from '../src/TelemetryEndpoints.js';

function fakeSharedSecret(byte = 0x42): Uint8Array {
  return new Uint8Array(32).fill(byte);
}

describe('Telemetry webhooks (/api/telemetry/access, /api/telemetry/destruction)', () => {
  let cache: SessionCache;
  let auditLog: AuditLog;
  let server: Server;
  let baseUrl: string;

  beforeEach(async () => {
    cache = new SessionCache();
    auditLog = new AuditLog();
    const app = express();
    app.use('/api/telemetry', createTelemetryRouter(cache, auditLog));

    await new Promise<void>((resolve) => {
      server = app.listen(0, resolve);
    });
    const { port } = server.address() as AddressInfo;
    baseUrl = `http://127.0.0.1:${port}`;
  });

  afterEach(async () => {
    await new Promise<void>((resolve, reject) => {
      server.close((err) => (err ? reject(err) : resolve()));
    });
  });

  async function post(path: string, body: unknown) {
    const response = await fetch(`${baseUrl}${path}`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body),
    });
    return { status: response.status, body: (await response.json()) as Record<string, unknown> };
  }

  it('accepts a validly proven access event and logs it to the audit log', async () => {
    const sharedSecret = fakeSharedSecret();
    cache.put('session-1', sharedSecret);
    const payload: TelemetryProofPayload = {
      sessionId: 'session-1',
      tokenId: 'token-abc',
      event: 'access',
      timestamp: 1_700_000_000_000,
    };
    const proof = await computeTelemetryProof(sharedSecret, payload);

    const { status, body } = await post('/api/telemetry/access', { ...payload, proof });

    expect(status).toBe(202);
    expect(body).toMatchObject({ status: 'recorded', event: 'access' });
    expect(auditLog.list()).toHaveLength(1);
    expect(auditLog.list()[0]).toMatchObject({
      sessionId: 'session-1',
      tokenId: 'token-abc',
      event: 'access',
    });
  });

  it('accepts a validly proven destruction event on its own route', async () => {
    const sharedSecret = fakeSharedSecret(0x77);
    cache.put('session-2', sharedSecret);
    const payload: TelemetryProofPayload = {
      sessionId: 'session-2',
      tokenId: 'token-xyz',
      event: 'destruction',
      timestamp: 1_700_000_001_000,
    };
    const proof = await computeTelemetryProof(sharedSecret, payload);

    const { status, body } = await post('/api/telemetry/destruction', { ...payload, proof });

    expect(status).toBe(202);
    expect(body).toMatchObject({ status: 'recorded', event: 'destruction' });
  });

  it('rejects a request for an unknown session with 404 and does not log it', async () => {
    const { status } = await post('/api/telemetry/access', {
      sessionId: 'ghost',
      tokenId: 'token-abc',
      event: 'access',
      timestamp: Date.now(),
      proof: 'not-a-real-proof',
    });

    expect(status).toBe(404);
    expect(auditLog.list()).toHaveLength(0);
  });

  it('rejects an invalid cryptographic proof with 401 and does not log it', async () => {
    const sharedSecret = fakeSharedSecret();
    cache.put('session-1', sharedSecret);

    const { status, body } = await post('/api/telemetry/access', {
      sessionId: 'session-1',
      tokenId: 'token-abc',
      timestamp: 1_700_000_000_000,
      proof: 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
    });

    expect(status).toBe(401);
    expect(String(body.error)).toMatch(/proof/i);
    expect(auditLog.list()).toHaveLength(0);
  });

  it('rejects a proof computed for a different event type (access proof replayed against destruction)', async () => {
    const sharedSecret = fakeSharedSecret();
    cache.put('session-1', sharedSecret);
    const payload: TelemetryProofPayload = {
      sessionId: 'session-1',
      tokenId: 'token-abc',
      event: 'access',
      timestamp: 1_700_000_000_000,
    };
    const accessProof = await computeTelemetryProof(sharedSecret, payload);

    const { status } = await post('/api/telemetry/destruction', {
      ...payload,
      proof: accessProof,
    });

    expect(status).toBe(401);
  });

  it('rejects a malformed request body with 400', async () => {
    const { status } = await post('/api/telemetry/access', { sessionId: 'session-1' });
    expect(status).toBe(400);
  });
});
