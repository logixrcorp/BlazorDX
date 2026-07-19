//! C-ABI surface for the `dx_security` wasm module -- the Wasm decryption core
//! of the "Zero-Trust, Ephemeral AI Chat Conduit."
//!
//! The TypeScript bridge (`ephemeral-chat.ts`) drives this module directly: it
//! writes session ids, seeds, public keys, nonces, and ciphertext into wasm
//! linear memory with [`alloc`], calls the exported functions below, and reads
//! results back out. No plaintext, key, or seed ever crosses back into C#/the
//! Blazor virtual DOM -- decryption happens here and the plaintext is injected
//! straight into an isolated Shadow DOM node by the TS bridge.
//!
//! Session state lives in a `thread_local!` [`session::SessionStore`]. Wasm32
//! running under the browser's single JS thread never has real concurrency, so
//! this is equivalent to a global but avoids `unsafe impl Sync` on interior
//! mutability we don't need to share across threads.
//!
//! # The `decrypt_payload` / `clear_payload` pair
//!
//! `zeroize::Zeroize::zeroize()` takes `&mut self`, so the decrypted plaintext
//! buffer must still be owned by Rust when it is zeroized -- a `Drop` impl that
//! fires only when the *host* releases its reference is not something Rust can
//! observe across the FFI boundary. So [`decrypt_payload`] returns a pointer and
//! length *without* releasing ownership (the backing `Vec<u8>` is leaked with
//! `mem::forget`, exactly like [`alloc`]), and [`clear_payload`] is a second,
//! mandatory export: it reconstructs the `Vec<u8>`, zeroizes it in place, and
//! only then lets it drop (deallocating). The TS bridge calls `clear_payload` in
//! a `finally` block so the buffer is scrubbed even if mounting throws.

mod session;

use core::cell::RefCell;
use core::mem;
use session::{SessionError, SessionStore};
use zeroize::Zeroize;

thread_local! {
    static STORE: RefCell<SessionStore> = RefCell::new(SessionStore::new());
}

/// Return codes shared by every fallible export. `OK` is always `0`; every
/// error is a distinct negative value so the TS bridge can log which stage
/// failed without ever seeing key material.
pub mod status {
    pub const OK: i32 = 0;
    pub const ERR_INVALID_UTF8: i32 = -1;
    pub const ERR_NULL_POINTER: i32 = -2;
    pub const ERR_INVALID_SEED: i32 = -3;
    pub const ERR_INVALID_PUBLIC_KEY: i32 = -4;
    pub const ERR_NOT_PENDING: i32 = -5;
    pub const ERR_NO_SIGNING_KEY: i32 = -6;
}

fn map_begin_error(e: SessionError) -> i32 {
    match e {
        SessionError::InvalidSeed => status::ERR_INVALID_SEED,
        _ => status::ERR_INVALID_SEED,
    }
}

fn map_complete_error(e: SessionError) -> i32 {
    match e {
        SessionError::InvalidSeed => status::ERR_INVALID_SEED,
        SessionError::InvalidPublicKey => status::ERR_INVALID_PUBLIC_KEY,
        SessionError::NotPending => status::ERR_NOT_PENDING,
        SessionError::UnknownSession
        | SessionError::InvalidNonceLength
        | SessionError::DecryptFailed
        | SessionError::AlreadyDecrypted
        | SessionError::NoSigningKey => status::ERR_NOT_PENDING,
    }
}

/// Maps any [`SessionError`] from [`SessionStore::sign`]/[`verify_and_end`]/[`end_with_receipt`]
/// to a status code. All three only ever fail with [`SessionError::NoSigningKey`] in practice
/// (no session, or still `Pending`) -- the wildcard arm exists only so this stays exhaustive
/// if `SessionError` grows a variant these functions don't actually produce.
///
/// [`SessionStore::sign`]: session::SessionStore::sign
/// [`verify_and_end`]: session::SessionStore::verify_and_end
/// [`end_with_receipt`]: session::SessionStore::end_with_receipt
fn map_signing_error(e: SessionError) -> i32 {
    match e {
        SessionError::NoSigningKey => status::ERR_NO_SIGNING_KEY,
        _ => status::ERR_NO_SIGNING_KEY,
    }
}

/// Reads `len` bytes at `ptr` as a UTF-8 `String`. Returns `None` on a null
/// pointer or invalid UTF-8; never panics on attacker-controlled input.
///
/// # Safety
/// `ptr` must be valid for `len` bytes (or `len` must be `0`).
unsafe fn read_string(ptr: *const u8, len: usize) -> Option<String> {
    if ptr.is_null() && len != 0 {
        return None;
    }
    if len == 0 {
        return Some(String::new());
    }
    let bytes = core::slice::from_raw_parts(ptr, len);
    core::str::from_utf8(bytes).ok().map(str::to_owned)
}

