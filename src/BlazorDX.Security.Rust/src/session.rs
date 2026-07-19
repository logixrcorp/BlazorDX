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
//!    public key, we run ECDH. The raw 32-byte shared secret becomes the session's
//!    AES-256-GCM key directly (unchanged, documented simplification below), and
//!    *separately* an HMAC-SHA256 signing key domain-separated from it (see
//!    [`derive_signing_key`]).
//!
//! Only `aes-gcm`, `p256`, `hmac`/`sha2`, and `zeroize` are in scope for this crate, so the
//! raw ECDH output is used as the AES key directly rather than being passed through a full
//! HKDF. That is a deliberate, documented simplification for this deliverable, not a
//! recommendation for a production key schedule -- a real deployment should run the shared
//! secret through HKDF-SHA256 (with a context/info string binding the session id) before
//! using it as a key. The signing key *is* domain-separated from the AES key (via a second,
//! differently-labeled HMAC over the same raw secret) specifically so a signature can never
//! be mistaken for, or reused as, encryption key material.
//!
//! # Signing key lifetime outlives the AES key
//!
//! [`SessionStore::decrypt`] consumes the AES key on its first call (`Option::take`),
//! structurally enforcing "one payload per handshake" rather than relying on an external
//! caller to remember to tear the session down. The HMAC signing key is *not* consumed by
//! decrypt -- it lives on inside the same [`EstablishedKeys`] so the session can still sign
//! an Access Receipt right after mounting, verify a WITHDRAW/REFRESH that arrives later, and
//! sign a Destruction Receipt at the moment it is finally torn down. Only [`SessionStore::end_session`]
//! (bare cleanup, no receipt), [`SessionStore::verify_and_end`], or
//! [`SessionStore::end_with_receipt`] remove and zeroize it.

use aes_gcm::aead::{Aead, KeyInit};
use aes_gcm::{Aes256Gcm, Key, Nonce};
use hmac::{Hmac, Mac};
use p256::ecdh::diffie_hellman;
use p256::elliptic_curve::sec1::ToEncodedPoint;
use p256::{PublicKey, SecretKey};
use sha2::Sha256;
use std::collections::HashMap;
use zeroize::Zeroizing;

type HmacSha256 = Hmac<Sha256>;

/// Uncompressed SEC1 P-256 point: `0x04 || X (32 bytes) || Y (32 bytes)`.
pub const PUBLIC_KEY_LEN: usize = 65;
/// AES-GCM standard nonce size.
pub const NONCE_LEN: usize = 12;
/// AES-256 key size, and the HMAC-SHA256 signing key size (both 32 bytes).
pub const KEY_LEN: usize = 32;
/// HMAC-SHA256 output length -- the size of every signature this module produces or checks.
pub const SIGNATURE_LEN: usize = 32;

/// Domain-separation label for deriving the session's HMAC-SHA256 signing key from the raw
/// ECDH secret, via a second HMAC keyed by that same secret. Distinct from the (implicit,
/// unlabeled) derivation of the AES key -- see the module doc comment -- so the two keys can
/// never collide or be substituted for one another.
const HMAC_KEY_DERIVATION_LABEL: &[u8] = b"dx-security/hmac-key/v1";

