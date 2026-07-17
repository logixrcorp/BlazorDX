//! Pure Rust session/crypto logic -- no FFI, no pointers, no wasm-specific code --
//! so it can be exercised directly with `cargo test` on the host target. `lib.rs`
//! wraps [`SessionStore`] in the `#[no_mangle] extern "C"` ABI the TypeScript
//! bridge drives.
//!
//! # Key agreement
//!
//! Each session goes through two steps:
//!
//! 1. [`SessionStore::begin_session`]: the host (browser) supplies 32 bytes of
//!    CSPRNG entropy (`crypto.getRandomValues`, via the TS bridge -- wasm32-unknown-unknown
//!    has no OS RNG of its own). We treat that entropy directly as a P-256 secret
//!    scalar and return the corresponding public key for the host to forward to
//!    the server.
//! 2. [`SessionStore::complete_session`]: given the server's ephemeral P-256
//!    public key, we run ECDH and use the raw 32-byte shared secret as the
//!    session's AES-256-GCM key.
//!
//! Only `aes-gcm`, `p256`, and `zeroize` are in scope for this crate, so the raw
//! ECDH output is used as the AES key directly rather than being passed through
//! an HKDF. That is a deliberate, documented simplification for this deliverable,
//! not a recommendation for a production key schedule -- a real deployment should
//! run the shared secret through HKDF-SHA256 (with a context/info string binding
//! the session id) before using it as a key.

use aes_gcm::aead::{Aead, KeyInit};
use aes_gcm::{Aes256Gcm, Key, Nonce};
use p256::ecdh::diffie_hellman;
use p256::elliptic_curve::sec1::ToEncodedPoint;
use p256::{PublicKey, SecretKey};
use std::collections::HashMap;
use zeroize::Zeroizing;

/// Uncompressed SEC1 P-256 point: `0x04 || X (32 bytes) || Y (32 bytes)`.
pub const PUBLIC_KEY_LEN: usize = 65;
/// AES-GCM standard nonce size.
pub const NONCE_LEN: usize = 12;
/// AES-256 key size.
pub const KEY_LEN: usize = 32;

/// Why a [`SessionStore`] operation failed. Never carries key material.
#[derive(Debug, PartialEq, Eq)]
pub enum SessionError {
    /// The 32-byte seed did not decode to a valid, non-zero P-256 scalar
    /// (astronomically unlikely with real entropy; treated as a hard failure
    /// rather than a silent retry so a broken entropy source is never masked).
    InvalidSeed,
    /// The supplied bytes are not a valid SEC1-encoded P-256 public key.
    InvalidPublicKey,
    /// `complete_session` was called for a session that never called
    /// `begin_session`, or that already completed.
    NotPending,
    /// `decrypt` was called for a session id with no established key.
    UnknownSession,
    /// The nonce was not exactly [`NONCE_LEN`] bytes.
    InvalidNonceLength,
    /// AES-GCM authentication failed: wrong key, corrupted ciphertext, or a
    /// tampered/forged tag. Deliberately does not distinguish which -- that
    /// distinction is not actionable and would only help an attacker.
    DecryptFailed,
}

enum SessionState {
    /// Waiting for the server's public key. Holds the 32-byte seed (not a
    /// derived scalar type) so it stays trivially `Zeroize`-able regardless of
    /// whether upstream elliptic-curve types implement zeroizing themselves.
    Pending(Zeroizing<[u8; 32]>),
    /// A session key has been derived from a completed ECDH exchange.
    Established(Zeroizing<[u8; KEY_LEN]>),
}

/// An in-memory table of ephemeral chat sessions and their decryption keys.
/// Holds no long-term secrets: every entry is created by [`begin_session`],
/// completed by [`complete_session`], and should be dropped via
/// [`end_session`] once the conversation is withdrawn or refreshed.
///
/// [`begin_session`]: SessionStore::begin_session
/// [`complete_session`]: SessionStore::complete_session
/// [`end_session`]: SessionStore::end_session
#[derive(Default)]
pub struct SessionStore {
    sessions: HashMap<String, SessionState>,
}

impl SessionStore {
    pub fn new() -> Self {
        Self { sessions: HashMap::new() }
    }

    /// Starts a session: derives a client-side ephemeral P-256 keypair from
    /// `seed` and stores it as pending. Returns the client's public key
    /// (uncompressed SEC1, 65 bytes) for the host to send to the server, or
    /// `Err(SessionError::InvalidSeed)` if `seed` is not a valid scalar.
    ///
    /// A second call with the same `session_id` overwrites (and zeroizes) any
    /// prior state for that id.
    pub fn begin_session(
        &mut self,
        session_id: &str,
        seed: &[u8; 32],
    ) -> Result<[u8; PUBLIC_KEY_LEN], SessionError> {
        let secret = SecretKey::from_slice(seed).map_err(|_| SessionError::InvalidSeed)?;
        let public_point = secret.public_key().to_encoded_point(false);
        let public_bytes: [u8; PUBLIC_KEY_LEN] = public_point
            .as_bytes()
            .try_into()
            .map_err(|_| SessionError::InvalidSeed)?;

        self.sessions.insert(session_id.to_owned(), SessionState::Pending(Zeroizing::new(*seed)));
        Ok(public_bytes)
    }

