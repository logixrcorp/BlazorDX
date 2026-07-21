When you drop `SecureEphemeralChat` into your own app, you're trusting a component whose entire
job is to leave no trace. That's not a claim you should just take on faith — [The Architecture of
Silence](/insights/articles/zero-trust-ephemeral-chat-conduit) and its accompanying
[whitepaper](/insights/whitepapers/human-right-to-forget) describe what the component is *supposed*
to do; this post is about how to check, yourself, that it actually does it.

Four techniques, roughly in order of effort: a DevTools check anyone can run in thirty seconds, a
real tamper test, a lifecycle hook you can log, and an automated Playwright test you can drop into
your own suite.

## 1. The DevTools test (DOM isolation)

This is the fastest check and it needs nothing from you but a browser.

- **Inspect the element.** Right-click where the message should be and Inspect. You'll see a
  `#shadow-root (closed)` node. DevTools will not let you expand it or read what's inside — that's
  what `mode: "closed"` means, and it's a real browser guarantee, not a component convention.
- **View source.** `Ctrl+U` (or `Cmd+Option+U`) on the server-rendered page. Search for your
  message text. It's not there — the server only ever sends the encrypted payload's coordinates
  (a session id, a nonce, a ciphertext), never plaintext.
- **Query for it.** Open the console and try `document.body.innerText`, or
  `document.querySelector(".dx-ephemeral-chat")?.shadowRoot`. The first won't contain the message;
  the second returns `null` for a closed root the caller didn't create.

If all three hold, the plaintext genuinely never touched the light DOM, the server-rendered HTML,
or any global DOM query — only the browser's own closed-shadow-root enforcement, which no
application code (this component's or otherwise) can bypass from outside.

## 2. The tamper test — and a correction

The obvious next thing to try is: while a message is mounted, open the console and mutate its
container — change a class, append a child, attach a listener — and watch the component notice and
tear itself down.

**It won't. And that's the point, not a bug.** The content lives inside that same closed shadow
root from step 1 — there is no node reachable from outside the page for `document.querySelector`
(or you, in the console) to reach in the first place. A `MutationObserver` watching for tampering
would be pointless if the tampering surface it's watching were reachable by anyone who wanted to
sidestep it entirely.

The tamper vector that *is* real is the one channel that legitimately reaches into a mounted
session from outside: the server-sent lifecycle events (`WITHDRAW`/`REFRESH`) the component listens
for. Every one of those is HMAC-signed with a key derived during the session's own ECDH handshake —
so forging one (right shape, wrong signature) is something you, or an attacker, actually *can* do
from outside the page, which makes it the one tamper path worth testing for real.

This repo's own `/ai-chat` demo does exactly that, through a small demo-only endpoint
(`DemoAiChatBroker.SimulateTamperRoute`) that pushes a real `WITHDRAW` event — through the same
`EphemeralSessionRegistry.PushEphemeralEventAsync` a production broker's revoke action would call —
with a deliberately wrong signature instead of a correct one:

```csharp
string data = JsonSerializer.Serialize(new
{
    action = "WITHDRAW",
    correlationId = sessionId,
    signature = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), // deliberately wrong
});

await registry.PushEphemeralEventAsync(sessionId, "security/lifecycle", data, cancellationToken);
```

On the client, `dx_security::session::verify_and_end` rejects the bad signature, and
`ephemeral-chat.ts` treats that rejection as tampering, not an ordinary failed withdraw: the shadow
root is destroyed, the wasm memory is zeroed, and `OnTamperDetected` fires — the same outcome an
actual forged signal from a compromised or malicious relay would produce. This is the mechanism
your own broker already has available: if you can push a legitimate `WITHDRAW`, you can push a
deliberately invalid one in a test, the same way.

## 3. Watching the state machine

`SecureEphemeralChat` exposes a `MountState` enum — `Decrypting` → `Mounted` → `Withdrawn` (or
`Decrypting` → `Failed`) — through a general-purpose `OnStateChanged` callback, alongside three
narrower, business-meaning ones you may already be using:

```razor
<SecureEphemeralChat SessionId="@sessionId"
                      UseLiveHandshake="true"
                      EstablishSession="EstablishSession"
                      OnStateChanged="s => Logger.LogInformation(\"ephemeral chat -> {State}\", s)"
                      OnWithdrawn="HandleWithdrawn"
                      OnTamperDetected="HandleTamperDetected" />
```

One deliberate gap: `OnStateChanged` reports `Failed` for *both* an ordinary decrypt failure and a
detected tamper attempt — it does not distinguish them, and neither does the status text a viewer
sees. That's intentional, not an oversight: an outside observer (including whatever's watching your
own logs, if those logs are ever exposed) should not be able to learn *why* a message failed to
render. If you specifically need to know a tamper attempt happened — to raise a security alert, say
— that's what the separate `OnTamperDetected` callback is for; it fires alongside the `Failed`
state transition, not instead of it.

## 4. An automated Playwright test

The pattern above is exactly what this repo's own end-to-end suite runs, for real, against the
`/ai-chat` demo — not a mocked fixture. Trimmed down to the shape you'd adapt for your own app:

```csharp
// 1. Drive a real handshake and wait for a real mount.
await page.FillAsync(ComposeSelector, prompt);
await page.ClickAsync(SendSelector);
await page.WaitForSelectorAsync(".dx-ephemeral-chat");
await page.WaitForFunctionAsync("() => !document.querySelector('.dx-ephemeral-chat-status')");

// 2. Push a forged lifecycle event -- from inside the page, so the request carries the
//    real session cookie your own ownership check expects.
int status = await page.EvaluateAsync<int>(
    "sessionId => fetch(`/your-broker/simulate-tamper/${sessionId}`, { method: 'POST' })" +
    ".then(r => r.status)",
    sessionId);
Assert.Equal(202, status);

// 3. Assert real teardown: the generic error status, and the node itself is gone.
await page.WaitForSelectorAsync(".dx-ephemeral-chat-status");
Assert.NotEmpty(await page.Locator(".dx-ephemeral-chat-error").AllAsync());
```

Two things worth asserting, and why: the *status text* proves the client rejected the forged event
in the first place; the *absence of the mounted node* proves the teardown was real, not just a UI
label change layered on top of still-live content.

Nothing in any of this touched the plaintext, the decryption key, or the wasm memory directly —
every assertion above is something *outside* the trust boundary the component draws, which is
exactly the point: if you can prove the guarantees hold from out here, you don't have to trust the
component's own account of itself.