/// HMAC-SHA256(raw_ecdh_secret, `HMAC_KEY_DERIVATION_LABEL`) -- a minimal, single-purpose
/// HKDF-Expand-equivalent: keying an HMAC by high-entropy secret material and MACing a fixed
/// label is a standard, well-understood way to derive an independent subkey without pulling
/// in a full `hkdf` crate for a single derivation. Any C#/TS broker implementation performing
/// the server side of this handshake MUST derive its own copy of the signing key with this
/// exact construction (same label bytes, same HMAC-SHA256) for signatures to verify across
/// the language boundary -- see `DemoAiChatBroker.cs`'s matching `HMACSHA256` derivation.
fn derive_signing_key(shared_secret: &[u8; KEY_LEN]) -> Zeroizing<[u8; KEY_LEN]> {
    let mut mac = <HmacSha256 as Mac>::new_from_slice(shared_secret).expect("HMAC-SHA256 accepts any key length");
    mac.update(HMAC_KEY_DERIVATION_LABEL);
    let signing_key: [u8; KEY_LEN] = mac.finalize().into_bytes().into();
    Zeroizing::new(signing_key)
}

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
    /// `decrypt` was called a second time for a session whose one-shot AES key
    /// was already consumed by an earlier successful (or attempted) decrypt.
    AlreadyDecrypted,
    /// `sign`/`verify_and_end`/`end_with_receipt` was called for a session with
    /// no signing key -- i.e. still `Pending`, or unknown entirely. Distinct
    /// from an *invalid* signature (which `verify_and_end` reports as `Ok(false)`,
    /// not an error: an attacker-controlled signature is expected, valid input).
    NoSigningKey,
}

/// A completed session's key material. `aes_key` is consumed (`Option::take`) by the first
/// call to [`SessionStore::decrypt`], structurally enforcing "one payload per handshake."
/// `hmac_key` deliberately outlives it -- see the module doc comment.
struct EstablishedKeys {
    aes_key: Option<Zeroizing<[u8; KEY_LEN]>>,
    hmac_key: Zeroizing<[u8; KEY_LEN]>,
}

enum SessionState {
    /// Waiting for the server's public key. Holds the 32-byte seed (not a
    /// derived scalar type) so it stays trivially `Zeroize`-able regardless of
    /// whether upstream elliptic-curve types implement zeroizing themselves.
    Pending(Zeroizing<[u8; 32]>),
    /// Keys have been derived from a completed ECDH exchange.
    Established(EstablishedKeys),
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

    /// Completes a pending session: runs ECDH against the server's public key, storing the
    /// raw shared secret as the session's AES-256-GCM key *and*, separately, an
    /// HMAC-SHA256 signing key derived from that same secret (see
    /// [`derive_signing_key`]). The pending seed is dropped (and zeroized) either way.
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
        let hmac_key = derive_signing_key(&key_bytes);

