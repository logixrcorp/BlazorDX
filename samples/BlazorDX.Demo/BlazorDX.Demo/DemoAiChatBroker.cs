using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorDX.Conduit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BlazorDX.Demo;

/// <summary>
/// DEMO-ONLY broker for the "/ai-chat" example app's ephemeral AI chat messages.
///
/// This is <b>not</b> part of <c>BlazorDX.Conduit</c>'s real contract. The Conduit itself never
/// encrypts anything — it is a blind, zero-knowledge router (see
/// <c>docs/adr/0016-zero-trust-ephemeral-chat-conduit.md</c> and
/// <c>McpProxyEndpoint</c>'s own doc comment: "this server never parses, inspects, or decrypts
/// it"). In a real deployment, the encrypted handshake response that
/// <c>SecureEphemeralChat</c>'s <c>EstablishSession</c> delegate receives comes from an actual
/// external MCP resource-provider server holding its own keypair, reached over whatever
/// transport ADR 0016 documents. This class stands in for that external provider purely so the
/// "/ai-chat" demo page can show a full, real client-side handshake — real P-256 ECDH, real
/// AES-256-GCM, a real closed Shadow DOM mount, real signed WITHDRAW/Access/Destruction
/// receipts — without requiring any external API key or a second running process. Do not
/// mistake this for production wiring: a real deployment also registers an
/// <see cref="IEphemeralSessionAuthorizer"/> before exposing anything like this beyond a local
/// demo (this endpoint, like <c>/mcp</c> and <c>/mcp-proxy</c> elsewhere in this sample, is
/// anonymous).
///
/// <see cref="System.Security.Cryptography"/>'s <c>ECDiffieHellman</c>/<c>AesGcm</c> types need a
/// native crypto backend that browser-wasm does not provide — exactly why the feature has its
/// own Rust/wasm crypto core client-side (<c>dx_security</c>). This broker's encryption logic
/// therefore must run here, server-side, and never in <c>BlazorDX.Demo.Client</c>.
/// </summary>
public static class DemoAiChatBroker
{
    /// <summary>
    /// <c>POST /demo/ai-chat/handshake</c> — the client's freshly wasm-generated P-256 public
    /// key comes in, an encrypted response encrypted for the shared secret with a fresh server
    /// keypair goes out. Request/response shapes are demo-local (<see cref="HandshakeRequest"/>
    /// / <see cref="HandshakeResponse"/>), not a BlazorDX.Conduit contract. Also derives and
    /// stores this session's HMAC signing key (see <see cref="DemoAiChatSigningKeyRegistry"/>) —
    /// the exact same domain-separated derivation <c>dx_security::session::derive_signing_key</c>
    /// runs client-side against its own copy of the shared secret, so a signature produced on
    /// either side verifies on the other.
    /// </summary>
    public const string HandshakeRoute = "/demo/ai-chat/handshake";

    /// <summary>
    /// <c>POST /demo/ai-chat/withdraw/{sessionId}</c> — signs a WITHDRAW control message with
    /// this session's stored HMAC key and pushes it as a real "WITHDRAW" SSE event via
    /// <see cref="EphemeralEventsEndpoint.PushEphemeralEventAsync"/>, exactly as a real external
    /// provider revoking a message would. Nothing about this push is simulated; only the fact
    /// that it is *this* demo page triggering it (rather than an external provider's own revoke
    /// action) is demo-specific.
    /// </summary>
    public const string WithdrawRoute = "/demo/ai-chat/withdraw/{sessionId}";

    /// <summary><c>POST /demo/ai-chat/telemetry/access</c> — Access Receipt intake (Proof of Access half of the PoD protocol).</summary>
    public const string TelemetryAccessRoute = "/demo/ai-chat/telemetry/access";

    /// <summary><c>POST /demo/ai-chat/telemetry/destruction</c> — Destruction Receipt intake (Proof of Destruction half).</summary>
    public const string TelemetryDestructionRoute = "/demo/ai-chat/telemetry/destruction";

    /// <summary><c>GET /demo/ai-chat/telemetry/audit</c> — read-only view of every receipt this broker has verified, newest first. Demo/visibility only.</summary>
    public const string TelemetryAuditRoute = "/demo/ai-chat/telemetry/audit";

    // Domain-separation label mirroring dx_security::session::HMAC_KEY_DERIVATION_LABEL
    // byte-for-byte. Any implementation on either side of the ECDH handshake that changes this
    // label independently will produce non-interoperable signing keys.
    private const string HmacKeyDerivationLabel = "dx-security/hmac-key/v1";