/// Allocates `len` bytes inside the module's linear memory and returns a
/// pointer the host can write into. The host must later return it via
/// [`dealloc`] (for plaintext specifically, via [`clear_payload`] instead, so
/// it is scrubbed first).
#[no_mangle]
pub extern "C" fn alloc(len: usize) -> *mut u8 {
    let mut buffer = Vec::<u8>::with_capacity(len);
    let pointer = buffer.as_mut_ptr();
    mem::forget(buffer);
    pointer
}

/// Frees a buffer previously returned by [`alloc`] (but NOT one returned by
/// [`decrypt_payload`] -- use [`clear_payload`] for that one, so it is
/// zeroized first).
///
/// # Safety
/// `pointer`/`len` must come from a prior [`alloc`] call and be freed once.
#[no_mangle]
pub unsafe extern "C" fn dealloc(pointer: *mut u8, len: usize) {
    if pointer.is_null() {
        return;
    }
    drop(Vec::from_raw_parts(pointer, 0, len));
}

/// Starts a session: derives a client-side ephemeral P-256 keypair from the
/// 32 bytes of host-supplied entropy at `seed_ptr` and writes the resulting
/// public key (uncompressed SEC1, [`session::PUBLIC_KEY_LEN`] bytes) into the
/// caller-allocated buffer at `out_pub_ptr`. Returns [`status::OK`] or a
/// negative `status::ERR_*` code.
///
/// # Safety
/// `session_id_ptr`/`session_id_len` must describe a valid UTF-8 byte range.
/// `seed_ptr` must be valid for exactly 32 bytes. `out_pub_ptr` must be valid
/// for exactly [`session::PUBLIC_KEY_LEN`] bytes (allocate it with [`alloc`]).
#[no_mangle]
pub unsafe extern "C" fn begin_session(
    session_id_ptr: *const u8,
    session_id_len: usize,
    seed_ptr: *const u8,
    out_pub_ptr: *mut u8,
) -> i32 {
    let Some(session_id) = read_string(session_id_ptr, session_id_len) else {
        return status::ERR_INVALID_UTF8;
    };
    if seed_ptr.is_null() || out_pub_ptr.is_null() {
        return status::ERR_NULL_POINTER;
    }

    let seed_slice = core::slice::from_raw_parts(seed_ptr, 32);
    let mut seed = [0u8; 32];
    seed.copy_from_slice(seed_slice);

    let result = STORE.with(|store| store.borrow_mut().begin_session(&session_id, &seed));
    seed.zeroize();

    match result {
        Ok(public_key) => {
            core::slice::from_raw_parts_mut(out_pub_ptr, session::PUBLIC_KEY_LEN).copy_from_slice(&public_key);
            status::OK
        }
        Err(e) => map_begin_error(e),
    }
}

/// Completes a pending session: runs ECDH against the server's public key at
/// `server_pub_ptr` and stores the derived AES-256-GCM key for `session_id`.
/// Returns [`status::OK`] or a negative `status::ERR_*` code.
///
/// # Safety
/// `session_id_ptr`/`session_id_len` must describe a valid UTF-8 byte range.
/// `server_pub_ptr`/`server_pub_len` must describe a valid byte range.
#[no_mangle]
pub unsafe extern "C" fn complete_session(
    session_id_ptr: *const u8,
    session_id_len: usize,
    server_pub_ptr: *const u8,
    server_pub_len: usize,
) -> i32 {
    let Some(session_id) = read_string(session_id_ptr, session_id_len) else {
        return status::ERR_INVALID_UTF8;
    };
    if server_pub_ptr.is_null() && server_pub_len != 0 {
        return status::ERR_NULL_POINTER;
    }

    let server_public_key = core::slice::from_raw_parts(server_pub_ptr, server_pub_len);

    let result = STORE.with(|store| store.borrow_mut().complete_session(&session_id, server_public_key));
    match result {
        Ok(()) => status::OK,
        Err(e) => map_complete_error(e),
    }
}