        self.sessions.insert(
            session_id.to_owned(),
            SessionState::Established(EstablishedKeys { aes_key: Some(Zeroizing::new(key_bytes)), hmac_key }),
        );
        Ok(())
    }

    /// Decrypts an AES-256-GCM payload (`ciphertext` includes the trailing 16-byte
    /// authentication tag, the standard AES-GCM convention) using the established key for
    /// `session_id`. Consumes the AES key on this call -- `Err(SessionError::AlreadyDecrypted)`
    /// on any subsequent call for the same session id, whether this one succeeded or not
    /// (a failed attempt must not leave the key available for a retry/probe). The session's
    /// signing key is untouched and remains usable by [`sign`]/[`verify_and_end`].
    ///
    /// [`sign`]: SessionStore::sign
    /// [`verify_and_end`]: SessionStore::verify_and_end
    pub fn decrypt(
        &mut self,
        session_id: &str,
        nonce: &[u8],
        ciphertext: &[u8],
    ) -> Result<Vec<u8>, SessionError> {
        let aes_key = match self.sessions.get_mut(session_id) {
            Some(SessionState::Established(keys)) => {
                keys.aes_key.take().ok_or(SessionError::AlreadyDecrypted)?
            }
            Some(SessionState::Pending(_)) | None => return Err(SessionError::UnknownSession),
        };

        if nonce.len() != NONCE_LEN {
            return Err(SessionError::InvalidNonceLength);
        }
        let nonce_array: [u8; NONCE_LEN] = nonce.try_into().expect("length checked above");

        let cipher = Aes256Gcm::new(&Key::<Aes256Gcm>::from(*aes_key));
        cipher
            .decrypt(&Nonce::from(nonce_array), ciphertext)
            .map_err(|_| SessionError::DecryptFailed)
    }

    /// Removes and zeroizes a session's state (pending or established) *without* generating
    /// a receipt -- for failure-path cleanup, where nothing was ever successfully mounted so
    /// there is nothing to prove was destroyed. A no-op if the session is unknown. Prefer
    /// [`verify_and_end`]/[`end_with_receipt`] for tearing down a session that *did* mount.
    ///
    /// [`verify_and_end`]: SessionStore::verify_and_end
    /// [`end_with_receipt`]: SessionStore::end_with_receipt
    pub fn end_session(&mut self, session_id: &str) {
        self.sessions.remove(session_id);
    }

    /// Computes an HMAC-SHA256 signature over `message` using `session_id`'s signing key.
    /// Works whether or not the AES key has been consumed -- the signing key outlives it.
    /// Does not modify or end the session. `Err(SessionError::NoSigningKey)` if the session
    /// id has no established signing key (still `Pending`, or unknown).
    pub fn sign(&self, session_id: &str, message: &[u8]) -> Result<[u8; SIGNATURE_LEN], SessionError> {
        let hmac_key = match self.sessions.get(session_id) {
            Some(SessionState::Established(keys)) => &keys.hmac_key,
            Some(SessionState::Pending(_)) | None => return Err(SessionError::NoSigningKey),
        };
        let mut mac = <HmacSha256 as Mac>::new_from_slice(hmac_key.as_slice()).expect("HMAC-SHA256 accepts any key length");
        mac.update(message);
        Ok(mac.finalize().into_bytes().into())
    }

    /// Verifies `signature` over `message` against `session_id`'s signing key (constant-time
    /// comparison, via `Hmac::verify_slice`) *without* ending the session -- for REFRESH,
    /// which (unlike WITHDRAW) does not tear the mount down. An invalid signature here still
    /// means an unauthenticated party injected a lifecycle event into a trusted channel; the
    /// caller (the TS bridge) is expected to treat that as tampering and tear the session down
    /// itself via [`end_with_receipt`], the same as any other detected tamper. `Err(SessionError::NoSigningKey)`
    /// if the session id has no established signing key to check against.
    ///
    /// [`end_with_receipt`]: SessionStore::end_with_receipt
    pub fn verify(&self, session_id: &str, message: &[u8], signature: &[u8]) -> Result<bool, SessionError> {
        let hmac_key = match self.sessions.get(session_id) {
            Some(SessionState::Established(keys)) => &keys.hmac_key,
            Some(SessionState::Pending(_)) | None => return Err(SessionError::NoSigningKey),
        };
        let mut mac =
            <HmacSha256 as Mac>::new_from_slice(hmac_key.as_slice()).expect("HMAC-SHA256 accepts any key length");
        mac.update(message);
        Ok(mac.verify_slice(signature).is_ok())
    }

    /// Verifies `signature` over `message` against `session_id`'s signing key (constant-time
    /// comparison, via `Hmac::verify_slice`), *also* signs `destruction_message` with that same
    /// key (both happen while the key still exists, in one atomic operation -- verifying and
    /// destruction-signing separately would mean the key is already gone by the time a
    /// destruction receipt tries to use it), then unconditionally ends the session -- valid or
    /// not. An invalid `signature` means an unauthenticated party injected a lifecycle event
    /// into what should be a trusted channel, which is itself grounds for the same defensive
    /// teardown a valid WITHDRAW would cause; the returned `valid` flag tells the caller (the
    /// TS bridge) which callback to fire (`onWithdraw` vs `onTamper`) and, in turn, which
    /// `trigger` value to report in the destruction receipt it sends using the returned
    /// signature -- not whether to tear down, which already happened either way.
    /// `Err(SessionError::NoSigningKey)` if the session id has no established signing key to
    /// check against (nothing to verify, sign, or end). Use [`verify`] instead for a signal
    /// (REFRESH) that must not terminate the session.
    ///
    /// [`verify`]: SessionStore::verify
    pub fn verify_and_end(
        &mut self,
        session_id: &str,
        message: &[u8],
        signature: &[u8],
        destruction_message: &[u8],
    ) -> Result<(bool, [u8; SIGNATURE_LEN]), SessionError> {
        let (valid, destruction_signature) = {
            let hmac_key = match self.sessions.get(session_id) {
                Some(SessionState::Established(keys)) => &keys.hmac_key,
                Some(SessionState::Pending(_)) | None => return Err(SessionError::NoSigningKey),
            };

            let mut verify_mac =
                <HmacSha256 as Mac>::new_from_slice(hmac_key.as_slice()).expect("HMAC-SHA256 accepts any key length");
            verify_mac.update(message);
            let valid = verify_mac.verify_slice(signature).is_ok();

            let mut receipt_mac =
                <HmacSha256 as Mac>::new_from_slice(hmac_key.as_slice()).expect("HMAC-SHA256 accepts any key length");
            receipt_mac.update(destruction_message);
            let destruction_signature: [u8; SIGNATURE_LEN] = receipt_mac.finalize().into_bytes().into();

            (valid, destruction_signature)
        };
        self.end_session(session_id);
        Ok((valid, destruction_signature))
    }

    /// Signs a destruction-receipt `message` using `session_id`'s signing key, then ends the
    /// session (removes and zeroizes all remaining key material). For client-initiated
    /// termination that still needs a signed Proof-of-Destruction receipt -- tamper detected
    /// locally, or the component unmounting -- as opposed to [`verify_and_end`], which
    /// already has an incoming signature to check for the WITHDRAW/REFRESH case.
    ///
    /// [`verify_and_end`]: SessionStore::verify_and_end
    pub fn end_with_receipt(
        &mut self,
        session_id: &str,
        message: &[u8],
    ) -> Result<[u8; SIGNATURE_LEN], SessionError> {
        let signature = self.sign(session_id, message)?;
        self.end_session(session_id);
        Ok(signature)
    }

    /// Whether a session currently has established (post-ECDH) keys, regardless of whether
    /// the AES key has already been consumed by `decrypt`. Test/diagnostic helper -- never
    /// exposes key material itself.
    #[cfg(test)]
    pub fn is_established(&self, session_id: &str) -> bool {
        matches!(self.sessions.get(session_id), Some(SessionState::Established(_)))
    }

    /// Whether a session's AES key specifically has NOT yet been consumed by `decrypt`.
    /// Test/diagnostic helper.
    #[cfg(test)]
    pub fn has_unused_aes_key(&self, session_id: &str) -> bool {
        matches!(
            self.sessions.get(session_id),
            Some(SessionState::Established(EstablishedKeys { aes_key: Some(_), .. }))
        )
    }

    // TEMPORARY DIAGNOSTIC (2026-07-19): tracking down a production-only ephemeral-chat
    // decrypt failure that doesn't reproduce locally. Returns SHA-256(aes_key) -- never the
    // key itself -- so the client-computed hash can be compared against a matching
    // SHA-256(sharedSecret) log added to DemoAiChatBroker.cs, to determine whether the two
    // sides are actually deriving the same shared secret. Peeks (does not consume) the key,
    // unlike `decrypt`. Remove this method, its FFI export in lib.rs, and both temporary log
    // lines once the root cause is found.
    pub fn debug_aes_key_sha256(&self, session_id: &str) -> Option<[u8; 32]> {
        use sha2::Digest;

        match self.sessions.get(session_id) {
            Some(SessionState::Established(EstablishedKeys { aes_key: Some(key), .. })) => {
                let mut hasher = Sha256::new();
                hasher.update(key.as_slice());
                Some(hasher.finalize().into())
            }
            _ => None,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use zeroize::Zeroize;

    // Arbitrary-but-fixed 32-byte seeds. Neither is the all-zero scalar and both
    // are far below the P-256 group order, so `SecretKey::from_slice` always
    // accepts them -- no RNG needed for deterministic, reproducible tests. This is
    // test-only fixture data (#[cfg(test)], never compiled into the shipped wasm
    // module) -- not real key material, so there is no production nonce/key reuse.
    const CLIENT_SEED: [u8; 32] = [ // codeql[rust/hard-coded-cryptographic-value] -- test-only fixture, see comment above
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25,
        26, 27, 28, 29, 30, 31, 32,
    ];
    const SERVER_SEED: [u8; 32] = [ // codeql[rust/hard-coded-cryptographic-value] -- test-only fixture, see comment above
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
        let result = store.begin_session("s1", &[0u8; 32]); // codeql[rust/hard-coded-cryptographic-value] -- deliberately-invalid all-zero seed asserting rejection; test-only fixture

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

        let result = store.complete_session("s1", &[0xFFu8; 10]); // codeql[rust/hard-coded-cryptographic-value] -- deliberately-malformed key bytes asserting rejection; test-only fixture

        assert_eq!(result, Err(SessionError::InvalidPublicKey));
    }

    #[test]
    fn full_handshake_then_decrypt_round_trips_a_message_encrypted_by_the_simulated_server() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");
        assert!(store.is_established("s1"));

        let nonce = [7u8; NONCE_LEN]; // codeql[rust/hard-coded-cryptographic-value] -- fixed nonce for a deterministic test vector; test-only, never shipped
        let plaintext = b"the conversation withdraws when the household link is severed";
        let ciphertext = server_encrypt(&nonce, plaintext);

        let decrypted = store.decrypt("s1", &nonce, &ciphertext).expect("decrypts");
        assert_eq!(decrypted, plaintext);
    }

    #[test]
    fn decrypt_before_a_session_is_established_is_rejected() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");

        let result = store.decrypt("s1", &[0u8; NONCE_LEN], b"anything"); // codeql[rust/hard-coded-cryptographic-value] -- fixed nonce, test-only fixture

        assert_eq!(result, Err(SessionError::UnknownSession));
    }

    #[test]
    fn decrypt_for_an_unknown_session_is_rejected() {
        let mut store = SessionStore::new();
        let result = store.decrypt("no-such-session", &[0u8; NONCE_LEN], b"anything"); // codeql[rust/hard-coded-cryptographic-value] -- fixed nonce, test-only fixture

        assert_eq!(result, Err(SessionError::UnknownSession));
    }

    #[test]
    fn decrypt_rejects_a_short_nonce() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");

        let result = store.decrypt("s1", &[0u8; 4], b"anything"); // codeql[rust/hard-coded-cryptographic-value] -- deliberately-short nonce asserting rejection; test-only fixture

        assert_eq!(result, Err(SessionError::InvalidNonceLength));
    }

    #[test]
    fn decrypt_rejects_a_tampered_ciphertext() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");

        let nonce = [7u8; NONCE_LEN]; // codeql[rust/hard-coded-cryptographic-value] -- fixed nonce for a deterministic test vector; test-only, never shipped
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
        let other_seed = [99u8; 32]; // codeql[rust/hard-coded-cryptographic-value] -- fixed seed for a deterministic test vector; test-only, never shipped
        store.begin_session("s2", &other_seed).expect("valid seed");
        store.complete_session("s2", &server_public_key()).expect("valid server key");

        let nonce = [7u8; NONCE_LEN]; // codeql[rust/hard-coded-cryptographic-value] -- fixed nonce for a deterministic test vector; test-only, never shipped
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
        let result = store.decrypt("s1", &[7u8; NONCE_LEN], b"anything"); // codeql[rust/hard-coded-cryptographic-value] -- fixed nonce, test-only fixture (session already ended)
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

    #[test]
    fn decrypt_consumes_the_aes_key_a_second_attempt_is_rejected_even_after_success() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");
        assert!(store.has_unused_aes_key("s1"));

        let nonce = [7u8; NONCE_LEN]; // codeql[rust/hard-coded-cryptographic-value] -- fixed nonce for a deterministic test vector; test-only, never shipped
        let ciphertext = server_encrypt(&nonce, b"one shot only");
        assert!(store.decrypt("s1", &nonce, &ciphertext).is_ok());
        assert!(!store.has_unused_aes_key("s1"), "the AES key must be consumed after one decrypt");

        let result = store.decrypt("s1", &nonce, &ciphertext);
        assert_eq!(result, Err(SessionError::AlreadyDecrypted));
        // The signing key is a *different* key from the AES key, and must survive the AES
        // key's consumption -- this is the entire point of splitting EstablishedKeys in two.
        assert!(store.is_established("s1"), "the session itself (and its signing key) must still exist");
    }

    #[test]
    fn decrypt_consumes_the_aes_key_even_on_a_failed_attempt() {
        // A failed decrypt (bad nonce length here) must not leave the key available for a
        // retry/probe -- consuming happens before the nonce-length check even runs.
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");

        let result = store.decrypt("s1", &[0u8; 4], b"anything"); // codeql[rust/hard-coded-cryptographic-value] -- deliberately-short nonce; test-only fixture
        assert_eq!(result, Err(SessionError::InvalidNonceLength));
        assert!(!store.has_unused_aes_key("s1"));

        let nonce = [7u8; NONCE_LEN]; // codeql[rust/hard-coded-cryptographic-value] -- fixed nonce; test-only fixture
        let ciphertext = server_encrypt(&nonce, b"too late");
        assert_eq!(store.decrypt("s1", &nonce, &ciphertext), Err(SessionError::AlreadyDecrypted));
    }

    #[test]
    fn sign_and_a_matching_verify_and_end_agree_and_the_second_call_reports_no_signing_key() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");

        let message = b"s1|WITHDRAW";
        let signature = store.sign("s1", message).expect("has a signing key");

        let destruction_message = b"s1|WITHDRAW_EVENT";
        let (valid, destruction_signature) = store
            .verify_and_end("s1", message, &signature, destruction_message)
            .expect("session existed");
        assert!(valid, "a signature produced by sign() must verify");
        assert_ne!(destruction_signature, [0u8; SIGNATURE_LEN], "a real destruction receipt signature was produced");
        assert!(!store.is_established("s1"), "verify_and_end always ends the session");

        // The session is gone now -- there is nothing left to sign or verify against.
        assert_eq!(store.sign("s1", message), Err(SessionError::NoSigningKey));
        assert_eq!(
            store.verify_and_end("s1", message, &signature, destruction_message),
            Err(SessionError::NoSigningKey),
        );
    }

    #[test]
    fn sign_works_after_the_aes_key_has_already_been_consumed_by_decrypt() {
        // Exercises the actual production sequence: decrypt (consumes the AES key), then
        // later sign an Access Receipt using the still-alive signing key.
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");

        let nonce = [7u8; NONCE_LEN]; // codeql[rust/hard-coded-cryptographic-value] -- fixed nonce; test-only fixture
        let ciphertext = server_encrypt(&nonce, b"access confirmed");
        store.decrypt("s1", &nonce, &ciphertext).expect("decrypts");
        assert!(!store.has_unused_aes_key("s1"));

        let receipt_signature = store.sign("s1", b"s1|ACCESS_CONFIRMED");
        assert!(receipt_signature.is_ok(), "signing must still work after the AES key is gone");
    }

    #[test]
    fn verify_and_end_rejects_a_forged_signature_but_still_tears_down_the_session() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");

        let forged = [0xAAu8; SIGNATURE_LEN]; // codeql[rust/hard-coded-cryptographic-value] -- deliberately-invalid forged signature; test-only fixture
        let (valid, destruction_signature) = store
            .verify_and_end("s1", b"s1|WITHDRAW", &forged, b"s1|TAMPER_DETECTED")
            .expect("session existed");

        assert!(!valid, "an attacker-forged signature must not verify");
        // Even a rejected WITHDRAW still produces a real destruction-receipt signature -- the
        // session is torn down defensively either way, and that teardown is still provable.
        assert_ne!(destruction_signature, [0u8; SIGNATURE_LEN]);
        // A forged control signal is itself grounds for defensive teardown, same as tampering.
        assert!(!store.is_established("s1"), "even a rejected signature still tears the session down");
    }

    #[test]
    fn verify_and_end_rejects_a_signature_for_the_wrong_message() {
        // Proves the signature is bound to the message content, not just the session -- an
        // attacker cannot replay a valid REFRESH signature to forge a WITHDRAW.
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");

        let refresh_signature = store.sign("s1", b"s1|REFRESH").expect("has a signing key");

        let (valid, _) = store
            .verify_and_end("s1", b"s1|WITHDRAW", &refresh_signature, b"s1|TAMPER_DETECTED")
            .expect("session existed");

        assert!(!valid, "a REFRESH signature must not verify as a WITHDRAW");
    }

    #[test]
    fn two_sessions_derive_different_signing_keys_even_against_the_same_server_key() {
        // Mirrors decrypt_rejects_a_key_from_the_wrong_session, for the signing key: proves
        // sessions do not share signing material any more than they share AES keys.
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");
        let other_seed = [99u8; 32]; // codeql[rust/hard-coded-cryptographic-value] -- fixed seed; test-only fixture
        store.begin_session("s2", &other_seed).expect("valid seed");
        store.complete_session("s2", &server_public_key()).expect("valid server key");

        let signature_from_s1 = store.sign("s1", b"shared-message").expect("has a signing key");

        let (valid_against_s2, _) = store
            .verify_and_end("s2", b"shared-message", &signature_from_s1, b"s2|TAMPER_DETECTED")
            .expect("s2 existed");
        assert!(!valid_against_s2, "s1's signature must not verify against s2's key");
    }

    #[test]
    fn end_with_receipt_signs_then_tears_down_and_a_second_call_reports_no_signing_key() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");

        let expected = store.sign("s1", b"s1|COMPONENT_UNMOUNT").expect("has a signing key");
        let receipt_signature =
            store.end_with_receipt("s1", b"s1|COMPONENT_UNMOUNT").expect("session existed");

        assert_eq!(receipt_signature, expected, "the receipt signature must match a plain sign() over the same message");
        assert!(!store.is_established("s1"));
        assert_eq!(
            store.end_with_receipt("s1", b"s1|COMPONENT_UNMOUNT"),
            Err(SessionError::NoSigningKey),
        );
    }

    #[test]
    fn verify_does_not_end_the_session_unlike_verify_and_end() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");
        store.complete_session("s1", &server_public_key()).expect("valid server key");

        let message = b"s1|REFRESH";
        let signature = store.sign("s1", message).expect("has a signing key");

        let valid = store.verify("s1", message, &signature).expect("session existed");
        assert!(valid);
        assert!(store.is_established("s1"), "verify (unlike verify_and_end) must not tear the session down");

        // A forged REFRESH is also just reported, not auto-torn-down -- the caller decides.
        let forged = [0x00u8; SIGNATURE_LEN]; // codeql[rust/hard-coded-cryptographic-value] -- deliberately-invalid forged signature; test-only fixture
        let forged_valid = store.verify("s1", message, &forged).expect("session existed");
        assert!(!forged_valid);
        assert!(store.is_established("s1"));
    }

    #[test]
    fn sign_and_verify_and_end_reject_a_pending_not_yet_established_session() {
        let mut store = SessionStore::new();
        store.begin_session("s1", &CLIENT_SEED).expect("valid seed");

        assert_eq!(store.sign("s1", b"anything"), Err(SessionError::NoSigningKey));
        assert_eq!(
            store.verify("s1", b"anything", &[0u8; SIGNATURE_LEN]),
            Err(SessionError::NoSigningKey),
        );
        assert_eq!(
            store.verify_and_end("s1", b"anything", &[0u8; SIGNATURE_LEN], b"anything-else"),
            Err(SessionError::NoSigningKey),
        );
        assert_eq!(store.end_with_receipt("s1", b"anything"), Err(SessionError::NoSigningKey));
    }
}
