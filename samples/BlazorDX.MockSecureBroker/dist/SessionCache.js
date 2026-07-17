const DEFAULT_TTL_MS = 5 * 60 * 1000; // 5 minutes -- deliberately short-lived.
/**
 * A volatile, in-memory, process-local cache of active secure sessions,
 * keyed by session id. This is the "short-lived volatile cache" the task
 * spec calls for: nothing here is persisted, and every entry self-expires.
 *
 * This is a reference/mock implementation. A real broker would back this
 * with a proper TTL store (e.g. Redis with EXPIRE) rather than an in-process
 * Map, so sessions survive process restarts and are visible across
 * horizontally-scaled instances.
 */
export class SessionCache {
    ttlMs;
    store = new Map();
    constructor(ttlMs = DEFAULT_TTL_MS) {
        this.ttlMs = ttlMs;
    }
    /** Stores a new session record, keyed by sessionId, expiring after ttlMs. */
    put(sessionId, sharedSecret, now = Date.now()) {
        const record = {
            sessionId,
            sharedSecret,
            createdAt: now,
            expiresAt: now + this.ttlMs,
        };
        this.store.set(sessionId, record);
        return record;
    }
    /** Returns the record for sessionId, or undefined if it's missing or has
     *  expired. An expired record is evicted as a side effect of reading it. */
    get(sessionId, now = Date.now()) {
        const record = this.store.get(sessionId);
        if (!record)
            return undefined;
        if (record.expiresAt <= now) {
            this.store.delete(sessionId);
            return undefined;
        }
        return record;
    }
    /** Removes a session immediately (e.g. after a confirmed destruction event). */
    delete(sessionId) {
        return this.store.delete(sessionId);
    }
    /** Number of entries currently held, including any not-yet-swept expired ones. */
    get size() {
        return this.store.size;
    }
    /** Evicts every expired entry proactively. Returns the count removed. */
    sweep(now = Date.now()) {
        let removed = 0;
        for (const [id, record] of this.store) {
            if (record.expiresAt <= now) {
                this.store.delete(id);
                removed++;
            }
        }
        return removed;
    }
}