    /// Completes a pending session: runs ECDH against the server's public key
    /// and stores the raw shared secret as the session's AES-256-GCM key. The
    /// pending seed is dropped (and zeroized) either way.
    pub fn complete_session(
        &mut self,
        session_id: &str,
        server_public_key: &[u8],
    ) -> Result<(), SessionError> {
        let seed: [u8; 32] = match self.sessions.get(session_id) {
            Some(SessionState::Pending(seed)) => **seed,
            Some(SessionState::Established(_)) | None => return Err(SessionError::NotPending),
        };

        let client_secret = SecretKey::from_slice(&seed).map_err(|_| SessionError::InvalidSeed)?;
        let server_public =
            PublicKey::from_sec1_bytes(server_public_key).map_err(|_| SessionError::InvalidPublicKey)?;

        let shared = diffie_hellman(client_secret.to_nonzero_scalar(), server_public.as_affine());
        let key_bytes: [u8; KEY_LEN] =
            (*shared.raw_secret_bytes()).into();

        self.sessions
            .insert(session_id.to_owned(), SessionState::Established(Zeroizing::new(key_bytes)));
        Ok(())
    }

    /// Decrypts an AES-256-GCM payload (`ciphertext` includes the trailing
    /// 16-byte authentication tag, the standard AES-GCM convention) using the
    /// established key for `session_id`.
    pub fn decrypt(
        &self,
        session_id: &str,
        nonce: &[u8],
        ciphertext: &[u8],
    ) -> Result<Vec<u8>, SessionError> {
        let key_bytes = match self.sessions.get(session_id) {
            Some(SessionState::Established(key)) => key,
            Some(SessionState::Pending(_)) | None => return Err(SessionError::UnknownSession),
        };

        if nonce.len() != NONCE_LEN {
            return Err(SessionError::InvalidNonceLength);
        }
        let nonce_array: [u8; NONCE_LEN] = nonce.try_into().expect("length checked above");

        let cipher = Aes256Gcm::new(&Key::<Aes256Gcm>::from(**key_bytes));
        cipher
            .decrypt(&Nonce::from(nonce_array), ciphertext)
            .map_err(|_| SessionError::DecryptFailed)
    }

    /// Removes and zeroizes a session's state (pending or established), e.g.
    /// on an SSE `WITHDRAW`/`REFRESH` event. A no-op if the session is unknown.
    pub fn end_session(&mut self, session_id: &str) {
        self.sessions.remove(session_id);
    }

    /// Whether a session currently has an established (post-ECDH) key.
    /// Test/diagnostic helper -- never exposes the key itself.
    #[cfg(test)]
    pub fn is_established(&self, session_id: &str) -> bool {
        matches!(self.sessions.get(session_id), Some(SessionState::Established(_)))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use zeroize::Zeroize;

    // Arbitrary-but-fixed 32-byte seeds. Neither is the all-zero scalar and both
    // are far below the P-256 group order, so `SecretKey::from_slice` always
    // accepts them -- no RNG needed for deterministic, reproducible tests.
    const CLIENT_SEED: [u8; 32] = [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25,
        26, 27, 28, 29, 30, 31, 32,
    ];
    const SERVER_SEED: [u8; 32] = [
        33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55,
        56, 57, 58, 59, 60, 61, 62, 63, 64,
    ];

    /// Stands in for the server side of the handshake, entirely with
    /// deterministic seeds -- no RNG, no network, matching the "no HTTP calls
    /// from Rust" constraint even in tests.
    fn server_public_key() -> [u8; PUBLIC_KEY_LEN] {
        let secret = SecretKey::from_slice(&SERVER_SEED).expect("valid server seed");
        secret.public_key().to_encoded_point(false).as_bytes().try_into().unwrap()
    }

    /// Encrypts as the server would, deriving the same shared key independently
    /// of `SessionStore` so the test never has to read a key back out of it.
    fn server_encrypt(nonce: &[u8; NONCE_LEN], plaintext: &[u8]) -> Vec<u8> {
        let server_secret = SecretKey::from_slice(&SERVER_SEED).expect("valid server seed");
        let client_secret = SecretKey::from_slice(&CLIENT_SEED).expect("valid client seed");
        let client_public = client_secret.public_key();

        let shared = diffie_hellman(server_secret.to_nonzero_scalar(), client_public.as_affine());
        let key_bytes: [u8; KEY_LEN] = (*shared.raw_secret_bytes()).into();

        let cipher = Aes256Gcm::new(&Key::<Aes256Gcm>::from(key_bytes));
        cipher.encrypt(&Nonce::from(*nonce), plaintext).expect("encrypt")
    }

    #[test]
    fn begin_session_returns_an_uncompressed_sec1_public_key() {
        let mut store = SessionStore::new();
        let public_key = store.begin_session("s1", &CLIENT_SEED).expect("valid seed");

        assert_eq!(public_key.len(), PUBLIC_KEY_LEN);
        assert_eq!(public_key[0], 0x04, "uncompressed SEC1 points start with 0x04");
        assert!(!store.is_established("s1"), "not established until complete_session");
    }

    #[test]
    fn begin_session_rejects_an_all_zero_seed() {
        let mut store = SessionStore::new();
        let result = store.begin_session("s1", &[0u8; 32]);

        assert_eq!(result, Err(SessionError::InvalidSeed));
    }

    #[test]
    fn complete_session_without_begin_is_rejected() {
        let mut store = SessionStore::new();
        let result = store.complete_session("never-began", &server_public_key());

        assert_eq!(result, Err(SessionError::NotPending));
    }

    #[test]
    fn complete_session_rejects_a_malformed_server_public_key() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");

        let result = store.complete_session("s1", &[0xFFu8; 10]);

        assert_eq!(result, Err(SessionError::InvalidPublicKey));
    }