    private const int P256PublicKeyLength = 65; // 0x04 || X(32) || Y(32)
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int MaxAuditEntries = 200;

    public static IEndpointRouteBuilder MapDemoAiChatBroker(this WebApplication app)
    {
        app.MapPost(HandshakeRoute, HandleHandshakeAsync).DisableAntiforgery();
        app.MapPost(WithdrawRoute, HandleWithdrawAsync).DisableAntiforgery();
        app.MapPost(TelemetryAccessRoute, HandleAccessTelemetryAsync).DisableAntiforgery();
        app.MapPost(TelemetryDestructionRoute, HandleDestructionTelemetryAsync).DisableAntiforgery();
        app.MapGet(TelemetryAuditRoute, HandleTelemetryAuditAsync);
        return app;
    }

    private static Task<IResult> HandleHandshakeAsync(
        HandshakeRequest request, DemoAiChatSigningKeyRegistry signingKeys, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.ClientPublicKeyBase64))
        {
            return Task.FromResult(Results.BadRequest(new { error = "sessionId and clientPublicKeyBase64 are required." }));
        }

        byte[] clientKeyBytes;
        try
        {
            clientKeyBytes = Convert.FromBase64String(request.ClientPublicKeyBase64);
        }
        catch (FormatException)
        {
            return Task.FromResult(Results.BadRequest(new { error = "clientPublicKeyBase64 was not valid base64." }));
        }

        if (clientKeyBytes.Length != P256PublicKeyLength || clientKeyBytes[0] != 0x04)
        {
            return Task.FromResult(Results.BadRequest(
                new { error = "clientPublicKeyBase64 was not an uncompressed P-256 SEC1 key (0x04 || X(32) || Y(32))." }));
        }

        string replyText = string.IsNullOrEmpty(request.ReplyText)
            ? "(the demo did not supply a reply — nothing to say here.)"
            : request.ReplyText;

        // Import the client's REAL public key (sent to us over HTTP, generated by real
        // crypto.getRandomValues entropy inside the browser's wasm module) as a public-key-only
        // handle — no private scalar involved on this side. Skip the leading 0x04 tag byte.
        ECParameters clientParameters = new()
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = clientKeyBytes[1..33],
                Y = clientKeyBytes[33..65],
            },
        };

        using ECDiffieHellman clientPublicKey = ECDiffieHellman.Create(clientParameters);
        // A fresh server keypair, real randomness this time (no fixed seed — that trick is
        // EphemeralChatFixture-only, for a deterministic Playwright vector; this broker accepts
        // whatever real public key a real browser handshake sends it).
        using ECDiffieHellman server = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceLength);
        byte[] ciphertextWithTag;
        byte[] sharedSecret = server.DeriveRawSecretAgreement(clientPublicKey.PublicKey);
        try
        {
            byte[] plaintext = Encoding.UTF8.GetBytes(replyText);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagLength];
            using (AesGcm gcm = new(sharedSecret, TagLength))
            {
                gcm.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            // Trailing 16-byte tag concatenated onto the ciphertext, matching the convention
            // BlazorDX.Security.Rust's decrypt_payload (and the aes-gcm crate) expects.
            ciphertextWithTag = [.. ciphertext, .. tag];

            // Same raw shared secret, a *second*, differently-labeled HMAC derivation -- the
            // session's signing key, kept independently of (and outliving, on the client side)
            // the AES key above. Stored here so this broker can later sign a WITHDRAW push and
            // verify the Access/Destruction receipts the client sends back.
            signingKeys.Store(request.SessionId, DeriveSigningKey(sharedSecret));
        }
        finally
        {
            // Cheap, cost-free hygiene consistent with the feature's own zero-trust framing —
            // .NET gives no hard guarantee against a GC copy having already happened, unlike the
            // Rust side's `zeroize`, but there is no reason not to clear what we can.
            CryptographicOperations.ZeroMemory(sharedSecret);
        }

        ECParameters serverExported = server.ExportParameters(includePrivateParameters: false);
        byte[] serverPublicKeyBytes = [0x04, .. serverExported.Q.X!, .. serverExported.Q.Y!];

        var response = new HandshakeResponse
        {
            ServerPublicKeyBase64 = Convert.ToBase64String(serverPublicKeyBytes),
            NonceBase64 = Convert.ToBase64String(nonce),
            CiphertextBase64 = Convert.ToBase64String(ciphertextWithTag),
        };
        return Task.FromResult(Results.Json(response));
    }

    private static async Task<IResult> HandleWithdrawAsync(
        string sessionId,
        EphemeralSessionRegistry registry,
        DemoAiChatSigningKeyRegistry signingKeys,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Results.BadRequest(new { error = "sessionId is required." });
        }

        if (!signingKeys.TryGet(sessionId, out byte[]? hmacKey))
        {
            // No handshake ever happened for this session (or it was already fully retired by a
            // prior destruction receipt) -- nothing to sign, and the client-side wasm module
            // would reject an unsigned/mismatched WITHDRAW as tampering anyway.
            return Results.NotFound(new { error = $"no signing key on file for session '{sessionId}'." });
        }

        byte[] signature = Sign(hmacKey, $"{sessionId}|WITHDRAW");
        string data = JsonSerializer.Serialize(new { signature = Convert.ToBase64String(signature) });

        // Real push through the real Conduit registry — the same method a production
        // McpBrokerClient/webhook relay would call. A session nobody is currently watching is a
        // silent no-op (the Conduit is an ephemeral relay, not a durable queue).
        await registry.PushEphemeralEventAsync(sessionId, "WITHDRAW", data, cancellationToken).ConfigureAwait(false);
        return Results.Accepted();
    }

    private static Task<IResult> HandleAccessTelemetryAsync(
        TelemetryReceipt receipt, DemoAiChatSigningKeyRegistry signingKeys)
    {
        if (!signingKeys.TryGet(receipt.SessionId, out byte[]? hmacKey))
        {
            return Task.FromResult(Results.NotFound(new { error = $"no signing key on file for session '{receipt.SessionId}'." }));
        }

        bool valid = VerifySignature(hmacKey, $"{receipt.SessionId}|ACCESS_CONFIRMED", receipt.ClientSignature);
        DemoTelemetryAudit.Record(receipt.SessionId, "ACCESS_CONFIRMED", trigger: null, valid);

        // A failed verification is itself an audit-worthy signal (see the receipt's own
        // Compliance-as-Telemetry framing in docs/adr/0016) -- but it never becomes an
        // exception or a 500; the client already made its own trust decisions before this
        // receipt was ever sent, and a broker is exactly as "blind" to *why* a signature failed
        // as the Conduit Router is to plaintext.
        return Task.FromResult(valid ? Results.Accepted() : Results.BadRequest(new { error = "signature did not verify." }));
    }

    private static Task<IResult> HandleDestructionTelemetryAsync(
        TelemetryReceipt receipt, DemoAiChatSigningKeyRegistry signingKeys)
    {
        if (!signingKeys.TryGet(receipt.SessionId, out byte[]? hmacKey))
        {
            return Task.FromResult(Results.NotFound(new { error = $"no signing key on file for session '{receipt.SessionId}'." }));
        }

        string trigger = string.IsNullOrEmpty(receipt.Trigger) ? "UNSPECIFIED" : receipt.Trigger;
        bool valid = VerifySignature(hmacKey, $"{receipt.SessionId}|{trigger}", receipt.ClientSignature);
        DemoTelemetryAudit.Record(receipt.SessionId, "MEMORY_ZEROED", trigger, valid);

        if (valid)
        {
            // The session's signing material is now retired on the broker side too -- mirrors
            // the client-side dx_security store, where end_with_receipt/verify_and_end already
            // removed and zeroized it before this receipt was ever sent.
            signingKeys.Remove(receipt.SessionId);
        }

        return Task.FromResult(valid ? Results.Accepted() : Results.BadRequest(new { error = "signature did not verify." }));
    }

    private static Task<IResult> HandleTelemetryAuditAsync() =>
        Task.FromResult(Results.Json(DemoTelemetryAudit.Recent(MaxAuditEntries)));

    /// <summary>
    /// HMAC-SHA256(sharedSecret, <see cref="HmacKeyDerivationLabel"/>) -- byte-for-byte the same
    /// construction as <c>dx_security::session::derive_signing_key</c>, so a signature produced
    /// on either side of the ECDH handshake verifies on the other.
    /// </summary>
    private static byte[] DeriveSigningKey(byte[] sharedSecret)
    {
        using HMACSHA256 hmac = new(sharedSecret);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(HmacKeyDerivationLabel));
    }

    private static byte[] Sign(byte[] hmacKey, string message)
    {
        using HMACSHA256 hmac = new(hmacKey);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
    }

    private static bool VerifySignature(byte[] hmacKey, string message, string? clientSignatureBase64)
    {
        if (string.IsNullOrEmpty(clientSignatureBase64))
        {
            return false;
        }

        byte[] claimed;
        try
        {
            claimed = Convert.FromBase64String(clientSignatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] expected = Sign(hmacKey, message);
        return CryptographicOperations.FixedTimeEquals(claimed, expected);
    }
}

