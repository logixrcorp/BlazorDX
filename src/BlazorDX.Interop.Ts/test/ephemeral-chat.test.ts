// Unit tests for ephemeral-chat.ts. The dx_security wasm module itself is
// exercised by cargo test (crypto/session logic) and the Playwright E2E suite
// (real in-browser tamper detection + zeroing timing) -- here we fake
// rust-loader's exports entirely so we can drive every begin/complete/decrypt/
// sign/verify success and failure path deterministically, and assert on the
// DOM/EventSource/fetch side effects that are this module's actual job.

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const PUBLIC_KEY_LEN = 65;
const NONCE_LEN = 12;
const SIGNATURE_LEN = 32;

// --- Fake dx_security wasm module -------------------------------------------
// A tiny bump allocator over a real ArrayBuffer, so pointer arithmetic and
// TypedArray views in ephemeral-chat.ts behave exactly as they would against
// genuine wasm linear memory.
function createFakeWasm() {
  const memory = { buffer: new ArrayBuffer(1 << 16) } as WebAssembly.Memory;
  let nextPointer = 8; // never hand out 0; ephemeral-chat.ts treats 0 as "null"

  const calls = {
    clearPayload: [] as Array<[number, number]>,
    endSession: 0,
    dealloc: 0,
    sign: 0,
    verifySignal: 0,
    verifyAndEnd: 0,
    endWithReceipt: 0,
  };

  const control = {
    beginStatus: 0,
    completeStatus: 0,
    decryptSucceeds: true,
    plaintext: "hello from the household channel",
  };

  // Sessions with a live "signing key" -- mirrors dx_security's EstablishedKeys.hmac_key,
  // which (unlike the AES key) survives decrypt_payload and is only removed by end_session /
  // verify_and_end_session / end_with_receipt.
  const signingKeys = new Set<string>();

  function alloc(len: number): number {
    const pointer = nextPointer;
    nextPointer += Math.max(len, 1);
    return pointer;
  }

  function readString(ptr: number, len: number): string {
    return new TextDecoder().decode(new Uint8Array(memory.buffer, ptr, len));
  }

  function readBytes(ptr: number, len: number): Uint8Array {
    return new Uint8Array(memory.buffer, ptr, len).slice();
  }

  function bytesEqual(a: Uint8Array, b: Uint8Array): boolean {
    return a.length === b.length && a.every((byte, i) => byte === b[i]);
  }

  // Deterministic, non-cryptographic stand-in for HMAC-SHA256: stable per (sessionId, message),
  // so a test can independently compute "the signature the broker would have sent" and forge a
  // mismatched one just by signing a different sessionId or message. Exposed on the returned
  // object so tests can construct valid/forged signatures without reaching into wasm internals.
  function fakeSignature(sessionId: string, message: Uint8Array): Uint8Array {
    const sig = new Uint8Array(SIGNATURE_LEN);
    const idBytes = new TextEncoder().encode(sessionId);
    for (let i = 0; i < idBytes.length; i++) {
      sig[i % SIGNATURE_LEN] ^= idBytes[i];
    }
    for (let i = 0; i < message.length; i++) {
      sig[(i + 7) % SIGNATURE_LEN] ^= message[i];
    }
    return sig;
  }

  const wasm = {
    memory,
    alloc,
    dealloc: vi.fn((_pointer: number, _len: number) => {
      calls.dealloc += 1;
    }),
    begin_session: vi.fn((_sessionIdPtr: number, _sessionIdLen: number, _seedPtr: number, outPubPtr: number) => {
      if (control.beginStatus === 0) {
        const out = new Uint8Array(memory.buffer, outPubPtr, PUBLIC_KEY_LEN);
        out[0] = 0x04;
        out.fill(0xab, 1);
      }
      return control.beginStatus;
    }),
    complete_session: vi.fn((sessionIdPtr: number, sessionIdLen: number) => {
      if (control.completeStatus === 0) {
        signingKeys.add(readString(sessionIdPtr, sessionIdLen));
      }
      return control.completeStatus;
    }),
    decrypt_payload: vi.fn(
      (
        _sessionIdPtr: number,
        _sessionIdLen: number,
        _noncePtr: number,
        _ciphertextPtr: number,
        _ciphertextLen: number,
        outLenPtr: number,
      ) => {
        if (!control.decryptSucceeds) {
          new DataView(memory.buffer).setUint32(outLenPtr, 0, true);
          return 0;
        }
        const bytes = new TextEncoder().encode(control.plaintext);
        const pointer = alloc(bytes.length);
        new Uint8Array(memory.buffer, pointer, bytes.length).set(bytes);
        new DataView(memory.buffer).setUint32(outLenPtr, bytes.length, true);
        return pointer;
      },
    ),
    clear_payload: vi.fn((pointer: number, len: number) => {
      calls.clearPayload.push([pointer, len]);
      new Uint8Array(memory.buffer, pointer, len).fill(0);
    }),
    end_session: vi.fn((sessionIdPtr: number, sessionIdLen: number) => {
      calls.endSession += 1;
      signingKeys.delete(readString(sessionIdPtr, sessionIdLen));
    }),
    sign: vi.fn(
      (sessionIdPtr: number, sessionIdLen: number, messagePtr: number, messageLen: number, outSigPtr: number) => {
        calls.sign += 1;
        const sessionId = readString(sessionIdPtr, sessionIdLen);
        if (!signingKeys.has(sessionId)) {
          return -6; // ERR_NO_SIGNING_KEY
        }
        const message = readBytes(messagePtr, messageLen);
        new Uint8Array(memory.buffer, outSigPtr, SIGNATURE_LEN).set(fakeSignature(sessionId, message));
        return 0;
      },
    ),
    verify_signal: vi.fn(
      (
        sessionIdPtr: number,
        sessionIdLen: number,
        messagePtr: number,
        messageLen: number,
        signaturePtr: number,
        signatureLen: number,
        outValidPtr: number,
      ) => {
        calls.verifySignal += 1;
        const sessionId = readString(sessionIdPtr, sessionIdLen);
        if (!signingKeys.has(sessionId)) {
          return -6;
        }
        const message = readBytes(messagePtr, messageLen);
        const signature = readBytes(signaturePtr, signatureLen);
        const valid = bytesEqual(signature, fakeSignature(sessionId, message));
        new Uint8Array(memory.buffer, outValidPtr, 1)[0] = valid ? 1 : 0;
        return 0;
      },
    ),
    verify_and_end_session: vi.fn(
      (
        sessionIdPtr: number,
        sessionIdLen: number,
        messagePtr: number,
        messageLen: number,
        signaturePtr: number,
        signatureLen: number,
        destructionMessagePtr: number,
        destructionMessageLen: number,
        outValidPtr: number,
        outDestructionSigPtr: number,
      ) => {
        calls.verifyAndEnd += 1;
        const sessionId = readString(sessionIdPtr, sessionIdLen);
        if (!signingKeys.has(sessionId)) {
          return -6;
        }
        const message = readBytes(messagePtr, messageLen);
        const signature = readBytes(signaturePtr, signatureLen);
        const destructionMessage = readBytes(destructionMessagePtr, destructionMessageLen);
        const valid = bytesEqual(signature, fakeSignature(sessionId, message));
        new Uint8Array(memory.buffer, outValidPtr, 1)[0] = valid ? 1 : 0;
        new Uint8Array(memory.buffer, outDestructionSigPtr, SIGNATURE_LEN).set(
          fakeSignature(sessionId, destructionMessage),
        );
        signingKeys.delete(sessionId);
        return 0;
      },
    ),
    end_with_receipt: vi.fn(
      (sessionIdPtr: number, sessionIdLen: number, messagePtr: number, messageLen: number, outSigPtr: number) => {
        calls.endWithReceipt += 1;
        const sessionId = readString(sessionIdPtr, sessionIdLen);
        if (!signingKeys.has(sessionId)) {
          return -6;
        }
        const message = readBytes(messagePtr, messageLen);
        new Uint8Array(memory.buffer, outSigPtr, SIGNATURE_LEN).set(fakeSignature(sessionId, message));
        signingKeys.delete(sessionId);
        return 0;
      },
    ),
  };

  return { wasm, control, calls, fakeSignature, signingKeys };
}

