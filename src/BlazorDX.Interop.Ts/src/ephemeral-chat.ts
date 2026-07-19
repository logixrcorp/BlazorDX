// The DOM bridge for the "Zero-Trust, Ephemeral AI Chat Conduit."
//
// Decryption happens entirely in the dx_security wasm module (see
// rust-loader.ts); this file never sees a raw AES key. Its job is:
//   1. Move ciphertext/keys into wasm memory and call the Rust exports.
//   2. Decode the returned plaintext and inject it into an isolated,
//      closed-mode Shadow DOM node -- never into the Blazor virtual DOM and
//      never via innerHTML (plain textContent only; the payload is a chat
//      message, not markup we trust).
//   3. Watch that node with a MutationObserver: any mutation we did not
//      originate ourselves is treated as tampering and self-destructs the
//      node immediately.
//   4. Listen for server-pushed lifecycle events over the native EventSource
//      against `${eventsBaseUrl}/{sessionId}` -- no SignalR. Both WITHDRAW
//      and REFRESH arrive as a single `security/lifecycle` SSE event
//      carrying an `action` field, matching the whitepaper's wire schema
//      (see docs/adr/0016). Both are HMAC-signed by the broker (the
//      session's signing key, the same one derived during the ECDH
//      handshake) and verified here before being acted on -- an
//      unsigned/forged control signal cannot force a teardown or a refresh
//      the way an unauthenticated Conduit relay could otherwise be tricked
//      into forwarding. An unrecognized `action` on this trusted channel is
//      itself treated as tampering, not silently ignored.
//   5. Always call `clear_payload` on the wasm buffer, even on failure, so
//      the plaintext is zeroized in Rust-owned memory the moment we are done
//      copying it out.
//   6. Sign and (best-effort, non-blocking) POST an Access Receipt right
//      after a successful mount, and a Destruction Receipt at the moment the
//      session is finally torn down (WITHDRAW, tamper, unmount, or TTL
//      expiry) -- the Proof-of-Destruction protocol. A telemetry POST
//      failure never blocks or breaks the chat itself.
//   7. Optionally self-destruct after a caller-supplied TTL (seconds) with
//      no other trigger required -- a client-scheduled timer, since the
//      Conduit relay never holds session state to expire on its own.

import { ensureSecurityWasm, securityWasm, type SecurityWasmExports } from "./rust-loader";

const PUBLIC_KEY_LEN = 65; // uncompressed SEC1 P-256 point
const NONCE_LEN = 12; // AES-GCM standard nonce size
const SEED_LEN = 32; // P-256 scalar / AES-256 key size
const SIGNATURE_LEN = 32; // HMAC-SHA256 output size
const USIZE_BYTES = 4; // wasm32 pointer width

interface MountedSession {
  sessionId: string;
  telemetryBaseUrl: string | null;
  shadowRoot: ShadowRoot;
  observer: MutationObserver;
  eventSource: EventSource | null;
  ttlTimer: ReturnType<typeof setTimeout> | null;
  onWithdraw: () => void;
  onRefresh: () => void;
  onTamper: () => void;
  destroyed: boolean;
}

// One entry per currently-mounted host element id. Looked up by both
// decryptAndMount (to register) and scrubNode (to tear down).
const mounted = new Map<string, MountedSession>();

function base64ToBytes(base64: string): Uint8Array {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = "";
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary);
}

// Copies `bytes` into a freshly wasm-allocated buffer and returns its pointer.
// The caller owns the buffer and must free it (via dealloc, unless it is the
// decrypt_payload result, which must go through clear_payload instead).
function writeBytes(wasm: SecurityWasmExports, bytes: Uint8Array): number {
  const pointer = wasm.alloc(bytes.length || 1);
  new Uint8Array(wasm.memory.buffer, pointer, bytes.length).set(bytes);
  return pointer;
}

function readU32(wasm: SecurityWasmExports, pointer: number): number {
  return new DataView(wasm.memory.buffer).getUint32(pointer, true);
}

// The exact byte sequence every signature in this module is computed/checked over: binds a
// signature to both the session and the specific action, so a signature for one cannot be
// replayed as proof of another (see session.rs's verify_and_end_rejects_a_signature_for_the_wrong_message).
function controlMessage(sessionId: string, action: string): Uint8Array {
  return new TextEncoder().encode(`${sessionId}|${action}`);
}

// --- telemetry (Proof of Destruction / Access Confirmation receipts) --------
// Fire-and-forget by design (S5 "Telemetry_Pulse" is explicitly non-blocking): a broker/network
// hiccup must never block mounting or teardown. `keepalive: true` lets a destruction receipt's
// request survive the page/tab tearing down right as it fires.

