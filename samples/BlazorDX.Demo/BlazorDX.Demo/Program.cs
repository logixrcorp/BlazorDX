using System.Text.Encodings.Web;
using BlazorDX.Compute;
using BlazorDX.Conduit;
using BlazorDX.Demo;
using BlazorDX.Demo.Client;
using BlazorDX.Demo.Components;
using BlazorDX.Integrations.PowerBI;
using BlazorDX.Integrations.Reporting;
using BlazorDX.MockReportServer;
using BlazorDX.Primitives.Forms;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Behind a reverse proxy / Cloudflare tunnel (TLS terminates at the edge, the origin is
// plain HTTP), honor X-Forwarded-Proto/For so the app sees the real https scheme and client
// IP — otherwise UseHttpsRedirection would bounce every request into a redirect loop. Gated
// by config so local `dotnet run` is unaffected; the container sets UseForwardedHeaders=true.
bool behindProxy = builder.Configuration.GetValue<bool>("UseForwardedHeaders");
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // The tunnel sidecar isn't on a loopback network, so don't restrict the proxy list.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

// Persist DataProtection keys (antiforgery tokens, auth cookies) to a fixed path outside the
// app's own publish tree, rather than the container default (an ephemeral in-memory/per-container
// key ring). Without this, every container restart or redeploy generates a fresh key ring, so any
// browser holding a cookie encrypted under the old keys gets a hard AntiforgeryValidationException
// on its next form submit. SetApplicationName pins the key-ring discriminator explicitly — without
// it, DataProtection derives one from the content root path, which can itself change between
// image builds and silently orphan a persisted key ring. The container Dockerfile documents that
// this path needs an operator-mounted persistent volume in production; local `dotnet run` just
// gets a stable folder under its own content root, which is already persistent across restarts.
string dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "dataprotection-keys");
builder.Services.AddDataProtection()
    .SetApplicationName("BlazorDX.Demo")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// IGridCompute for server-side prerender of the grid (resolves to managed C#).
builder.Services.AddBlazorDXCompute();

// Scoped toast service, also needed for server-side prerender of WASM pages.
builder.Services.AddScoped<BlazorDX.Components.ToastService>();

// TicketDesk demo data — Scoped, registered server-side too so /app pages prerender.
builder.Services.AddScoped<BlazorDX.Demo.Client.TicketDesk.TicketStore>();

// ContentVault (ECM) + Mailbox example-app data — also server-side so /ecm and /mail prerender.
builder.Services.AddScoped<BlazorDX.Demo.Client.Ecm.EcmStore>();
builder.Services.AddScoped<BlazorDX.Demo.Client.Mail.MailStore>();
builder.Services.AddScoped<BlazorDX.Demo.Client.Hr.HrStore>();

// Ephemeral AI Chat example app data — also server-side so /ai-chat prerenders.
builder.Services.AddScoped<BlazorDX.Demo.Client.AiChat.AiChatStore>();

// Same observability sink registered server-side so the /errors page prerenders.
builder.Services.AddScoped<BlazorDX.Demo.Client.DemoDiagnosticsLog>();
builder.Services.AddScoped<BlazorDX.Primitives.Diagnostics.IDxDiagnostics>(
    sp => sp.GetRequiredService<BlazorDX.Demo.Client.DemoDiagnosticsLog>());

// SSRS reporting (ADR 0010): wire the URL-Access render client + REST parameter
// source against the mock SSRS server mounted in-process below. The server-side
// HttpClient base address is absolute (loopback); the DxReportViewer's browser-
// facing iframe/export URLs are relative (/ReportServer), so they load same-origin
// regardless of host/scheme. Credentials would live here, server-side only — the
// mock is open, so none are set.
string reportServerBase =
    builder.Configuration["Reporting:ServerUrl"] ?? "http://localhost:5296/ReportServer";
string reportRestBase =
    builder.Configuration["Reporting:RestUrl"] ?? "http://localhost:5296/reports";
