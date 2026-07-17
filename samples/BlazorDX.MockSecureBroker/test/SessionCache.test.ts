import { describe, expect, it } from 'vitest';
import { SessionCache } from '../src/SessionCache.js';

describe('SessionCache', () => {
  it('stores and retrieves a session record by id', () => {
    const cache = new SessionCache();
    const secret = new Uint8Array([1, 2, 3]);
    const record = cache.put('session-1', secret);

    expect(cache.get('session-1')).toEqual(record);
    expect(record.sharedSecret).toBe(secret);
  });

  it('returns undefined for an unknown session id', () => {
    const cache = new SessionCache();
    expect(cache.get('does-not-exist')).toBeUndefined();
  });

  it('expires entries after the configured TTL', () => {
    const cache = new SessionCache(1000);
    const now = 1_000_000;
    cache.put('session-1', new Uint8Array([9]), now);

    expect(cache.get('session-1', now + 999)).toBeDefined();
    expect(cache.get('session-1', now + 1000)).toBeUndefined();
  });

  it('removes an entry from the store once it has been observed as expired', () => {
    const cache = new SessionCache(100);
    cache.put('session-1', new Uint8Array([9]), 0);

    expect(cache.get('session-1', 200)).toBeUndefined();
    expect(cache.size).toBe(0);
  });

  it('sweep() removes all expired entries and reports the count removed', () => {
    const cache = new SessionCache(100);
    cache.put('a', new Uint8Array([1]), 0);
    cache.put('b', new Uint8Array([2]), 0);
    cache.put('c', new Uint8Array([3]), 500);

    const removed = cache.sweep(150);

    expect(removed).toBe(2);
    expect(cache.size).toBe(1);
    expect(cache.get('c', 500)).toBeDefined();
  });

  it('delete() removes a session and reports whether it existed', () => {
    const cache = new SessionCache();
    cache.put('session-1', new Uint8Array([1]));

    expect(cache.delete('session-1')).toBe(true);
    expect(cache.delete('session-1')).toBe(false);
  });
});