function postTelemetryReceipt(telemetryBaseUrl: string | null, path: "access" | "destruction", body: unknown): void {
  if (telemetryBaseUrl === null) {
    return;
  }
  const base = telemetryBaseUrl.replace(/\/+$/, "");
  fetch(`${base}/${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
    keepalive: true,
  }).catch(() => {
    // Best-effort: never let a telemetry failure surface to the chat UI or block anything.
  });
}

// --- wasm-backed signing/verification helpers --------------------------------
// Each wraps one FFI call with its own alloc/dealloc cycle, independent of any session-id
// buffer a caller elsewhere might still be holding -- signing/verification can happen at any
// point after complete_session, long after the pointers used during the handshake are freed.

function signWithWasm(wasm: SecurityWasmExports, sessionId: string, message: Uint8Array): Uint8Array | null {
  const sessionIdBytes = new TextEncoder().encode(sessionId);
  const sessionIdPointer = writeBytes(wasm, sessionIdBytes);
  const messagePointer = writeBytes(wasm, message);
  const outSigPointer = wasm.alloc(SIGNATURE_LEN);
  try {
    const status = wasm.sign(sessionIdPointer, sessionIdBytes.length, messagePointer, message.length, outSigPointer);
    if (status !== 0) {
      return null;
    }
    return new Uint8Array(wasm.memory.buffer, outSigPointer, SIGNATURE_LEN).slice();
  } finally {
    wasm.dealloc(sessionIdPointer, sessionIdBytes.length || 1);
    wasm.dealloc(messagePointer, message.length || 1);
    wasm.dealloc(outSigPointer, SIGNATURE_LEN);
  }
}

// For REFRESH: verifies without ending the session (unlike WITHDRAW, REFRESH must not tear the
// mount down). Returns null if there was no signing key to check against (session already gone).
function verifySignalWithWasm(
  wasm: SecurityWasmExports,
  sessionId: string,
  message: Uint8Array,
  signature: Uint8Array,
): boolean | null {
  const sessionIdBytes = new TextEncoder().encode(sessionId);
  const sessionIdPointer = writeBytes(wasm, sessionIdBytes);
  const messagePointer = writeBytes(wasm, message);
  const signaturePointer = writeBytes(wasm, signature);
  const outValidPointer = wasm.alloc(1);
  try {
    const status = wasm.verify_signal(
      sessionIdPointer,
      sessionIdBytes.length,
      messagePointer,
      message.length,
      signaturePointer,
      signature.length || 1,
      outValidPointer,
    );
    if (status !== 0) {
      return null;
    }
    return new Uint8Array(wasm.memory.buffer, outValidPointer, 1)[0] === 1;
  } finally {
    wasm.dealloc(sessionIdPointer, sessionIdBytes.length || 1);
    wasm.dealloc(messagePointer, message.length || 1);
    wasm.dealloc(signaturePointer, signature.length || 1);
    wasm.dealloc(outValidPointer, 1);
  }
}

// For WITHDRAW: verifies the incoming signature AND signs a destruction receipt in the same
// atomic wasm call (the signing key would otherwise already be gone by the time a separate
// destruction-signing call ran), then unconditionally ends the session. Returns null if there
// was no signing key to check against (e.g. a duplicate WITHDRAW after the session already ended).
function verifyAndEndWithWasm(
  wasm: SecurityWasmExports,
  sessionId: string,
  message: Uint8Array,
  signature: Uint8Array,
  destructionMessage: Uint8Array,
): { valid: boolean; destructionSignature: Uint8Array } | null {
  const sessionIdBytes = new TextEncoder().encode(sessionId);
  const sessionIdPointer = writeBytes(wasm, sessionIdBytes);
  const messagePointer = writeBytes(wasm, message);
  const signaturePointer = writeBytes(wasm, signature);
  const destructionMessagePointer = writeBytes(wasm, destructionMessage);
  const outValidPointer = wasm.alloc(1);
  const outDestructionSigPointer = wasm.alloc(SIGNATURE_LEN);
  try {
    const status = wasm.verify_and_end_session(
      sessionIdPointer,
      sessionIdBytes.length,
      messagePointer,
      message.length,
      signaturePointer,
      signature.length || 1,
      destructionMessagePointer,
      destructionMessage.length,
      outValidPointer,
      outDestructionSigPointer,
    );
    if (status !== 0) {
      return null;
    }
    return {
      valid: new Uint8Array(wasm.memory.buffer, outValidPointer, 1)[0] === 1,
      destructionSignature: new Uint8Array(wasm.memory.buffer, outDestructionSigPointer, SIGNATURE_LEN).slice(),
    };
  } finally {
    wasm.dealloc(sessionIdPointer, sessionIdBytes.length || 1);
    wasm.dealloc(messagePointer, message.length || 1);
    wasm.dealloc(signaturePointer, signature.length || 1);
    wasm.dealloc(destructionMessagePointer, destructionMessage.length || 1);
    wasm.dealloc(outValidPointer, 1);
    wasm.dealloc(outDestructionSigPointer, SIGNATURE_LEN);
  }
}

// For tamper detection and component unmount: no incoming signal to check, just sign a
// destruction receipt and end the session in one call. Returns null if there was nothing left
// to sign against (already torn down by something else).
function endWithReceiptWithWasm(wasm: SecurityWasmExports, sessionId: string, message: Uint8Array): Uint8Array | null {
  const sessionIdBytes = new TextEncoder().encode(sessionId);
  const sessionIdPointer = writeBytes(wasm, sessionIdBytes);
  const messagePointer = writeBytes(wasm, message);
  const outSigPointer = wasm.alloc(SIGNATURE_LEN);
  try {
    const status = wasm.end_with_receipt(
      sessionIdPointer,
      sessionIdBytes.length,
      messagePointer,
      message.length,
      outSigPointer,
    );
    if (status !== 0) {
      return null;
    }
    return new Uint8Array(wasm.memory.buffer, outSigPointer, SIGNATURE_LEN).slice();
  } finally {
    wasm.dealloc(sessionIdPointer, sessionIdBytes.length || 1);
    wasm.dealloc(messagePointer, message.length || 1);
    wasm.dealloc(outSigPointer, SIGNATURE_LEN);
  }
}

function destructionReceiptBody(sessionId: string, trigger: string, signature: Uint8Array) {
  return {
    sessionId,
    eventType: "MEMORY_ZEROED",
    timestamp: new Date().toISOString(),
    trigger,
    clientSignature: bytesToBase64(signature),
  };
}

// Wipes and detaches every child of a shadow root. Removing the node stops it
// being reachable at all; overwriting first is a best-effort defense-in-depth
// step, since JS strings are immutable and this cannot guarantee the original
// string's heap bytes are zeroed the way the wasm buffer genuinely is.
function overwriteShadowContent(shadowRoot: ShadowRoot): void {
  for (const node of Array.from(shadowRoot.childNodes)) {
    if (node.nodeType === Node.TEXT_NODE && node.textContent !== null) {
      node.textContent = " ".repeat(node.textContent.length);
    }
    shadowRoot.removeChild(node);
  }
}

// DOM-only teardown: disconnects the observer, scrubs the shadow root, closes the EventSource,
// and removes the map entry. Never touches wasm -- callers that need a signed Destruction
// Receipt call the wasm sign/end step themselves first (see destroySessionWithReceipt and the
// WITHDRAW listener, which has its own incoming signature to check at the same time).
function destroySession(hostElementId: string, notifyTamper: boolean): void {
  const entry = mounted.get(hostElementId);
  if (entry === undefined || entry.destroyed) {
    return;
  }
  entry.destroyed = true;
  entry.observer.disconnect();
  overwriteShadowContent(entry.shadowRoot);
  entry.eventSource?.close();
  if (entry.ttlTimer !== null) {
    clearTimeout(entry.ttlTimer);
  }
  mounted.delete(hostElementId);
  if (notifyTamper) {
    entry.onTamper();
  }
}

// Signs a Destruction Receipt for `trigger` (TAMPER_DETECTED / COMPONENT_UNMOUNT -- WITHDRAW
// goes through verifyAndEndWithWasm instead, which already has an incoming signature to check
// in the same atomic call), tears the session down, and posts the receipt.
function destroySessionWithReceipt(
  hostElementId: string,
  sessionId: string,
  telemetryBaseUrl: string | null,
  trigger: string,
  notifyTamper: boolean,
): void {
  const signature = endWithReceiptWithWasm(securityWasm(), sessionId, controlMessage(sessionId, trigger));
  destroySession(hostElementId, notifyTamper);
  if (signature !== null) {
    postTelemetryReceipt(telemetryBaseUrl, "destruction", destructionReceiptBody(sessionId, trigger, signature));
  }
}

function startTamperObserver(
  hostElementId: string,
  shadowRoot: ShadowRoot,
  sessionId: string,
  telemetryBaseUrl: string | null,
): MutationObserver {
  const observer = new MutationObserver(() => {
    // Any mutation reaching here was not made by scrubNode/destroySession
    // (which disconnect the observer before touching the DOM), so it is
    // necessarily an outside actor -- self-destruct immediately.
    destroySessionWithReceipt(hostElementId, sessionId, telemetryBaseUrl, "TAMPER_DETECTED", /* notifyTamper */ true);
  });
  observer.observe(shadowRoot, { childList: true, subtree: true, characterData: true, attributes: true });
  return observer;
}

// Parses `{"action":"WITHDRAW"|"REFRESH", "correlationId":"...", "signature":"<base64>"}` from
// a `security/lifecycle` SSE frame's data field -- matching the whitepaper's §8.3 wire schema.
// `correlationId` is accepted but not required here: this bridge is always already scoped to
// one sessionId (the EventSource URL itself), so the field is informational rather than a
// second source of truth. Any parse failure (malformed JSON, missing/non-string fields, not
// even valid base64 once decoded) degrades to a null action and/or empty signature, which
// simply fails verification below rather than throwing -- a malformed control frame is exactly
// as untrusted as a well-formed-but-wrong one.
function parseLifecycleEventData(data: string): { action: string | null; signature: Uint8Array } {
  try {
    const parsed: unknown = JSON.parse(data);
    if (typeof parsed !== "object" || parsed === null) {
      return { action: null, signature: new Uint8Array(0) };
    }
    const record = parsed as { action?: unknown; signature?: unknown };
    const action = typeof record.action === "string" ? record.action : null;
    const signature = typeof record.signature === "string" ? base64ToBytes(record.signature) : new Uint8Array(0);
    return { action, signature };
  } catch {
    return { action: null, signature: new Uint8Array(0) };
  }
}

function openEventSource(
  eventsBaseUrl: string,
  sessionId: string,
  hostElementId: string,
  telemetryBaseUrl: string | null,
): EventSource {
  const base = eventsBaseUrl.replace(/\/+$/, "");
  const url = `${base}/${encodeURIComponent(sessionId)}`;
  const source = new EventSource(url, { withCredentials: true });

  // WITHDRAW and REFRESH both arrive as one `security/lifecycle` event, distinguished by an
  // `action` field -- the whitepaper's §8.3 envelope, rather than a distinct SSE event name per
  // action. An action this bridge does not recognize (including a missing/malformed one) is
  // treated the same as a forged signature: this is meant to be a trusted, fully-controlled
  // channel, so anything unexpected on it is tampering, not something to silently drop.
  source.addEventListener("security/lifecycle", (event) => {
    const entry = mounted.get(hostElementId);
    if (entry === undefined) {
      return;
    }
    const { action, signature } = parseLifecycleEventData((event as MessageEvent).data);

    if (action === "WITHDRAW") {
      const outcome = verifyAndEndWithWasm(
        securityWasm(),
        sessionId,
        controlMessage(sessionId, "WITHDRAW"),
        signature,
        controlMessage(sessionId, "WITHDRAW_EVENT"),
      );
      if (outcome === null) {
        return; // nothing left to verify/end against -- already torn down by something else
      }
      const onWithdraw = entry.onWithdraw;
      // A forged/invalid WITHDRAW is itself a tamper signal: an unauthenticated party injected
      // a lifecycle event into what should be a trusted channel. A genuinely valid WITHDRAW is
      // not tampering -- scrub without the tamper callback, then tell the host directly.
      destroySession(hostElementId, /* notifyTamper */ !outcome.valid);
      postTelemetryReceipt(
        telemetryBaseUrl,
        "destruction",
        destructionReceiptBody(sessionId, outcome.valid ? "WITHDRAW_EVENT" : "TAMPER_DETECTED", outcome.destructionSignature),
      );
      if (outcome.valid) {
        onWithdraw();
      }
      return;
    }

    if (action === "REFRESH") {
      const valid = verifySignalWithWasm(securityWasm(), sessionId, controlMessage(sessionId, "REFRESH"), signature);
      if (valid === null) {
        return; // nothing left to verify against
      }
      if (valid) {
        entry.onRefresh();
      } else {
        // Same defensive-teardown response as any other detected tamper -- REFRESH does not
        // normally end the session, but a forged one is not a normal REFRESH.
        destroySessionWithReceipt(hostElementId, sessionId, telemetryBaseUrl, "TAMPER_DETECTED", /* notifyTamper */ true);
      }
      return;
    }

    // Anything else on this channel -- an unrecognized action, or none at all (malformed JSON,
    // wrong shape) -- is treated as tampering rather than silently ignored.
    destroySessionWithReceipt(hostElementId, sessionId, telemetryBaseUrl, "TAMPER_DETECTED", /* notifyTamper */ true);
  });

  return source;
}

// Schedules TTL_EXPIRY self-destruction `ttlSeconds` after a successful mount. Returns null
// when ttlSeconds is null/disabled. A session already torn down by something else by the time
// the timer fires is a harmless no-op (destroySessionWithReceipt's wasm call simply finds no
// signing key left and returns null, same as a duplicate WITHDRAW) -- but destroySession also
// clears this timer on every other teardown path, so that path is not normally reached at all.
function scheduleTtlExpiry(
  hostElementId: string,
  sessionId: string,
  telemetryBaseUrl: string | null,
  ttlSeconds: number | null,
): ReturnType<typeof setTimeout> | null {
  if (ttlSeconds === null) {
    return null;
  }
  return setTimeout(() => {
    destroySessionWithReceipt(hostElementId, sessionId, telemetryBaseUrl, "TTL_EXPIRY", /* notifyTamper */ false);
  }, ttlSeconds * 1000);
}

/**
 * Decrypts the AES-GCM payload for `sessionId` and mounts it into an isolated
 * Shadow DOM node under `hostElementId`. Returns `false` (and mounts nothing)
 * if the wasm session handshake, decryption, or DOM mount fails for any
 * reason -- callers must never fall back to displaying the raw ciphertext.
 * `telemetryBaseUrl` is optional (pass `null` to disable): when set, an Access
 * Receipt is signed and posted right after a successful mount, and every
 * termination path signs and posts a Destruction Receipt. `ttlSeconds` is
 * optional (pass `null` to disable): when set, the session self-destructs
 * (trigger `TTL_EXPIRY`) that many seconds after a successful mount, with no
 * other trigger required.
 */
export async function decryptAndMount(
  hostElementId: string,
  sessionId: string,
  serverPublicKeyBase64: string,
  nonceBase64: string,
  ciphertextBase64: string,
  eventsBaseUrl: string,
  telemetryBaseUrl: string | null,
  ttlSeconds: number | null,
  onWithdraw: () => void,
  onRefresh: () => void,
  onTamper: () => void,
): Promise<boolean> {
  const host = document.getElementById(hostElementId);
  if (host === null) {
    return false;
  }

  await ensureSecurityWasm();
  const wasm = securityWasm();

  const serverPublicKey = base64ToBytes(serverPublicKeyBase64);
  const nonce = base64ToBytes(nonceBase64);
  const ciphertext = base64ToBytes(ciphertextBase64);
  if (serverPublicKey.length !== PUBLIC_KEY_LEN || nonce.length !== NONCE_LEN) {
    return false;
  }

  const sessionIdBytes = new TextEncoder().encode(sessionId);
  const sessionIdPointer = writeBytes(wasm, sessionIdBytes);

  let plaintextPointer = 0;
  let plaintextLength = 0;
  let mountSucceeded = false;

  try {
    // 32 bytes of browser-CSPRNG entropy seed the client's ephemeral P-256
    // key. wasm32-unknown-unknown has no OS RNG, so this is the only source
    // of randomness for the handshake, and it never leaves this function.
    const seed = new Uint8Array(SEED_LEN);
    crypto.getRandomValues(seed);
    const seedPointer = writeBytes(wasm, seed);
    const outPublicKeyPointer = wasm.alloc(PUBLIC_KEY_LEN);
    let beginStatus: number;
    try {
      beginStatus = wasm.begin_session(sessionIdPointer, sessionIdBytes.length, seedPointer, outPublicKeyPointer);
    } finally {
      seed.fill(0);
      wasm.dealloc(seedPointer, SEED_LEN);
      wasm.dealloc(outPublicKeyPointer, PUBLIC_KEY_LEN);
    }
    if (beginStatus !== 0) {
      return false;
    }

    const serverPublicKeyPointer = writeBytes(wasm, serverPublicKey);
    let completeStatus: number;
    try {
      completeStatus = wasm.complete_session(
        sessionIdPointer,
        sessionIdBytes.length,
        serverPublicKeyPointer,
        serverPublicKey.length,
      );
    } finally {
      wasm.dealloc(serverPublicKeyPointer, serverPublicKey.length);
    }
    if (completeStatus !== 0) {
      return false;
    }

    const noncePointer = writeBytes(wasm, nonce);
    const ciphertextPointer = writeBytes(wasm, ciphertext);
    const outLengthPointer = wasm.alloc(USIZE_BYTES);
    try {
      plaintextPointer = wasm.decrypt_payload(
        sessionIdPointer,
        sessionIdBytes.length,
        noncePointer,
        ciphertextPointer,
        ciphertext.length,
        outLengthPointer,
      );
      plaintextLength = plaintextPointer === 0 ? 0 : readU32(wasm, outLengthPointer);
    } finally {
      wasm.dealloc(noncePointer, nonce.length || 1);
      wasm.dealloc(ciphertextPointer, ciphertext.length || 1);
      wasm.dealloc(outLengthPointer, USIZE_BYTES);
    }
    if (plaintextPointer === 0 || plaintextLength === 0) {
      return false;
    }

    // Copy the bytes out via TextDecoder immediately; clear_payload (in the
    // outer finally below) scrubs the wasm-owned copy the instant this call
    // returns, whether mounting below succeeds or throws.
    const plaintextBytes = new Uint8Array(wasm.memory.buffer, plaintextPointer, plaintextLength).slice();
    const text = new TextDecoder().decode(plaintextBytes);
    plaintextBytes.fill(0); // scrub the JS-side copy the instant it is decoded

    const shadowRoot = host.attachShadow({ mode: "closed" });
    const bubble = document.createElement("p");
    bubble.className = "dx-ephemeral-chat-text";
    bubble.textContent = text; // textContent only -- never innerHTML
    shadowRoot.appendChild(bubble);

    const observer = startTamperObserver(hostElementId, shadowRoot, sessionId, telemetryBaseUrl);
    const eventSource = openEventSource(eventsBaseUrl, sessionId, hostElementId, telemetryBaseUrl);
    const ttlTimer = scheduleTtlExpiry(hostElementId, sessionId, telemetryBaseUrl, ttlSeconds);

    mounted.set(hostElementId, {
      sessionId,
      telemetryBaseUrl,
      shadowRoot,
      observer,
      eventSource,
      ttlTimer,
      onWithdraw,
      onRefresh,
      onTamper,
      destroyed: false,
    });
    mountSucceeded = true;

    // S5 Telemetry_Pulse: sign and (non-blocking, best-effort) post the Access Receipt. Uses
    // the session's signing key, which -- unlike the AES key consumed by decrypt_payload above
    // -- is still alive and stays alive until the session is actually torn down.
    const accessSignature = signWithWasm(wasm, sessionId, controlMessage(sessionId, "ACCESS_CONFIRMED"));
    if (accessSignature !== null) {
      postTelemetryReceipt(telemetryBaseUrl, "access", {
        sessionId,
        eventType: "ACCESS_CONFIRMED",
        timestamp: new Date().toISOString(),
        clientSignature: bytesToBase64(accessSignature),
      });
    }

    return true;
  } finally {
    if (plaintextPointer !== 0) {
      // Mandatory: scrubs the Rust-owned plaintext buffer in place before
      // freeing it, whether mounting above succeeded or threw.
      wasm.clear_payload(plaintextPointer, plaintextLength);
    }
    if (!mountSucceeded) {
      // Only clean up eagerly on failure -- a successful mount keeps its signing key alive
      // (the AES key was already consumed by decrypt_payload) so it can verify a later
      // WITHDRAW/REFRESH and sign telemetry receipts until the session is actually torn down
      // (see destroySession/destroySessionWithReceipt/verifyAndEndWithWasm).
      wasm.end_session(sessionIdPointer, sessionIdBytes.length);
    }
    wasm.dealloc(sessionIdPointer, sessionIdBytes.length || 1);
  }
}

/**
 * Starts a session's client-side ECDH handshake: generates a fresh P-256
 * keypair from browser CSPRNG entropy (never leaves this function) and
 * returns the resulting public key so the host can forward it to a broker
 * that will encrypt a payload for this exact client. The derived key is held
 * in the wasm module's session store under `sessionId` until a matching
 * {@link completeAndMount} call (or {@link decryptAndMount}, which runs both
 * halves itself) consumes it. Returns `null` on failure -- the seed is
 * rejected, or the module fails to load.
 */
export async function beginHandshake(sessionId: string): Promise<string | null> {
  await ensureSecurityWasm();
  const wasm = securityWasm();

  const sessionIdBytes = new TextEncoder().encode(sessionId);
  const sessionIdPointer = writeBytes(wasm, sessionIdBytes);
  const seed = new Uint8Array(SEED_LEN);
  crypto.getRandomValues(seed);
  const seedPointer = writeBytes(wasm, seed);
  const outPublicKeyPointer = wasm.alloc(PUBLIC_KEY_LEN);

  try {
    const status = wasm.begin_session(sessionIdPointer, sessionIdBytes.length, seedPointer, outPublicKeyPointer);
    if (status !== 0) {
      return null;
    }
    const publicKeyBytes = new Uint8Array(wasm.memory.buffer, outPublicKeyPointer, PUBLIC_KEY_LEN).slice();
    return bytesToBase64(publicKeyBytes);
  } finally {
    seed.fill(0);
    wasm.dealloc(seedPointer, SEED_LEN);
    wasm.dealloc(outPublicKeyPointer, PUBLIC_KEY_LEN);
    wasm.dealloc(sessionIdPointer, sessionIdBytes.length || 1);
  }
}

/**
 * The second half of the handshake: completes ECDH against the broker's
 * server public key, decrypts the AES-GCM payload, and mounts it exactly as
 * {@link decryptAndMount} does -- but without generating a new client
 * keypair, reusing the pending session a prior {@link beginHandshake} call
 * for the same `sessionId` already stored in the wasm module. Calling this
 * without a preceding, successful `beginHandshake` for the same session id
 * always fails (`complete_session` rejects a session with no pending state).
 * `telemetryBaseUrl` and `ttlSeconds` are both optional (pass `null` to disable either) -- see
 * {@link decryptAndMount}.
 */
export async function completeAndMount(
  hostElementId: string,
  sessionId: string,
  serverPublicKeyBase64: string,
  nonceBase64: string,
  ciphertextBase64: string,
  eventsBaseUrl: string,
  telemetryBaseUrl: string | null,
  ttlSeconds: number | null,
  onWithdraw: () => void,
  onRefresh: () => void,
  onTamper: () => void,
): Promise<boolean> {
  // TEMPORARY DIAGNOSTIC (2026-07-19): see the earlier diagnostic block below. This one
  // traces every early-return point in this function, since the first diagnostic (which
  // only logs after complete_session succeeds) produced zero output on production --
  // meaning the failure happens before that point, not at decrypt as originally assumed.
  // Remove alongside the rest once the root cause is found.
  console.log(`[DIAGNOSTIC] completeAndMount start: session=${sessionId}`);

  const host = document.getElementById(hostElementId);
  if (host === null) {
    console.log("[DIAGNOSTIC] completeAndMount: host element not found, returning false");
    return false;
  }

  await ensureSecurityWasm();
  const wasm = securityWasm();
  console.log(`[DIAGNOSTIC] completeAndMount: wasm loaded, has debug_aes_key_hash=${typeof wasm.debug_aes_key_hash}`);

  const serverPublicKey = base64ToBytes(serverPublicKeyBase64);
  const nonce = base64ToBytes(nonceBase64);
  const ciphertext = base64ToBytes(ciphertextBase64);
  console.log(
    `[DIAGNOSTIC] completeAndMount: serverPublicKey.length=${serverPublicKey.length} ` +
      `(expected ${PUBLIC_KEY_LEN}), nonce.length=${nonce.length} (expected ${NONCE_LEN}), ` +
      `ciphertext.length=${ciphertext.length}`,
  );
  if (serverPublicKey.length !== PUBLIC_KEY_LEN || nonce.length !== NONCE_LEN) {
    console.log("[DIAGNOSTIC] completeAndMount: length check failed, returning false");
    return false;
  }

  const sessionIdBytes = new TextEncoder().encode(sessionId);
  const sessionIdPointer = writeBytes(wasm, sessionIdBytes);

  let plaintextPointer = 0;
  let plaintextLength = 0;
  let mountSucceeded = false;

  try {
    const serverPublicKeyPointer = writeBytes(wasm, serverPublicKey);
    let completeStatus: number;
    try {
      completeStatus = wasm.complete_session(
        sessionIdPointer,
        sessionIdBytes.length,
        serverPublicKeyPointer,
        serverPublicKey.length,
      );
    } finally {
      wasm.dealloc(serverPublicKeyPointer, serverPublicKey.length);
    }
    console.log(`[DIAGNOSTIC] completeAndMount: complete_session status=${completeStatus}`);
    if (completeStatus !== 0) {
      console.log("[DIAGNOSTIC] completeAndMount: complete_session failed, returning false");
      return false;
    }

    // TEMPORARY DIAGNOSTIC (2026-07-19): tracking down a production-only decrypt failure
    // that doesn't reproduce locally. Logs SHA-256 of the client-derived AES key -- never
    // the key itself -- to compare against a matching server-side log in
    // DemoAiChatBroker.cs. Remove this block, the wasm export, and the server log once the
    // root cause is found.
    {
      const outHashPointer = wasm.alloc(32);
      try {
        const hashStatus = wasm.debug_aes_key_hash(sessionIdPointer, sessionIdBytes.length, outHashPointer);
        if (hashStatus === 0) {
          const hashBytes = new Uint8Array(wasm.memory.buffer, outHashPointer, 32).slice();
          const hex = Array.from(hashBytes).map((b) => b.toString(16).padStart(2, "0")).join("");
          console.log(`[DIAGNOSTIC] client aes_key sha256: ${hex} (session ${sessionId})`);
        } else {
          console.log(`[DIAGNOSTIC] client aes_key sha256: unavailable (status ${hashStatus})`);
        }
      } finally {
        wasm.dealloc(outHashPointer, 32);
      }
    }

    const noncePointer = writeBytes(wasm, nonce);
    const ciphertextPointer = writeBytes(wasm, ciphertext);
    const outLengthPointer = wasm.alloc(USIZE_BYTES);
    try {
      plaintextPointer = wasm.decrypt_payload(
        sessionIdPointer,
        sessionIdBytes.length,
        noncePointer,
        ciphertextPointer,
        ciphertext.length,
        outLengthPointer,
      );
      plaintextLength = plaintextPointer === 0 ? 0 : readU32(wasm, outLengthPointer);
    } finally {
      wasm.dealloc(noncePointer, nonce.length || 1);
      wasm.dealloc(ciphertextPointer, ciphertext.length || 1);
      wasm.dealloc(outLengthPointer, USIZE_BYTES);
    }
    // TEMPORARY DIAGNOSTIC (2026-07-19): see the block above. Remove alongside it.
    console.log(
      `[DIAGNOSTIC] completeAndMount: decrypt_payload plaintextPointer=${plaintextPointer}, ` +
        `plaintextLength=${plaintextLength}`,
    );
    if (plaintextPointer === 0 || plaintextLength === 0) {
      console.log("[DIAGNOSTIC] completeAndMount: decrypt_payload failed (bad key/nonce/tag), returning false");
      return false;
    }

    const plaintextBytes = new Uint8Array(wasm.memory.buffer, plaintextPointer, plaintextLength).slice();
    const text = new TextDecoder().decode(plaintextBytes);
    plaintextBytes.fill(0);

    const shadowRoot = host.attachShadow({ mode: "closed" });
    const bubble = document.createElement("p");
    bubble.className = "dx-ephemeral-chat-text";
    bubble.textContent = text;
    shadowRoot.appendChild(bubble);

    const observer = startTamperObserver(hostElementId, shadowRoot, sessionId, telemetryBaseUrl);
    const eventSource = openEventSource(eventsBaseUrl, sessionId, hostElementId, telemetryBaseUrl);
    const ttlTimer = scheduleTtlExpiry(hostElementId, sessionId, telemetryBaseUrl, ttlSeconds);

    mounted.set(hostElementId, {
      sessionId,
      telemetryBaseUrl,
      shadowRoot,
      observer,
      eventSource,
      ttlTimer,
      onWithdraw,
      onRefresh,
      onTamper,
      destroyed: false,
    });
    mountSucceeded = true;

    const accessSignature = signWithWasm(wasm, sessionId, controlMessage(sessionId, "ACCESS_CONFIRMED"));
    if (accessSignature !== null) {
      postTelemetryReceipt(telemetryBaseUrl, "access", {
        sessionId,
        eventType: "ACCESS_CONFIRMED",
        timestamp: new Date().toISOString(),
        clientSignature: bytesToBase64(accessSignature),
      });
    }

    return true;
  } finally {
    if (plaintextPointer !== 0) {
      wasm.clear_payload(plaintextPointer, plaintextLength);
    }
    if (!mountSucceeded) {
      wasm.end_session(sessionIdPointer, sessionIdBytes.length);
    }
    wasm.dealloc(sessionIdPointer, sessionIdBytes.length || 1);
  }
}

/**
 * Scrubs the mounted node's memory, closes its EventSource, stops observing it, and -- if the
 * mount had succeeded -- signs and (best-effort) posts a Destruction Receipt with trigger
 * `COMPONENT_UNMOUNT`. Safe to call for a host element id that was never mounted (or already
 * scrubbed) -- a no-op in that case.
 */
export function scrubNode(hostElementId: string): void {
  const entry = mounted.get(hostElementId);
  if (entry === undefined || entry.destroyed) {
    return;
  }
  destroySessionWithReceipt(hostElementId, entry.sessionId, entry.telemetryBaseUrl, "COMPONENT_UNMOUNT", /* notifyTamper */ false);
}