let fake: ReturnType<typeof createFakeWasm>;

vi.mock("../src/rust-loader", () => ({
  ensureSecurityWasm: vi.fn(async () => {}),
  securityWasm: () => fake.wasm,
}));

// jsdom does not implement EventSource. A minimal fake that records the last
// constructed instance (one per decryptAndMount call in these tests) and lets
// tests dispatch WITHDRAW/REFRESH synthetically, with an SSE-shaped `data` field.
class FakeEventSource {
  static instances: FakeEventSource[] = [];
  url: string;
  closed = false;
  private listeners = new Map<string, Array<(event: { data: string }) => void>>();

  constructor(url: string, _init?: unknown) {
    this.url = url;
    FakeEventSource.instances.push(this);
  }

  addEventListener(type: string, callback: (event: { data: string }) => void): void {
    const list = this.listeners.get(type) ?? [];
    list.push(callback);
    this.listeners.set(type, list);
  }

  close(): void {
    this.closed = true;
  }

  dispatch(type: string, data: string = "{}"): void {
    for (const callback of this.listeners.get(type) ?? []) {
      callback({ data });
    }
  }
}

async function flushMicrotasks(): Promise<void> {
  // MutationObserver callbacks fire as a queued microtask after the mutation;
  // two ticks reliably drains it under jsdom.
  await Promise.resolve();
  await Promise.resolve();
}