builder.Services.AddBlazorDXReporting(o =>
{
    o.ServerUrl = reportServerBase;
    o.RestUrl = reportRestBase;
});

// Power BI embedding (ADR 0010): the embed-token service mints a short-lived embed
// config server-side against the mock Power BI REST API mounted in-process below.
// The Azure AD token is held server-side by the StaticPowerBiTokenProvider (a real
// host swaps in MSAL); only the minted embed token + embedUrl ever cross to the
// browser via the /powerbi/embedconfig endpoint. ApiBaseUrl points at this same
// origin so the service's /v1.0/myorg/... REST calls hit the in-process mock.
const string PowerBiWorkspaceId = "ws-demo-0001";
string powerBiApiBase =
    builder.Configuration["PowerBI:ApiBaseUrl"] ?? "http://localhost:5296";
builder.Services.AddBlazorDXPowerBi(o =>
{
    o.ApiBaseUrl = powerBiApiBase;
    o.WorkspaceId = PowerBiWorkspaceId;
}).UseTokenProvider(new StaticPowerBiTokenProvider("demo-aad-token"));

// Used (Production / PowerBI:PlaygroundSample) to fetch the Power BI playground sample config.
builder.Services.AddHttpClient();

// The Conduit Router (BlazorDX.Conduit): tracks active ephemeral SSE connections so
// /ephemeral-events and /mcp-proxy below can register/push against the same set. Singleton —
// it must outlive any single request, since SSE connections and provider pushes come from
// unrelated requests.
builder.Services.AddSingleton<EphemeralSessionRegistry>();

// DemoAiChatBroker's session signing-key store (see DemoAiChatBroker.cs) — Singleton for the
// same reason as EphemeralSessionRegistry: a session's handshake and its later WITHDRAW/
// telemetry calls arrive on unrelated requests.
builder.Services.AddSingleton<BlazorDX.Demo.DemoAiChatSigningKeyRegistry>();

// This demo's IEphemeralSessionAuthorizer: gates a session id's SSE stream (and, per the
// interface's own contract, McpProxyEndpoint's payload intake, though nothing here exercises
// that) to the same browser that ran its handshake, via a visitor cookie -- see
// DemoAiChatBroker.cs for the full ownership model. Not registering this at all (as an earlier
// version of this demo did) means every session id is allowed; this is the fix for that.
builder.Services.AddDemoAiChatBroker();

var app = builder.Build();

// Must run before UseHttpsRedirection so the forwarded https scheme is applied first.
if (behindProxy)
{
    app.UseForwardedHeaders();
}

// Header-only security hardening applied to every response (incl. static assets):
// stop MIME sniffing, limit referrer leakage, and gate powerful browser features off.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
    await next();
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

// Mount the mock SSRS server in-process so the report viewer's render loop is real:
// the URL-Access render endpoint and a slice of the REST parameter API, both backed
// by the canned, deterministic catalog (/Sales/Monthly, /HR/Headcount).
var reportCatalog = new ReportCatalog();
app.MapMockReportServer("/ReportServer", reportCatalog);
app.MapMockReportsRestApi("/reports", reportCatalog);

// Mount the mock Power BI REST API in-process (under /v1.0/myorg, the documented
// prefix the embed service expects) so the embed-token flow is real: the service's
// GET report + POST GenerateToken calls hit this deterministic stand-in instead of
// a live Azure tenant. The returned embed token is a clearly-fake stand-in.
app.MapMockPowerBiApi("/v1.0/myorg");