/// Decrypts an AES-256-GCM payload for `session_id` and returns a pointer to
/// the plaintext, writing its length to `out_len_ptr`. **Ownership of the
/// returned buffer is retained by Rust** -- the caller must pass the pointer
/// and length to [`clear_payload`] (never [`dealloc`]) once it has copied the
/// bytes out. Returns a null pointer and writes `0` to `out_len_ptr` on any
/// failure (unknown session, bad nonce length, or authentication failure --
/// deliberately not distinguished in the return value).
///
/// # Safety
/// `session_id_ptr`/`session_id_len` must describe a valid UTF-8 byte range.
/// `nonce_ptr` must be valid for exactly [`session::NONCE_LEN`] bytes.
/// `ciphertext_ptr`/`ciphertext_len` must describe a valid byte range.
/// `out_len_ptr` must be valid for one `usize`.
#[no_mangle]
pub unsafe extern "C" fn decrypt_payload(
    session_id_ptr: *const u8,
    session_id_len: usize,
    nonce_ptr: *const u8,
    ciphertext_ptr: *const u8,
    ciphertext_len: usize,
    out_len_ptr: *mut usize,
) -> *mut u8 {
    let fail = |out_len_ptr: *mut usize| -> *mut u8 {
        if !out_len_ptr.is_null() {
            *out_len_ptr = 0;
        }
        core::ptr::null_mut()
    };

    let Some(session_id) = read_string(session_id_ptr, session_id_len) else {
        return fail(out_len_ptr);
    };
    if nonce_ptr.is_null() || out_len_ptr.is_null() {
        return fail(out_len_ptr);
    }
    if ciphertext_ptr.is_null() && ciphertext_len != 0 {
        return fail(out_len_ptr);
    }

    let nonce = core::slice::from_raw_parts(nonce_ptr, session::NONCE_LEN);
    let ciphertext = core::slice::from_raw_parts(ciphertext_ptr, ciphertext_len);

    let result = STORE.with(|store| store.borrow_mut().decrypt(&session_id, nonce, ciphertext));
    match result {
        Ok(mut plaintext) => {
            let ptr = plaintext.as_mut_ptr();
            let len = plaintext.len();
            mem::forget(plaintext);
            *out_len_ptr = len;
            ptr
        }
        Err(_) => fail(out_len_ptr),
    }
}

/// Zeroizes a plaintext buffer previously returned by [`decrypt_payload`] in
/// place, then deallocates it. Must be called exactly once per successful
/// [`decrypt_payload`] call, and only after the caller has finished reading
/// the bytes out (e.g. via `TextDecoder`) -- the TS bridge calls this in a
/// `finally` block so it always runs, even if mounting throws.
///
/// # Safety
/// `ptr`/`len` must be exactly the pointer/length pair most recently returned
/// by [`decrypt_payload`] for this buffer, and must not have been passed to
/// [`clear_payload`] or [`dealloc`] already.
#[no_mangle]
pub unsafe extern "C" fn clear_payload(ptr: *mut u8, len: usize) {
    if ptr.is_null() || len == 0 {
        return;
    }
    let mut buffer = Vec::from_raw_parts(ptr, len, len);
    buffer.zeroize();
    drop(buffer);
}

/// Removes and zeroizes a session's key material *without* generating a receipt -- for
/// failure-path cleanup, where nothing was ever successfully mounted so there is nothing to
/// prove was destroyed. A no-op if the session id is unknown or invalid UTF-8. Prefer
/// [`verify_and_end_session`]/[`end_with_receipt`] for a session that *did* mount.
///
/// # Safety
/// `session_id_ptr`/`session_id_len` must describe a valid UTF-8 byte range.
#[no_mangle]
pub unsafe extern "C" fn end_session(session_id_ptr: *const u8, session_id_len: usize) {
    let Some(session_id) = read_string(session_id_ptr, session_id_len) else {
        return;
    };
    STORE.with(|store| store.borrow_mut().end_session(&session_id));
}

/// Computes an HMAC-SHA256 signature (writing exactly [`session::SIGNATURE_LEN`] bytes to
/// `out_sig_ptr`) over an arbitrary message using `session_id`'s signing key. Used to sign an
/// outgoing telemetry receipt (Access Confirmed). Works whether or not the AES decryption key
/// has already been consumed by [`decrypt_payload`] -- the signing key outlives it. Does not
/// end the session. Returns [`status::OK`] or [`status::ERR_NO_SIGNING_KEY`].
///
/// # Safety
/// `session_id_ptr`/`session_id_len` and `message_ptr`/`message_len` must describe valid byte
/// ranges (the session id additionally valid UTF-8). `out_sig_ptr` must be valid for exactly
/// [`session::SIGNATURE_LEN`] bytes (allocate it with [`alloc`]).
#[no_mangle]
pub unsafe extern "C" fn sign(
    session_id_ptr: *const u8,
    session_id_len: usize,
    message_ptr: *const u8,
    message_len: usize,
    out_sig_ptr: *mut u8,
) -> i32 {
    let Some(session_id) = read_string(session_id_ptr, session_id_len) else {
        return status::ERR_INVALID_UTF8;
    };
    if (message_ptr.is_null() && message_len != 0) || out_sig_ptr.is_null() {
        return status::ERR_NULL_POINTER;
    }
    let message = core::slice::from_raw_parts(message_ptr, message_len);

    let result = STORE.with(|store| store.borrow().sign(&session_id, message));
    match result {
        Ok(signature) => {
            core::slice::from_raw_parts_mut(out_sig_ptr, session::SIGNATURE_LEN).copy_from_slice(&signature);
            status::OK
        }
        Err(e) => map_signing_error(e),
    }
}

