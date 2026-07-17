/** A single active secure session: the raw ECDH shared secret plus its
 *  lifetime, kept only in memory (never persisted) for the life of one
 *  ephemeral chat exchange. */
export interface SessionRecord {
  readonly sessionId: string;
  readonly sharedSecret: Uint8Array;
  readonly createdAt: number;
  readonly expiresAt: number;
}