// The embed-config endpoint DxPowerBiReport fetches from. It runs SERVER-SIDE: it
// calls IPowerBiEmbedService (which uses the AAD token, held server-side, against
// the mock) and returns ONLY the browser-bound config — embedUrl + embedToken +
// reportId. The AAD token never appears in this response. The default report id is
// one the mock recognises so the demo works zero-config.
const string DemoReportId = "11111111-1111-1111-1111-111111111111";
app.MapGet("/powerbi/embedconfig",
    async (HttpContext http, IPowerBiEmbedService embed, IConfiguration cfg, IHttpClientFactory httpFactory) =>
{
    // Live mode (Production): embed the Power BI playground's public sample report by fetching
    // its embed config (EmbedUrl + EmbedToken) from Microsoft's public playground backend.
    if (cfg.GetValue<bool>("PowerBI:PlaygroundSample"))
    {
        try
        {
            return Results.Json(await PlaygroundSample.FetchAsync(httpFactory, http.RequestAborted));
        }
        catch (Exception ex)
        {
            return Results.Json(
                new { error = "Could not load the Power BI playground sample report: " + ex.Message },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    // Default: the in-process mock (no tenant; deterministic for tests/axe).
    string reportId = http.Request.Query.TryGetValue("reportId", out var v) && v.Count > 0
        ? v[0]!
        : DemoReportId;
    try
    {
        PowerBiEmbedConfig config = await embed.CreateEmbedConfigAsync(reportId, http.RequestAborted);
        return Results.Json(new
        {
            embedUrl = config.EmbedUrl,
            embedToken = config.EmbedToken,
            reportId = config.ReportId,
            expiration = config.Expiration,
        });
    }
    catch (PowerBiEmbedException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BlazorDX.Demo.Client._Imports).Assembly);

// Endpoint backing the HTMX tier demo: returns an HTML fragment that htmx swaps
// into the page. Antiforgery is disabled here only to keep the demo self-contained.
app.MapPost("/htmx/echo", (HttpContext http) =>
{
    string message = http.Request.Form["message"].ToString();
    string safe = HtmlEncoder.Default.Encode(message);
    string fragment = string.IsNullOrWhiteSpace(safe)
        ? "<p class=\"dx-echo\">Type a message and submit — no circuit, no WASM.</p>"
        : $"<p class=\"dx-echo\">Server received: <strong>{safe}</strong></p>";
    return Results.Content(fragment, "text/html");
}).DisableAntiforgery();

// MCP over HTTP — the JSON request/response subset of the Streamable HTTP transport, which is
// enough for request/response tools (server-initiated SSE/sessions are a follow-up). A web
// agent POSTs a JSON-RPC message and gets the JSON-RPC response. The same McpToolServer that
// runs over stdio (samples/BlazorDX.McpServer) is the engine.
//
// PRODUCTION: protect this endpoint with `.RequireAuthorization()` and pass an
// `IAiToolAuthorizer` to the McpToolServer so tools are gated per caller. This demo exposes a
// single harmless echo tool anonymously, and audits every call to the diagnostics sink.
app.MapPost("/mcp", async (HttpContext http) =>
{
    using StreamReader reader = new(http.Request.Body);
    string body = await reader.ReadToEndAsync(http.RequestAborted);

    McpToolServer mcp = new McpToolServer
    {
        ServerName = "BlazorDX demo",
        Diagnostics = http.RequestServices.GetService<BlazorDX.Primitives.Diagnostics.IDxDiagnostics>(),
    }.Add(new FormAiTool<MeetingRequest>(
        new MeetingRequestFormModel(),
        () => new MeetingRequest(),
        (meeting, ct) => Task.FromResult($"Scheduled \"{meeting.Title}\" for {meeting.Attendees} attendee(s).")));

    string response = await mcp.HandleAsync(body, http.RequestAborted);
    return Results.Content(response, "application/json");
}).DisableAntiforgery();

// The Conduit Router (BlazorDX.Conduit): BlazorDX acting as an MCP CLIENT of an external MCP
// resource-provider server — the reverse role of the /mcp endpoint above (which is BlazorDX
// serving its own tools) and of samples/BlazorDX.McpServer (untouched by this feature). A blind
// router: it shuttles opaque, already-encrypted tokens between that external provider and the
// Blazor client over Server-Sent Events, and never reads plaintext or holds a decryption key.
//
// GET  /ephemeral-events/{sessionId} — the SSE stream a frontend's EventSource connects to.
// POST /mcp-proxy/{sessionId}        — where the external provider delivers an encrypted payload.
//
// PRODUCTION: register an IEphemeralSessionAuthorizer (gating a session id to its owning
// caller) and wire a real IConduitNotificationSource + McpBrokerClient hosted service against
// the actual provider (webhook relay or Azure Service Bus) so WITHDRAW/REFRESH notifications
// flow in too — this demo only mounts the two endpoints.
app.MapEphemeralEvents();
app.MapMcpProxy();

// DEMO-ONLY: the "/ai-chat" example app's stand-in for an external MCP resource-provider (see
// DemoAiChatBroker's own doc comment). Unlike the two mounts above, this is not part of
// BlazorDX.Conduit's real contract — it is this sample's own encryption broker, kept as a
// separate file specifically so nobody mistakes it for library code.
app.MapDemoAiChatBroker();

app.Run();

/// <summary>
/// Fetches the Power BI <em>playground</em> sample report's embed config (EmbedUrl + EmbedToken)
/// from Microsoft's public playground backend, shaped for <c>DxPowerBiReport</c>. Best-effort and
/// defensive: the backend is an unofficial public endpoint whose JSON shape may vary, so the
/// fields are read case-insensitively and any failure surfaces as a 502 (the component then shows
/// its accessible error state). Used only when <c>PowerBI:PlaygroundSample</c> is on.
/// </summary>
internal static class PlaygroundSample
{
    // The playground's own page (https://playground.powerbi.com/sampleReportEmbed) GETs this to mint
    // a short-lived embed token + URL for its public sample report, then calls powerbi.embed with
    // tokenType=Embed. We mirror it exactly. It is an unofficial endpoint and may change again (the
    // previous backend host was retired); any failure surfaces as a 502 and the component shows its
    // accessible error state.
    private const string SampleUrl =
        "https://wabi-us-east-a-primary-redirect.analysis.windows.net/playground/report/45ce895e-0693-46c7-b984-c849948414e1/GenerateToken";

    public static async Task<object> FetchAsync(IHttpClientFactory factory, CancellationToken ct)
    {
        System.Net.Http.HttpClient client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        using System.Net.Http.HttpResponseMessage resp = await client.GetAsync(SampleUrl, ct);
        resp.EnsureSuccessStatusCode();

        await using Stream stream = await resp.Content.ReadAsStreamAsync(ct);
        using System.Text.Json.JsonDocument doc = await System.Text.Json.JsonDocument.ParseAsync(stream, default, ct);
        System.Text.Json.JsonElement root = doc.RootElement;

        string embedUrl = Str(root, "embedUrl", "EmbedUrl")
            ?? throw new InvalidOperationException("the playground response had no embedUrl.");
        string embedToken = Token(root)
            ?? throw new InvalidOperationException("the playground response had no embed token.");
        string reportId = Str(root, "id", "Id", "reportId", "ReportId") ?? string.Empty;

        // The playground appends this flag to the embed URL for its demo presentation; match it.
        return new { embedUrl = embedUrl + "&embeddedDemo=true", embedToken, reportId };
    }

    private static string? Str(System.Text.Json.JsonElement el, params string[] names)
    {
        foreach (string name in names)
        {
            if (el.TryGetProperty(name, out System.Text.Json.JsonElement v)
                && v.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return v.GetString();
            }
        }

        return null;
    }

    // The embed token may be a bare string or an object { Token: "..." }.
    private static string? Token(System.Text.Json.JsonElement root)
    {
        foreach (string name in new[] { "token", "embedToken", "EmbedToken", "Token", "accessToken" })
        {
            if (!root.TryGetProperty(name, out System.Text.Json.JsonElement v))
            {
                continue;
            }

            if (v.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return v.GetString();
            }

            if (v.ValueKind == System.Text.Json.JsonValueKind.Object
                && v.TryGetProperty("Token", out System.Text.Json.JsonElement t)
                && t.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return t.GetString();
            }
        }

        return null;
    }
}