/// Verifies `signature` over `message` against `session_id`'s signing key (constant-time),
/// *without* ending the session -- writing `1` to `out_valid_ptr` if valid, `0` otherwise. For
/// an incoming REFRESH control signal, which (unlike WITHDRAW) must not tear the mount down.
/// The TS bridge is expected to treat an invalid signature here as tampering and tear the
/// session down itself via [`end_with_receipt`], same as any other detected tamper. Returns
/// [`status::OK`] or [`status::ERR_NO_SIGNING_KEY`].
///
/// # Safety
/// `session_id_ptr`/`session_id_len`, `message_ptr`/`message_len`, and
/// `signature_ptr`/`signature_len` must describe valid byte ranges (the session id
/// additionally valid UTF-8). `out_valid_ptr` must be valid for one byte.
#[no_mangle]
pub unsafe extern "C" fn verify_signal(
    session_id_ptr: *const u8,
    session_id_len: usize,
    message_ptr: *const u8,
    message_len: usize,
    signature_ptr: *const u8,
    signature_len: usize,
    out_valid_ptr: *mut u8,
) -> i32 {
    let Some(session_id) = read_string(session_id_ptr, session_id_len) else {
        return status::ERR_INVALID_UTF8;
    };
    if (message_ptr.is_null() && message_len != 0)
        || (signature_ptr.is_null() && signature_len != 0)
        || out_valid_ptr.is_null()
    {
        return status::ERR_NULL_POINTER;
    }
    let message = core::slice::from_raw_parts(message_ptr, message_len);
    let signature = core::slice::from_raw_parts(signature_ptr, signature_len);

    let result = STORE.with(|store| store.borrow().verify(&session_id, message, signature));
    match result {
        Ok(valid) => {
            *out_valid_ptr = valid as u8;
            status::OK
        }
        Err(e) => map_signing_error(e),
    }
}

/// Verifies `signature` over `message` against `session_id`'s signing key (constant-time),
/// *also* signs `destruction_message` with that same key -- both happen while the key still
/// exists, in one atomic operation, so a Destruction Receipt can still be produced even though
/// this call also ends the session -- then unconditionally ends the session, valid or not.
/// Writes `1`/`0` to `out_valid_ptr` and exactly [`session::SIGNATURE_LEN`] bytes to
/// `out_destruction_sig_ptr` regardless of validity. Used for an incoming WITHDRAW control
/// signal: an invalid signature means an unauthenticated party injected a lifecycle event into
/// what should be a trusted channel, which is itself grounds for the same defensive teardown a
/// valid WITHDRAW would cause -- the TS bridge uses the written flag to choose which callback
/// fires (`onWithdraw` vs `onTamper`) and which `trigger` value to report in the destruction
/// receipt it sends using the returned signature, not whether to tear down (that already
/// happened). Returns [`status::OK`] if a signing key existed to check against, or
/// [`status::ERR_NO_SIGNING_KEY`] if the session id was unknown/still pending (nothing to
/// verify, sign, or end).
///
/// # Safety
/// `session_id_ptr`/`session_id_len`, `message_ptr`/`message_len`,
/// `signature_ptr`/`signature_len`, and `destruction_message_ptr`/`destruction_message_len`
/// must describe valid byte ranges (the session id additionally valid UTF-8).
/// `out_valid_ptr` must be valid for one byte; `out_destruction_sig_ptr` must be valid for
/// exactly [`session::SIGNATURE_LEN`] bytes (allocate it with [`alloc`]).
#[no_mangle]
pub unsafe extern "C" fn verify_and_end_session(
    session_id_ptr: *const u8,
    session_id_len: usize,
    message_ptr: *const u8,
    message_len: usize,
    signature_ptr: *const u8,
    signature_len: usize,
    destruction_message_ptr: *const u8,
    destruction_message_len: usize,
    out_valid_ptr: *mut u8,
    out_destruction_sig_ptr: *mut u8,
) -> i32 {
    let Some(session_id) = read_string(session_id_ptr, session_id_len) else {
        return status::ERR_INVALID_UTF8;
    };
    if (message_ptr.is_null() && message_len != 0)
        || (signature_ptr.is_null() && signature_len != 0)
        || (destruction_message_ptr.is_null() && destruction_message_len != 0)
        || out_valid_ptr.is_null()
        || out_destruction_sig_ptr.is_null()
    {
        return status::ERR_NULL_POINTER;
    }
    let message = core::slice::from_raw_parts(message_ptr, message_len);
    let signature = core::slice::from_raw_parts(signature_ptr, signature_len);
    let destruction_message = core::slice::from_raw_parts(destruction_message_ptr, destruction_message_len);

    let result = STORE.with(|store| {
        store.borrow_mut().verify_and_end(&session_id, message, signature, destruction_message)
    });
    match result {
        Ok((valid, destruction_signature)) => {
            *out_valid_ptr = valid as u8;
            core::slice::from_raw_parts_mut(out_destruction_sig_ptr, session::SIGNATURE_LEN)
                .copy_from_slice(&destruction_signature);
            status::OK
        }
        Err(e) => map_signing_error(e),
    }
}