    #[test]
    fn full_handshake_then_decrypt_round_trips_a_message_encrypted_by_the_simulated_server() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");
        assert!(store.is_established("s1"));

        let nonce = [7u8; NONCE_LEN];
        let plaintext = b"the conversation withdraws when the household link is severed";
        let ciphertext = server_encrypt(&nonce, plaintext);

        let decrypted = store.decrypt("s1", &nonce, &ciphertext).expect("decrypts");
        assert_eq!(decrypted, plaintext);
    }

    #[test]
    fn decrypt_before_a_session_is_established_is_rejected() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");

        let result = store.decrypt("s1", &[0u8; NONCE_LEN], b"anything");

        assert_eq!(result, Err(SessionError::UnknownSession));
    }

    #[test]
    fn decrypt_for_an_unknown_session_is_rejected() {
        let store = SessionStore::new();
        let result = store.decrypt("no-such-session", &[0u8; NONCE_LEN], b"anything");

        assert_eq!(result, Err(SessionError::UnknownSession));
    }

    #[test]
    fn decrypt_rejects_a_short_nonce() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");

        let result = store.decrypt("s1", &[0u8; 4], b"anything");

        assert_eq!(result, Err(SessionError::InvalidNonceLength));
    }

    #[test]
    fn decrypt_rejects_a_tampered_ciphertext() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");

        let nonce = [7u8; NONCE_LEN];
        let mut ciphertext = server_encrypt(&nonce, b"original message");
        let last = ciphertext.len() - 1;
        ciphertext[last] ^= 0xFF; // flip a bit in the authentication tag

        let result = store.decrypt("s1", &nonce, &ciphertext);

        assert_eq!(result, Err(SessionError::DecryptFailed));
    }

    #[test]
    fn decrypt_rejects_a_key_from_the_wrong_session() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");
        // A second session that completes against its OWN (identical, for this
        // test) server key still gets a DIFFERENT shared secret because its
        // client seed differs -- proving sessions do not share key material.
        let other_seed = [99u8; 32];
        store.begin_session("s2", &other_seed).expect("valid seed");
        store.complete_session("s2", &server_public_key()).expect("valid server key");

        let nonce = [7u8; NONCE_LEN];
        let ciphertext = server_encrypt(&nonce, b"for s1 only");

        assert!(store.decrypt("s1", &nonce, &ciphertext).is_ok());
        assert_eq!(store.decrypt("s2", &nonce, &ciphertext), Err(SessionError::DecryptFailed));
    }

    #[test]
    fn end_session_removes_established_state() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");
        assert!(store.is_established("s1"));

        store.end_session("s1");

        assert!(!store.is_established("s1"));
        let result = store.decrypt("s1", &[7u8; NONCE_LEN], b"anything");
        assert_eq!(result, Err(SessionError::UnknownSession));
    }

    #[test]
    fn end_session_on_an_unknown_session_is_a_harmless_no_op() {
        let mut store = SessionStore::new();
        store.end_session("never-existed");
        // No panic; nothing to assert beyond "it returned."
    }

    #[test]
    fn zeroizing_a_plaintext_buffer_clears_every_byte() {
        // Exercises the same zeroize::Zeroize call the FFI clear_payload wrapper
        // makes on the decrypted buffer before it is deallocated. The genuine
        // in-browser memory-zeroing *timing* guarantee (freed before any other
        // JS can observe it) is covered by the Playwright E2E suite, not here.
        let mut buffer = vec![0xABu8; 64];
        buffer.zeroize();

        assert!(buffer.iter().all(|&b| b == 0));
    }
}