/// <summary>
/// Demo-only in-memory store of each session's HMAC signing key, keyed by session id.
/// Registered <c>AddSingleton</c> (must outlive any single request, like
/// <see cref="EphemeralSessionRegistry"/>) — a real broker would back this with whatever
/// short-lived session store it already keeps its own shared-secret material in (see
/// <c>samples/BlazorDX.MockSecureBroker/src/SessionCache.ts</c> for the reference external
/// broker's equivalent).
/// </summary>
public sealed class DemoAiChatSigningKeyRegistry
{
    private readonly ConcurrentDictionary<string, byte[]> keysBySessionId = new(StringComparer.Ordinal);

    public void Store(string sessionId, byte[] hmacKey) => keysBySessionId[sessionId] = hmacKey;

    public bool TryGet(string sessionId, [NotNullWhen(true)] out byte[]? hmacKey) =>
        keysBySessionId.TryGetValue(sessionId, out hmacKey);

    public void Remove(string sessionId) => keysBySessionId.TryRemove(sessionId, out _);
}

/// <summary>
/// Demo-only, in-memory, bounded audit trail of every telemetry receipt this broker has
/// checked (valid or not) -- a minimal stand-in for the "immutable, write-once audit vault"
/// concept, purely so the "/ai-chat" demo can show the Compliance-as-Telemetry loop actually
/// closing. Not persisted, not thread-safe beyond the concurrent queue's own guarantees, and
/// intentionally capped so a long-running demo process cannot leak memory.
/// </summary>
internal static class DemoTelemetryAudit
{
    private static readonly ConcurrentQueue<TelemetryAuditEntry> entries = new();
    private static int count;