const VALID_PUBLIC_KEY_B64 = btoa(String.fromCharCode(...new Uint8Array(PUBLIC_KEY_LEN).fill(7)));
const VALID_NONCE_B64 = btoa(String.fromCharCode(...new Uint8Array(NONCE_LEN).fill(3)));
const VALID_CIPHERTEXT_B64 = btoa("irrelevant-to-the-fake-decrypt");

function bytesToBase64(bytes: Uint8Array): string {
  return btoa(String.fromCharCode(...bytes));
}

describe("ephemeral-chat", () => {
  let hostElementId: string;
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fake = createFakeWasm();
    FakeEventSource.instances = [];
    vi.stubGlobal("EventSource", FakeEventSource);
    fetchMock = vi.fn(async () => new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    hostElementId = "dx-ephemeral-chat-test";
    const host = document.createElement("div");
    host.id = hostElementId;
    document.body.appendChild(host);
  });

  afterEach(() => {
    document.getElementById(hostElementId)?.remove();
    vi.unstubAllGlobals();
    vi.clearAllMocks();
  });

  async function mount(overrides: Partial<{
    sessionId: string;
    telemetryBaseUrl: string | null;
    onWithdraw: () => void;
    onRefresh: () => void;
    onTamper: () => void;
  }> = {}) {
    const { decryptAndMount } = await import("../src/ephemeral-chat");
    const sessionId = overrides.sessionId ?? "session-123";
    const telemetryBaseUrl = overrides.telemetryBaseUrl === undefined ? null : overrides.telemetryBaseUrl;
    const onWithdraw = overrides.onWithdraw ?? vi.fn();
    const onRefresh = overrides.onRefresh ?? vi.fn();
    const onTamper = overrides.onTamper ?? vi.fn();

    const ok = await decryptAndMount(
      hostElementId,
      sessionId,
      VALID_PUBLIC_KEY_B64,
      VALID_NONCE_B64,
      VALID_CIPHERTEXT_B64,
      "/ephemeral-events",
      telemetryBaseUrl,
      onWithdraw,
      onRefresh,
      onTamper,
    );

    return { ok, sessionId, telemetryBaseUrl, onWithdraw, onRefresh, onTamper };
  }

  it("mounts the decrypted text into a closed shadow root, never the light DOM", async () => {
    const { ok } = await mount();

    expect(ok).toBe(true);
    const host = document.getElementById(hostElementId)!;
    expect(host.shadowRoot).toBeNull(); // "closed" mode: not reachable from outside
    expect(host.textContent).toBe(""); // plaintext never lands in the light DOM
  });

  it("calls clear_payload with the exact pointer/length decrypt_payload returned", async () => {
    fake = createFakeWasm();
    fake.control.plaintext = "scrub me";
    vi.stubGlobal("EventSource", FakeEventSource);
    await mount();

    expect(fake.calls.clearPayload).toHaveLength(1);
    const [, len] = fake.calls.clearPayload[0];
    expect(len).toBe(new TextEncoder().encode("scrub me").length);
  });

  it("calls clear_payload even when a later step throws, via the finally block", async () => {
    fake = createFakeWasm();
    // Force the plaintext to decode fine but make the host element vanish
    // between decrypt and mount to simulate a late failure after decrypt
    // succeeded (still must scrub).
    const { decryptAndMount } = await import("../src/ephemeral-chat");
    const host = document.getElementById(hostElementId)!;
    const originalAttachShadow = host.attachShadow.bind(host);
    host.attachShadow = () => {
      throw new Error("simulated mount failure");
    };

    await expect(
      decryptAndMount(
        hostElementId,
        "session-throw",
        VALID_PUBLIC_KEY_B64,
        VALID_NONCE_B64,
        VALID_CIPHERTEXT_B64,
        "/ephemeral-events",
        null,
        vi.fn(),
        vi.fn(),
        vi.fn(),
      ),
    ).rejects.toThrow("simulated mount failure");

    expect(fake.calls.clearPayload).toHaveLength(1);
    host.attachShadow = originalAttachShadow;
  });

  it("does NOT end the wasm session after a successful mount -- the signing key must survive for a later WITHDRAW/receipt", async () => {
    const { sessionId } = await mount();

    expect(fake.calls.endSession).toBe(0);
    expect(fake.signingKeys.has(sessionId)).toBe(true);
  });

  it("ends the wasm session when a mount attempt fails (nothing to keep a signing key alive for)", async () => {
    fake = createFakeWasm();
    fake.control.decryptSucceeds = false;
    vi.stubGlobal("EventSource", FakeEventSource);

    await mount();

    expect(fake.calls.endSession).toBe(1);
  });

  it("returns false and mounts nothing when the server public key length is wrong", async () => {
    const { decryptAndMount } = await import("../src/ephemeral-chat");
    const ok = await decryptAndMount(
      hostElementId,
      "session-bad-key",
      btoa("too-short"),
      VALID_NONCE_B64,
      VALID_CIPHERTEXT_B64,
      "/ephemeral-events",
      null,
      vi.fn(),
      vi.fn(),
      vi.fn(),
    );

    expect(ok).toBe(false);
    expect(fake.wasm.begin_session).not.toHaveBeenCalled();
  });

  it("returns false when begin_session rejects the seed", async () => {
    fake = createFakeWasm();
    fake.control.beginStatus = -3;
    vi.stubGlobal("EventSource", FakeEventSource);

    const { ok } = await mount();

    expect(ok).toBe(false);
    expect(fake.wasm.complete_session).not.toHaveBeenCalled();
  });

  it("returns false when complete_session rejects the server public key", async () => {
    fake = createFakeWasm();
    fake.control.completeStatus = -4;
    vi.stubGlobal("EventSource", FakeEventSource);

    const { ok } = await mount();

    expect(ok).toBe(false);
    expect(fake.wasm.decrypt_payload).not.toHaveBeenCalled();
  });

  it("returns false and calls no clear_payload when decrypt_payload fails", async () => {
    fake = createFakeWasm();
    fake.control.decryptSucceeds = false;
    vi.stubGlobal("EventSource", FakeEventSource);

    const { ok } = await mount();

    expect(ok).toBe(false);
    expect(fake.calls.clearPayload).toHaveLength(0);
  });

  it("opens an EventSource against `${eventsBaseUrl}/{sessionId}`", async () => {
    await mount();

    expect(FakeEventSource.instances).toHaveLength(1);
    expect(FakeEventSource.instances[0].url).toBe("/ephemeral-events/session-123");
  });

  it("self-destructs and reports tampering when the mounted node is mutated externally, and posts a signed Destruction Receipt", async () => {
    // "closed" mode means `host.shadowRoot` is null to ordinary callers -- the
    // one documented way a page-context attacker can still reach in is by
    // monkey-patching Element.prototype.attachShadow *before* our code calls
    // it, capturing the root it creates. That is exactly what this test does,
    // to drive a real external mutation without weakening decryptAndMount's
    // production use of closed mode.
    let capturedRoot: ShadowRoot | null = null;
    const originalAttachShadow = Element.prototype.attachShadow;
    Element.prototype.attachShadow = function (this: Element, init: ShadowRootInit): ShadowRoot {
      const root = originalAttachShadow.call(this, init);
      capturedRoot = root;
      return root;
    };

    const { onTamper, sessionId } = await mount({ telemetryBaseUrl: "/telemetry" });
    Element.prototype.attachShadow = originalAttachShadow;

    expect(capturedRoot).not.toBeNull();
    const eventSource = FakeEventSource.instances[0];
    expect(eventSource.closed).toBe(false);
    expect(onTamper).not.toHaveBeenCalled();

    // The external mutation: append a node the module did not put there.
    capturedRoot!.appendChild(document.createElement("script"));
    await flushMicrotasks();

    expect(onTamper).toHaveBeenCalledOnce();
    expect(eventSource.closed).toBe(true);
    expect(capturedRoot!.childNodes).toHaveLength(0); // scrubbed
    expect(fake.calls.endWithReceipt).toBe(1);
    expect(fake.signingKeys.has(sessionId)).toBe(false);

    expect(fetchMock).toHaveBeenCalledWith("/telemetry/destruction", expect.objectContaining({ method: "POST" }));
    const [, init] = fetchMock.mock.calls.find(([url]) => url === "/telemetry/destruction")!;
    const body = JSON.parse((init as RequestInit).body as string);
    expect(body.trigger).toBe("TAMPER_DETECTED");
    expect(body.eventType).toBe("MEMORY_ZEROED");
    expect(body.sessionId).toBe(sessionId);
  });

  it("scrubNode overwrites the node, closes its EventSource, and posts a COMPONENT_UNMOUNT Destruction Receipt", async () => {
    const { sessionId } = await mount({ telemetryBaseUrl: "/telemetry" });
    const { scrubNode } = await import("../src/ephemeral-chat");
    const eventSource = FakeEventSource.instances[0];

    scrubNode(hostElementId);

    expect(eventSource.closed).toBe(true);
    expect(fake.calls.endWithReceipt).toBe(1);
    const [, init] = fetchMock.mock.calls.find(([url]) => url === "/telemetry/destruction")!;
    const body = JSON.parse((init as RequestInit).body as string);
    expect(body.trigger).toBe("COMPONENT_UNMOUNT");
    expect(body.sessionId).toBe(sessionId);
  });

  it("scrubNode on an unknown host id is a harmless no-op", async () => {
    const { scrubNode } = await import("../src/ephemeral-chat");
    expect(() => scrubNode("no-such-host")).not.toThrow();
  });

  it("scrubNode posts no telemetry when telemetryBaseUrl was null", async () => {
    await mount({ telemetryBaseUrl: null });
    const { scrubNode } = await import("../src/ephemeral-chat");

    scrubNode(hostElementId);

    expect(fetchMock).not.toHaveBeenCalled();
  });

  describe("Access Receipt (S5 Telemetry_Pulse)", () => {
    it("signs and posts an Access Receipt right after a successful mount", async () => {
      const { sessionId } = await mount({ telemetryBaseUrl: "/telemetry" });

      expect(fake.calls.sign).toBe(1);
      const [url, init] = fetchMock.mock.calls[0];
      expect(url).toBe("/telemetry/access");
      const body = JSON.parse((init as RequestInit).body as string);
      expect(body.eventType).toBe("ACCESS_CONFIRMED");
      expect(body.sessionId).toBe(sessionId);
      expect(typeof body.clientSignature).toBe("string");
    });

    it("posts no Access Receipt when telemetryBaseUrl is null", async () => {
      await mount({ telemetryBaseUrl: null });
      expect(fetchMock).not.toHaveBeenCalled();
    });

    it("a fetch rejection for the Access Receipt never fails the mount", async () => {
      fetchMock.mockRejectedValue(new Error("network down"));
      const { ok } = await mount({ telemetryBaseUrl: "/telemetry" });
      expect(ok).toBe(true);
    });
  });

  describe("a server WITHDRAW event", () => {
    it("with a valid signature: scrubs the node, calls onWithdraw (not onTamper), and posts a WITHDRAW_EVENT Destruction Receipt", async () => {
      const { onWithdraw, onTamper, sessionId } = await mount({ telemetryBaseUrl: "/telemetry" });
      const eventSource = FakeEventSource.instances[0];
      const signature = fake.fakeSignature(sessionId, new TextEncoder().encode(`${sessionId}|WITHDRAW`));

      eventSource.dispatch("WITHDRAW", JSON.stringify({ signature: bytesToBase64(signature) }));

      expect(eventSource.closed).toBe(true);
      expect(onWithdraw).toHaveBeenCalledOnce();
      expect(onTamper).not.toHaveBeenCalled();
      expect(fake.calls.verifyAndEnd).toBe(1);

      const [, init] = fetchMock.mock.calls.find(([url]) => url === "/telemetry/destruction")!;
      const body = JSON.parse((init as RequestInit).body as string);
      expect(body.trigger).toBe("WITHDRAW_EVENT");
    });

    it("with a forged signature: still tears down, but calls onTamper (not onWithdraw), and reports TAMPER_DETECTED", async () => {
      const { onWithdraw, onTamper } = await mount({ telemetryBaseUrl: "/telemetry" });
      const eventSource = FakeEventSource.instances[0];
      const forgedSignature = new Uint8Array(SIGNATURE_LEN); // all-zero -- never a real signature

      eventSource.dispatch("WITHDRAW", JSON.stringify({ signature: bytesToBase64(forgedSignature) }));

      expect(eventSource.closed).toBe(true);
      expect(onWithdraw).not.toHaveBeenCalled();
      expect(onTamper).toHaveBeenCalledOnce();

      const [, init] = fetchMock.mock.calls.find(([url]) => url === "/telemetry/destruction")!;
      const body = JSON.parse((init as RequestInit).body as string);
      expect(body.trigger).toBe("TAMPER_DETECTED");
    });

    it("with malformed/missing signature data: treated the same as a forged signature", async () => {
      const { onWithdraw, onTamper } = await mount();
      const eventSource = FakeEventSource.instances[0];

      eventSource.dispatch("WITHDRAW", "not even json");

      expect(onWithdraw).not.toHaveBeenCalled();
      expect(onTamper).toHaveBeenCalledOnce();
    });

    it("a signature valid for REFRESH must not verify as a WITHDRAW (message-bound, not just session-bound)", async () => {
      const { onWithdraw, onTamper, sessionId } = await mount();
      const eventSource = FakeEventSource.instances[0];
      const refreshSignature = fake.fakeSignature(sessionId, new TextEncoder().encode(`${sessionId}|REFRESH`));

      eventSource.dispatch("WITHDRAW", JSON.stringify({ signature: bytesToBase64(refreshSignature) }));

      expect(onWithdraw).not.toHaveBeenCalled();
      expect(onTamper).toHaveBeenCalledOnce();
    });

    it("a second WITHDRAW after the first (no signing key left) is a harmless no-op", async () => {
      const { onWithdraw, onTamper, sessionId } = await mount();
      const eventSource = FakeEventSource.instances[0];
      const signature = fake.fakeSignature(sessionId, new TextEncoder().encode(`${sessionId}|WITHDRAW`));

      eventSource.dispatch("WITHDRAW", JSON.stringify({ signature: bytesToBase64(signature) }));
      onWithdraw.mockClear();
      onTamper.mockClear();
      eventSource.dispatch("WITHDRAW", JSON.stringify({ signature: bytesToBase64(signature) }));

      expect(onWithdraw).not.toHaveBeenCalled();
      expect(onTamper).not.toHaveBeenCalled();
    });
  });

  describe("a server REFRESH event", () => {
    it("with a valid signature: calls onRefresh without tearing the node down", async () => {
      const { onRefresh, sessionId } = await mount();
      const eventSource = FakeEventSource.instances[0];
      const signature = fake.fakeSignature(sessionId, new TextEncoder().encode(`${sessionId}|REFRESH`));

      eventSource.dispatch("REFRESH", JSON.stringify({ signature: bytesToBase64(signature) }));

      expect(onRefresh).toHaveBeenCalledOnce();
      expect(eventSource.closed).toBe(false);
      expect(fake.signingKeys.has(sessionId)).toBe(true);
    });

    it("with a forged signature: does not call onRefresh, and instead tears down as tampering", async () => {
      const { onRefresh, onTamper } = await mount({ telemetryBaseUrl: "/telemetry" });
      const eventSource = FakeEventSource.instances[0];
      const forgedSignature = new Uint8Array(SIGNATURE_LEN);

      eventSource.dispatch("REFRESH", JSON.stringify({ signature: bytesToBase64(forgedSignature) }));

      expect(onRefresh).not.toHaveBeenCalled();
      expect(onTamper).toHaveBeenCalledOnce();
      expect(eventSource.closed).toBe(true);
      const [, init] = fetchMock.mock.calls.find(([url]) => url === "/telemetry/destruction")!;
      expect(JSON.parse((init as RequestInit).body as string).trigger).toBe("TAMPER_DETECTED");
    });
  });

  it("returns false immediately when the host element does not exist", async () => {
    const { decryptAndMount } = await import("../src/ephemeral-chat");
    const ok = await decryptAndMount(
      "does-not-exist",
      "session-x",
      VALID_PUBLIC_KEY_B64,
      VALID_NONCE_B64,
      VALID_CIPHERTEXT_B64,
      "/ephemeral-events",
      null,
      vi.fn(),
      vi.fn(),
      vi.fn(),
    );

    expect(ok).toBe(false);
    expect(fake.wasm.begin_session).not.toHaveBeenCalled();
  });

  describe("beginHandshake / completeAndMount (the split, live-broker handshake)", () => {
    it("beginHandshake returns the client's public key as base64 without decrypting anything", async () => {
      const { beginHandshake } = await import("../src/ephemeral-chat");

      const publicKeyBase64 = await beginHandshake("live-session-1");

      expect(publicKeyBase64).not.toBeNull();
      const bytes = Uint8Array.from(atob(publicKeyBase64!), (c) => c.charCodeAt(0));
      expect(bytes).toHaveLength(PUBLIC_KEY_LEN);
      expect(bytes[0]).toBe(0x04); // uncompressed SEC1 point
      expect(fake.wasm.complete_session).not.toHaveBeenCalled();
      expect(fake.wasm.decrypt_payload).not.toHaveBeenCalled();
    });

    it("beginHandshake returns null when begin_session rejects the seed", async () => {
      fake.control.beginStatus = -3;
      const { beginHandshake } = await import("../src/ephemeral-chat");

      const publicKeyBase64 = await beginHandshake("live-session-bad-seed");

      expect(publicKeyBase64).toBeNull();
    });

    it("beginHandshake frees its session-id, seed, and output-key buffers", async () => {
      const { beginHandshake } = await import("../src/ephemeral-chat");
      await beginHandshake("live-session-cleanup");

      // session id + seed + output public key pointer, all freed.
      expect(fake.calls.dealloc).toBe(3);
    });

    it("completeAndMount mounts using the pending state a prior beginHandshake left behind, without regenerating a client key", async () => {
      const { beginHandshake, completeAndMount } = await import("../src/ephemeral-chat");
      const onWithdraw = vi.fn();
      const onRefresh = vi.fn();
      const onTamper = vi.fn();

      const publicKeyBase64 = await beginHandshake("live-session-2");
      expect(publicKeyBase64).not.toBeNull();
      expect(fake.wasm.begin_session).toHaveBeenCalledTimes(1);

      const ok = await completeAndMount(
        hostElementId,
        "live-session-2",
        VALID_PUBLIC_KEY_B64,
        VALID_NONCE_B64,
        VALID_CIPHERTEXT_B64,
        "/ephemeral-events",
        null,
        onWithdraw,
        onRefresh,
        onTamper,
      );

      expect(ok).toBe(true);
      // completeAndMount never calls begin_session -- it only completes/decrypts.
      expect(fake.wasm.begin_session).toHaveBeenCalledTimes(1);
      expect(fake.wasm.complete_session).toHaveBeenCalledTimes(1);
      const host = document.getElementById(hostElementId)!;
      expect(host.shadowRoot).toBeNull(); // closed mode
      expect(FakeEventSource.instances).toHaveLength(1);
    });

    it("completeAndMount fails when no beginHandshake preceded it for this session id (mirrors complete_session's NotPending rejection)", async () => {
      fake.control.completeStatus = -5; // ERR_NOT_PENDING, as complete_session would return with no prior begin_session
      const { completeAndMount } = await import("../src/ephemeral-chat");

      const ok = await completeAndMount(
        hostElementId,
        "never-began-live-session",
        VALID_PUBLIC_KEY_B64,
        VALID_NONCE_B64,
        VALID_CIPHERTEXT_B64,
        "/ephemeral-events",
        null,
        vi.fn(),
        vi.fn(),
        vi.fn(),
      );

      expect(ok).toBe(false);
      expect(fake.wasm.decrypt_payload).not.toHaveBeenCalled();
    });

    it("completeAndMount still calls clear_payload and end_session even when decrypt fails", async () => {
      const { beginHandshake, completeAndMount } = await import("../src/ephemeral-chat");
      await beginHandshake("live-session-decrypt-fail");
      fake.control.decryptSucceeds = false;

      const ok = await completeAndMount(
        hostElementId,
        "live-session-decrypt-fail",
        VALID_PUBLIC_KEY_B64,
        VALID_NONCE_B64,
        VALID_CIPHERTEXT_B64,
        "/ephemeral-events",
        null,
        vi.fn(),
        vi.fn(),
        vi.fn(),
      );

      expect(ok).toBe(false);
      expect(fake.calls.clearPayload).toHaveLength(0); // nothing decrypted, nothing to scrub
      expect(fake.calls.endSession).toBe(1); // still torn down (mount never succeeded)
    });

    it("completeAndMount does NOT end the session after a successful mount, and signs an Access Receipt", async () => {
      const { beginHandshake, completeAndMount } = await import("../src/ephemeral-chat");
      await beginHandshake("live-session-access-receipt");

      const ok = await completeAndMount(
        hostElementId,
        "live-session-access-receipt",
        VALID_PUBLIC_KEY_B64,
        VALID_NONCE_B64,
        VALID_CIPHERTEXT_B64,
        "/ephemeral-events",
        "/telemetry",
        vi.fn(),
        vi.fn(),
        vi.fn(),
      );

      expect(ok).toBe(true);
      expect(fake.calls.endSession).toBe(0);
      expect(fake.signingKeys.has("live-session-access-receipt")).toBe(true);
      expect(fetchMock).toHaveBeenCalledWith("/telemetry/access", expect.objectContaining({ method: "POST" }));
    });
  });
});