/// Signs a destruction-receipt `message` (writing exactly [`session::SIGNATURE_LEN`] bytes to
/// `out_sig_ptr`) using `session_id`'s signing key, then ends the session (removes and
/// zeroizes all remaining key material). For client-initiated termination that still needs a
/// signed Proof-of-Destruction receipt -- tamper detected locally, or the component
/// unmounting -- as opposed to [`verify_and_end_session`], which already has an incoming
/// signature to check for the WITHDRAW/REFRESH case. Returns [`status::OK`] or
/// [`status::ERR_NO_SIGNING_KEY`].
///
/// # Safety
/// `session_id_ptr`/`session_id_len` and `message_ptr`/`message_len` must describe valid byte
/// ranges (the session id additionally valid UTF-8). `out_sig_ptr` must be valid for exactly
/// [`session::SIGNATURE_LEN`] bytes (allocate it with [`alloc`]).
#[no_mangle]
pub unsafe extern "C" fn end_with_receipt(
    session_id_ptr: *const u8,
    session_id_len: usize,
    message_ptr: *const u8,
    message_len: usize,
    out_sig_ptr: *mut u8,
) -> i32 {
    let Some(session_id) = read_string(session_id_ptr, session_id_len) else {
        return status::ERR_INVALID_UTF8;
    };
    if (message_ptr.is_null() && message_len != 0) || out_sig_ptr.is_null() {
        return status::ERR_NULL_POINTER;
    }
    let message = core::slice::from_raw_parts(message_ptr, message_len);

    let result = STORE.with(|store| store.borrow_mut().end_with_receipt(&session_id, message));
    match result {
        Ok(signature) => {
            core::slice::from_raw_parts_mut(out_sig_ptr, session::SIGNATURE_LEN).copy_from_slice(&signature);
            status::OK
        }
        Err(e) => map_signing_error(e),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use p256::elliptic_curve::sec1::ToEncodedPoint;

    // Fixed, arbitrary 32-byte seeds -- test-only fixture data (#[cfg(test)], never compiled
    // into the shipped wasm module), matching session::tests' own seeds for cross-consistency.
    const CLIENT_SEED: [u8; 32] = [ // codeql[rust/hard-coded-cryptographic-value] -- test-only fixture, see comment above
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25,
        26, 27, 28, 29, 30, 31, 32,
    ];
    const SERVER_SEED: [u8; 32] = [ // codeql[rust/hard-coded-cryptographic-value] -- test-only fixture, see comment above
        33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55,
        56, 57, 58, 59, 60, 61, 62, 63, 64,
    ];

    /// Drives the exported `extern "C"` functions exactly the way the TS bridge
    /// does: raw pointers into buffers this test owns, standing in for wasm
    /// linear memory (on the host target there is no separate "wasm memory" --
    /// the ABI shape is identical, which is exactly what we want to exercise).
    #[test]
    fn full_ffi_handshake_decrypt_and_clear_round_trips() {
        // A distinct STORE per #[test] fn would need real thread-local isolation
        // per test thread; Rust's test harness runs each #[test] on its own OS
        // thread by default, and thread_local! gives each thread its own STORE,
        // so session ids below never collide across tests.
        let session_id = b"ffi-session-1";

        // 1. begin_session
        let mut out_pub = [0u8; session::PUBLIC_KEY_LEN];
        let begin_rc = unsafe {
            begin_session(session_id.as_ptr(), session_id.len(), CLIENT_SEED.as_ptr(), out_pub.as_mut_ptr())
        };
        assert_eq!(begin_rc, status::OK);
        assert_eq!(out_pub[0], 0x04);

        // 2. complete_session, against a server keypair built the same way the
        // Rust-side unit tests build one (see session::tests), independent of
        // the client's out_pub above.
        let server_secret = p256::SecretKey::from_slice(&SERVER_SEED).expect("valid server seed");
        let server_public: [u8; session::PUBLIC_KEY_LEN] =
            server_secret.public_key().to_encoded_point(false).as_bytes().try_into().unwrap();

        let complete_rc = unsafe {
            complete_session(session_id.as_ptr(), session_id.len(), server_public.as_ptr(), server_public.len())
        };
        assert_eq!(complete_rc, status::OK);

        // 3. Encrypt as the server would, deriving the same shared key.
        let client_public = p256::PublicKey::from_sec1_bytes(&out_pub).expect("valid client public key");
        let shared = p256::ecdh::diffie_hellman(server_secret.to_nonzero_scalar(), client_public.as_affine());
        let key_bytes: [u8; session::KEY_LEN] = (*shared.raw_secret_bytes()).into();
        let nonce_bytes = [9u8; session::NONCE_LEN]; // codeql[rust/hard-coded-cryptographic-value] -- fixed nonce for a deterministic FFI test vector; test-only, never shipped
        let plaintext = b"withdrawn households still keep their message history";
        let ciphertext = {
            use aes_gcm::aead::{Aead, KeyInit};
            let cipher = aes_gcm::Aes256Gcm::new(&aes_gcm::Key::<aes_gcm::Aes256Gcm>::from(key_bytes));
            cipher.encrypt(&aes_gcm::Nonce::from(nonce_bytes), plaintext.as_slice()).expect("encrypt")
        };

        // 4. decrypt_payload
        let mut out_len: usize = 0;
        let ptr = unsafe {
            decrypt_payload(
                session_id.as_ptr(),
                session_id.len(),
                nonce_bytes.as_ptr(),
                ciphertext.as_ptr(),
                ciphertext.len(),
                &mut out_len as *mut usize,
            )
        };
        assert!(!ptr.is_null());
        assert_eq!(out_len, plaintext.len());

        let decrypted = unsafe { core::slice::from_raw_parts(ptr, out_len) };
        assert_eq!(decrypted, plaintext);

        // 5. clear_payload must not panic and (per its contract) leaves the
        // buffer scrubbed and deallocated. We cannot safely re-read `ptr` after
        // this call (that memory is freed) -- which is exactly the point.
        unsafe { clear_payload(ptr, out_len) };

        // 6. end_session removes the key; a second decrypt attempt now fails.
        unsafe { end_session(session_id.as_ptr(), session_id.len()) };
        let mut out_len2: usize = 0;
        let ptr2 = unsafe {
            decrypt_payload(
                session_id.as_ptr(),
                session_id.len(),
                nonce_bytes.as_ptr(),
                ciphertext.as_ptr(),
                ciphertext.len(),
                &mut out_len2 as *mut usize,
            )
        };
        assert!(ptr2.is_null());
        assert_eq!(out_len2, 0);
    }

    #[test]
    fn begin_session_rejects_invalid_utf8_session_id() {
        let invalid_utf8: [u8; 2] = [0xFF, 0xFE];
        let mut out_pub = [0u8; session::PUBLIC_KEY_LEN];
        let rc = unsafe {
            begin_session(invalid_utf8.as_ptr(), invalid_utf8.len(), CLIENT_SEED.as_ptr(), out_pub.as_mut_ptr())
        };
        assert_eq!(rc, status::ERR_INVALID_UTF8);
    }

    #[test]
    fn begin_session_rejects_an_all_zero_seed() {
        let session_id = b"zero-seed-session";
        let zero_seed = [0u8; 32]; // codeql[rust/hard-coded-cryptographic-value] -- deliberately-invalid all-zero seed asserting rejection; test-only fixture
        let mut out_pub = [0u8; session::PUBLIC_KEY_LEN];
        let rc = unsafe {
            begin_session(session_id.as_ptr(), session_id.len(), zero_seed.as_ptr(), out_pub.as_mut_ptr())
        };
        assert_eq!(rc, status::ERR_INVALID_SEED);
    }

    #[test]
    fn complete_session_without_begin_is_rejected() {
        let session_id = b"never-began";
        let server_public = [0x04u8; session::PUBLIC_KEY_LEN]; // codeql[rust/hard-coded-cryptographic-value] -- deliberately-malformed key material asserting rejection; test-only fixture
        let rc = unsafe {
            complete_session(session_id.as_ptr(), session_id.len(), server_public.as_ptr(), server_public.len())
        };
        assert_eq!(rc, status::ERR_NOT_PENDING);
    }

    #[test]
    fn decrypt_payload_on_unknown_session_returns_null() {
        let session_id = b"unknown-for-decrypt";
        let nonce = [0u8; session::NONCE_LEN]; // codeql[rust/hard-coded-cryptographic-value] -- fixed nonce, test-only fixture
        let ciphertext = b"irrelevant";
        let mut out_len: usize = 0xDEADBEEF; // sentinel: must be reset to 0 on failure
        let ptr = unsafe {
            decrypt_payload(
                session_id.as_ptr(),
                session_id.len(),
                nonce.as_ptr(),
                ciphertext.as_ptr(),
                ciphertext.len(),
                &mut out_len as *mut usize,
            )
        };
        assert!(ptr.is_null());
        assert_eq!(out_len, 0);
    }

    #[test]
    fn clear_payload_on_null_or_zero_length_is_a_harmless_no_op() {
        unsafe {
            clear_payload(core::ptr::null_mut(), 0);
            clear_payload(core::ptr::null_mut(), 16);
        }
    }

    #[test]
    fn alloc_dealloc_round_trip_does_not_panic() {
        let ptr = alloc(128);
        assert!(!ptr.is_null());
        unsafe { dealloc(ptr, 128) };
    }

    /// Drives sign/verify_and_end_session/end_with_receipt through the raw pointer ABI,
    /// exactly as ephemeral-chat.ts does: sign an Access Receipt right after decrypt (the AES
    /// key is already consumed at that point -- proving the signing key genuinely outlives
    /// it), then verify+consume a WITHDRAW signature produced independently (standing in for
    /// the broker), matching production's cross-language signing.
    #[test]
    fn sign_and_verify_and_end_session_agree_over_the_ffi_boundary() {
        let session_id = b"ffi-signing-session";

        let mut out_pub = [0u8; session::PUBLIC_KEY_LEN];
        unsafe { begin_session(session_id.as_ptr(), session_id.len(), CLIENT_SEED.as_ptr(), out_pub.as_mut_ptr()) };

        let server_secret = p256::SecretKey::from_slice(&SERVER_SEED).expect("valid server seed");
        let server_public: [u8; session::PUBLIC_KEY_LEN] =
            server_secret.public_key().to_encoded_point(false).as_bytes().try_into().unwrap();
        unsafe {
            complete_session(session_id.as_ptr(), session_id.len(), server_public.as_ptr(), server_public.len())
        };

        // Consume the AES key via a real decrypt, exactly like production.
        let client_public = p256::PublicKey::from_sec1_bytes(&out_pub).expect("valid client public key");
        let shared = p256::ecdh::diffie_hellman(server_secret.to_nonzero_scalar(), client_public.as_affine());
        let key_bytes: [u8; session::KEY_LEN] = (*shared.raw_secret_bytes()).into();
        let nonce_bytes = [3u8; session::NONCE_LEN]; // codeql[rust/hard-coded-cryptographic-value] -- fixed nonce; test-only fixture
        let plaintext = b"access confirmed";
        let ciphertext = {
            use aes_gcm::aead::{Aead, KeyInit};
            let cipher = aes_gcm::Aes256Gcm::new(&aes_gcm::Key::<aes_gcm::Aes256Gcm>::from(key_bytes));
            cipher.encrypt(&aes_gcm::Nonce::from(nonce_bytes), plaintext.as_slice()).expect("encrypt")
        };
        let mut out_len: usize = 0;
        let ptr = unsafe {
            decrypt_payload(
                session_id.as_ptr(),
                session_id.len(),
                nonce_bytes.as_ptr(),
                ciphertext.as_ptr(),
                ciphertext.len(),
                &mut out_len as *mut usize,
            )
        };
        assert!(!ptr.is_null());
        unsafe { clear_payload(ptr, out_len) };

        // Sign an Access Receipt -- the signing key must still work with the AES key gone.
        let access_message = b"ffi-signing-session|ACCESS_CONFIRMED";
        let mut access_sig = [0u8; session::SIGNATURE_LEN];
        let sign_rc = unsafe {
            sign(session_id.as_ptr(), session_id.len(), access_message.as_ptr(), access_message.len(), access_sig.as_mut_ptr())
        };
        assert_eq!(sign_rc, status::OK);
        assert_ne!(access_sig, [0u8; session::SIGNATURE_LEN], "a real signature was written, not left zeroed");

        // Independently derive the same signing key the way a broker would (raw shared secret
        // through the SAME domain-separated HMAC construction) and verify a WITHDRAW signature
        // it "sent" -- proving the FFI verify path actually checks against session state, not
        // just recomputing from scratch.
        let withdraw_message = b"ffi-signing-session|WITHDRAW";
        let broker_signature = {
            use hmac::{Hmac, Mac};
            use sha2::Sha256;
            let mut key_mac = <Hmac<Sha256> as Mac>::new_from_slice(&key_bytes).unwrap();
            key_mac.update(b"dx-security/hmac-key/v1");
            let hmac_key = key_mac.finalize().into_bytes();
            let mut sig_mac = <Hmac<Sha256> as Mac>::new_from_slice(&hmac_key).unwrap();
            sig_mac.update(withdraw_message);
            sig_mac.finalize().into_bytes()
        };

        let destruction_message = b"ffi-signing-session|WITHDRAW_EVENT";
        let mut out_valid: u8 = 0xFF; // sentinel: must become exactly 0 or 1
        let mut out_destruction_sig = [0u8; session::SIGNATURE_LEN];
        let verify_rc = unsafe {
            verify_and_end_session(
                session_id.as_ptr(),
                session_id.len(),
                withdraw_message.as_ptr(),
                withdraw_message.len(),
                broker_signature.as_ptr(),
                broker_signature.len(),
                destruction_message.as_ptr(),
                destruction_message.len(),
                &mut out_valid as *mut u8,
                out_destruction_sig.as_mut_ptr(),
            )
        };
        assert_eq!(verify_rc, status::OK);
        assert_eq!(out_valid, 1, "an independently-derived, correctly-constructed signature must verify");
        assert_ne!(out_destruction_sig, [0u8; session::SIGNATURE_LEN], "a destruction receipt signature was produced too");

        // The session is gone now -- signing again reports ERR_NO_SIGNING_KEY.
        let mut post_sig = [0u8; session::SIGNATURE_LEN];
        let post_rc = unsafe {
            sign(session_id.as_ptr(), session_id.len(), access_message.as_ptr(), access_message.len(), post_sig.as_mut_ptr())
        };
        assert_eq!(post_rc, status::ERR_NO_SIGNING_KEY);
    }

    #[test]
    fn verify_and_end_session_rejects_a_forged_signature_via_the_ffi_boundary_and_still_tears_down() {
        let session_id = b"ffi-forged-signature-session";
        let mut out_pub = [0u8; session::PUBLIC_KEY_LEN];
        unsafe { begin_session(session_id.as_ptr(), session_id.len(), CLIENT_SEED.as_ptr(), out_pub.as_mut_ptr()) };
        let server_secret = p256::SecretKey::from_slice(&SERVER_SEED).expect("valid server seed");
        let server_public: [u8; session::PUBLIC_KEY_LEN] =
            server_secret.public_key().to_encoded_point(false).as_bytes().try_into().unwrap();
        unsafe {
            complete_session(session_id.as_ptr(), session_id.len(), server_public.as_ptr(), server_public.len())
        };

        let message = b"ffi-forged-signature-session|WITHDRAW";
        let forged = [0x00u8; session::SIGNATURE_LEN]; // codeql[rust/hard-coded-cryptographic-value] -- deliberately-invalid forged signature; test-only fixture
        let destruction_message = b"ffi-forged-signature-session|TAMPER_DETECTED";
        let mut out_valid: u8 = 0xFF;
        let mut out_destruction_sig = [0u8; session::SIGNATURE_LEN];
        let verify_rc = unsafe {
            verify_and_end_session(
                session_id.as_ptr(),
                session_id.len(),
                message.as_ptr(),
                message.len(),
                forged.as_ptr(),
                forged.len(),
                destruction_message.as_ptr(),
                destruction_message.len(),
                &mut out_valid as *mut u8,
                out_destruction_sig.as_mut_ptr(),
            )
        };
        assert_eq!(verify_rc, status::OK, "a session existed to check against, even though the signature was wrong");
        assert_eq!(out_valid, 0, "a forged signature must not verify");
        assert_ne!(
            out_destruction_sig,
            [0u8; session::SIGNATURE_LEN],
            "a destruction receipt is still produced even when the incoming signature was forged",
        );

        // Still torn down -- a second verify attempt has no signing key left to check against.
        let mut out_valid2: u8 = 0xFF;
        let mut out_destruction_sig2 = [0u8; session::SIGNATURE_LEN];
        let second_rc = unsafe {
            verify_and_end_session(
                session_id.as_ptr(),
                session_id.len(),
                message.as_ptr(),
                message.len(),
                forged.as_ptr(),
                forged.len(),
                destruction_message.as_ptr(),
                destruction_message.len(),
                &mut out_valid2 as *mut u8,
                out_destruction_sig2.as_mut_ptr(),
            )
        };
        assert_eq!(second_rc, status::ERR_NO_SIGNING_KEY);
    }

    #[test]
    fn end_with_receipt_via_the_ffi_boundary_signs_and_tears_down() {
        let session_id = b"ffi-end-with-receipt-session";
        let mut out_pub = [0u8; session::PUBLIC_KEY_LEN];
        unsafe { begin_session(session_id.as_ptr(), session_id.len(), CLIENT_SEED.as_ptr(), out_pub.as_mut_ptr()) };
        let server_secret = p256::SecretKey::from_slice(&SERVER_SEED).expect("valid server seed");
        let server_public: [u8; session::PUBLIC_KEY_LEN] =
            server_secret.public_key().to_encoded_point(false).as_bytes().try_into().unwrap();
        unsafe {
            complete_session(session_id.as_ptr(), session_id.len(), server_public.as_ptr(), server_public.len())
        };

        let message = b"ffi-end-with-receipt-session|COMPONENT_UNMOUNT";
        let mut signature = [0u8; session::SIGNATURE_LEN];
        let rc = unsafe {
            end_with_receipt(session_id.as_ptr(), session_id.len(), message.as_ptr(), message.len(), signature.as_mut_ptr())
        };
        assert_eq!(rc, status::OK);
        assert_ne!(signature, [0u8; session::SIGNATURE_LEN]);

        let mut out_valid: u8 = 0xFF;
        let mut out_destruction_sig = [0u8; session::SIGNATURE_LEN];
        let post_rc = unsafe {
            verify_and_end_session(
                session_id.as_ptr(),
                session_id.len(),
                message.as_ptr(),
                message.len(),
                signature.as_ptr(),
                signature.len(),
                message.as_ptr(),
                message.len(),
                &mut out_valid as *mut u8,
                out_destruction_sig.as_mut_ptr(),
            )
        };
        assert_eq!(post_rc, status::ERR_NO_SIGNING_KEY, "end_with_receipt must have already torn the session down");
    }
}
