import { describe, expect, it } from 'vitest';
import { SessionCache } from '../src/SessionCache.js';
import {
  SecurePayloadWrapper,
  SessionNotFoundError,
  TokenDecryptionError,
} from '../src/SecurePayloadWrapper.js';

function fakeSharedSecret(byte = 0x42): Uint8Array {
  return new Uint8Array(32).fill(byte);
}

describe('SecurePayloadWrapper', () => {
  it('encrypts plaintext into an opaque, non-plaintext-leaking token', async () => {
    const cache = new SessionCache();
    cache.put('session-1', fakeSharedSecret());
    const wrapper = new SecurePayloadWrapper(cache);

    const plaintext = '<h1>Confidential board memo</h1>';
    const { token } = await wrapper.encryptPayload('session-1', plaintext);

    expect(typeof token).toBe('string');
    expect(token.length).toBeGreaterThan(0);
    expect(token).not.toContain('Confidential');
    expect(token).toMatch(/^[A-Za-z0-9_-]+$/); // base64url alphabet only
  });

  it('produces a different token every time (random IV) even for identical plaintext', async () => {
    const cache = new SessionCache();
    cache.put('session-1', fakeSharedSecret());
    const wrapper = new SecurePayloadWrapper(cache);

    const first = await wrapper.encryptPayload('session-1', 'same payload');
    const second = await wrapper.encryptPayload('session-1', 'same payload');

    expect(first.token).not.toBe(second.token);
  });

  it('round-trips through decryptPayload back to the original plaintext', async () => {
    const cache = new SessionCache();
    cache.put('session-1', fakeSharedSecret());
    const wrapper = new SecurePayloadWrapper(cache);

    const plaintext = '# Markdown payload\n\nWith **formatting** and unicode: café, 日本語';
    const { token } = await wrapper.encryptPayload('session-1', plaintext);
    const decrypted = await wrapper.decryptPayload('session-1', token);

    expect(decrypted).toBe(plaintext);
  });

  it('rejects encryption for an unknown or expired session', async () => {
    const cache = new SessionCache();
    const wrapper = new SecurePayloadWrapper(cache);

    await expect(wrapper.encryptPayload('ghost-session', 'x')).rejects.toThrow(
      SessionNotFoundError,
    );
  });

  it('rejects decryption for an unknown or expired session', async () => {
    const cache = new SessionCache();
    cache.put('session-1', fakeSharedSecret());
    const wrapper = new SecurePayloadWrapper(cache);
    const { token } = await wrapper.encryptPayload('session-1', 'x');

    cache.delete('session-1');

    await expect(wrapper.decryptPayload('session-1', token)).rejects.toThrow(
      SessionNotFoundError,
    );
  });

  it('fails closed (auth-tag verification) if the token is tampered with', async () => {
    const cache = new SessionCache();
    cache.put('session-1', fakeSharedSecret());
    const wrapper = new SecurePayloadWrapper(cache);

    const { token } = await wrapper.encryptPayload('session-1', 'sensitive content');
    const tamperedBytes = Buffer.from(token, 'base64url');
    tamperedBytes[tamperedBytes.length - 1] ^= 0xff; // flip a bit in the auth tag
    const tamperedToken = tamperedBytes.toString('base64url');

    await expect(wrapper.decryptPayload('session-1', tamperedToken)).rejects.toThrow(
      TokenDecryptionError,
    );
  });

  it("fails to decrypt a token encrypted under a different session's key", async () => {
    const cache = new SessionCache();
    cache.put('session-a', fakeSharedSecret(0x11));
    cache.put('session-b', fakeSharedSecret(0x22));
    const wrapper = new SecurePayloadWrapper(cache);

    const { token } = await wrapper.encryptPayload('session-a', 'secret for A only');

    await expect(wrapper.decryptPayload('session-b', token)).rejects.toThrow(
      TokenDecryptionError,
    );
  });

  it('rejects a structurally invalid (too short) token', async () => {
    const cache = new SessionCache();
    cache.put('session-1', fakeSharedSecret());
    const wrapper = new SecurePayloadWrapper(cache);

    await expect(wrapper.decryptPayload('session-1', 'AA')).rejects.toThrow(
      TokenDecryptionError,
    );
  });

  it('rejects a token with an unsupported version byte', async () => {
    const cache = new SessionCache();
    cache.put('session-1', fakeSharedSecret());
    const wrapper = new SecurePayloadWrapper(cache);

    const { token } = await wrapper.encryptPayload('session-1', 'x');
    const bytes = Buffer.from(token, 'base64url');
    bytes[0] = 0xff; // corrupt the version byte
    const badVersionToken = bytes.toString('base64url');

    await expect(wrapper.decryptPayload('session-1', badVersionToken)).rejects.toThrow(
      TokenDecryptionError,
    );
  });
});