    public static void Record(string sessionId, string eventType, string? trigger, bool valid)
    {
        entries.Enqueue(new TelemetryAuditEntry(sessionId, eventType, trigger, valid, DateTimeOffset.UtcNow));
        if (Interlocked.Increment(ref count) > 200)
        {
            entries.TryDequeue(out _);
            Interlocked.Decrement(ref count);
        }
    }

    public static IReadOnlyList<TelemetryAuditEntry> Recent(int max) =>
        [.. entries.Reverse().Take(max)];
}

internal sealed record TelemetryAuditEntry(string SessionId, string EventType, string? Trigger, bool Valid, DateTimeOffset RecordedAt);

/// <summary>Request body for <see cref="DemoAiChatBroker.HandshakeRoute"/>. Demo-local shape, not a BlazorDX.Conduit contract.</summary>
internal sealed class HandshakeRequest
{
    public string SessionId { get; set; } = string.Empty;

    public string ClientPublicKeyBase64 { get; set; } = string.Empty;

    /// <summary>The plaintext the caller wants this handshake's ciphertext to decrypt to — the demo's canned "AI reply".</summary>
    public string ReplyText { get; set; } = string.Empty;
}

/// <summary>Response body for <see cref="DemoAiChatBroker.HandshakeRoute"/>, shaped to match <c>BlazorDX.Components.EphemeralHandshakeResult</c> (camelCase JSON, via ASP.NET Core's default naming policy).</summary>
internal sealed class HandshakeResponse
{
    public string ServerPublicKeyBase64 { get; set; } = string.Empty;

    public string NonceBase64 { get; set; } = string.Empty;

    public string CiphertextBase64 { get; set; } = string.Empty;
}

/// <summary>
/// Request body shared by <see cref="DemoAiChatBroker.TelemetryAccessRoute"/> and
/// <see cref="DemoAiChatBroker.TelemetryDestructionRoute"/> — matches the JSON shape
/// <c>ephemeral-chat.ts</c>'s <c>postTelemetryReceipt</c> sends (camelCase via ASP.NET Core's
/// default naming policy). <see cref="Trigger"/> is present only on a destruction receipt.
/// </summary>
internal sealed class TelemetryReceipt
{
    public string SessionId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Timestamp { get; set; } = string.Empty;

    public string? Trigger { get; set; }

    public string ClientSignature { get; set; } = string.Empty;
}
