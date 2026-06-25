using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Integrations.PowerBI;

/// <summary>
/// An interactive Power BI report embed (ADR 0010 / 0014). It renders a labeled,
/// screen-reader-reachable container and, once interactive in the browser, embeds a
/// Power BI report into it through the hand-written <c>dx-powerbi.js</c> wrapper over
/// <c>[JSImport]</c> (never <c>IJSRuntime</c>).
/// <para>
/// <b>How the secret stays server-side.</b> The Azure AD token that authorizes the
/// Power BI REST API is held and used only on the server (by
/// <see cref="IPowerBiEmbedService"/>). The component never sees it. Instead it
/// obtains a <see cref="PowerBiEmbedConfig"/> — the embed URL plus a short-lived
/// embed <em>token</em> minted server-side — either:
/// </para>
/// <list type="bullet">
///   <item>by fetching it from a host endpoint (set <see cref="ConfigEndpoint"/>;
///   recommended, and the path the demo wires), so the browser only ever receives
///   the embed token + URL, which are meant for it; or</item>
///   <item>directly from <see cref="IPowerBiEmbedService"/> when an
///   <see cref="EmbedConfig"/> is not supplied and the component runs where the
///   service is resolvable (Interactive Server), or when the host passes a config in
///   via <see cref="EmbedConfig"/>.</item>
/// </list>
/// <para>
/// <b>Accessibility.</b> BlazorDX guarantees the labeled container
/// (<c>role="application"</c> + accessible name), and accessible loading/error
/// states (<c>aria-live</c>). The embedded report's own accessibility — keyboard
/// navigation and the "Show as a table" view — is provided by Microsoft inside the
/// iframe and is outside BlazorDX's control. See
/// <c>docs/powerbi-accessibility.md</c>.
/// </para>
/// </summary>
public sealed class DxPowerBiReport : ComponentBase, IAsyncDisposable
{
    private readonly string elementId = $"dx-powerbi-{Guid.NewGuid():N}";

    private PowerBiEmbedConfig? _resolved;
    private string? _error;
    private bool _embedded;
    private bool _loadFailed;

    /// <summary>The Power BI workspace (group) GUID. Level-1 zero-config, with <see cref="ReportId"/>.</summary>
    [Parameter] public string? WorkspaceId { get; set; }

    /// <summary>The report GUID to embed. Level-1 zero-config, with <see cref="WorkspaceId"/>.</summary>
    [Parameter] public string? ReportId { get; set; }

    /// <summary>
    /// An explicit, already-minted embed config. When set, the component embeds it
    /// directly and performs no fetch — the host took responsibility for minting it
    /// server-side.
    /// </summary>
    [Parameter] public PowerBiEmbedConfig? EmbedConfig { get; set; }

    /// <summary>
    /// A same-origin host endpoint the component GETs to obtain the embed config when
    /// no <see cref="EmbedConfig"/> is supplied. The endpoint mints the config
    /// server-side (calling <see cref="IPowerBiEmbedService"/>) and returns JSON with
    /// <c>embedUrl</c>, <c>embedToken</c>, and <c>reportId</c>. The Azure AD token
    /// never leaves the server. Recommended for WebAssembly hosting. Must be relative
    /// (same-origin); <see cref="ReportId"/> is appended as a query key.
    /// </summary>
    [Parameter] public string? ConfigEndpoint { get; set; }

    /// <summary>The accessible name for the embed container (WCAG 4.1.2). Required-ish; defaults to a generic label.</summary>
    [Parameter] public string Label { get; set; } = "Power BI report";

    /// <summary>Extra CSS classes appended to the component root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Level-3 slot: replaces the default loading indicator shown before the embed completes.</summary>
    [Parameter] public RenderFragment? Loading { get; set; }

    /// <summary>Level-3 slot: replaces the default error panel; receives the message.</summary>
    [Parameter] public RenderFragment<string>? Error { get; set; }

    /// <summary>Level-3 slot: optional toolbar rendered above the embed container.</summary>
    [Parameter] public RenderFragment? Toolbar { get; set; }

    // Resolved only where it is available (Interactive Server, or a host that
    // registered it WASM-side). Optional: the recommended path is the config
    // endpoint, where the browser never resolves the embed service at all.
    [Inject] private IServiceProvider Services { get; set; } = default!;

    [Inject] private IPowerBiInterop Interop { get; set; } = default!;

    // Source-gen JSON for the small config the endpoint returns — zero reflection.
    private async Task<PowerBiEmbedConfig?> FetchFromEndpointAsync(string endpoint)
    {
        var http = Services.GetService(typeof(HttpClient)) as HttpClient;
        if (http is null)
        {
            throw new PowerBiEmbedException(
                "No HttpClient is registered to fetch the Power BI embed config from " +
                $"'{endpoint}'. Register one, or supply EmbedConfig directly.");
        }

        string url = AppendReportId(endpoint, ReportId);
        try
        {
            using var response = await http.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new PowerBiEmbedException(
                    $"The Power BI config endpoint '{url}' returned {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var dto = await JsonSerializer.DeserializeAsync(
                stream, EmbedConfigJsonContext.Default.EmbedConfigDto).ConfigureAwait(false);
            if (dto is null || string.IsNullOrEmpty(dto.EmbedUrl) || string.IsNullOrEmpty(dto.EmbedToken))
            {
                throw new PowerBiEmbedException(
                    $"The Power BI config endpoint '{url}' returned an incomplete config.");
            }

            return new PowerBiEmbedConfig(
                EmbedUrl: dto.EmbedUrl,
                EmbedToken: dto.EmbedToken,
                ReportId: dto.ReportId ?? ReportId ?? string.Empty,
                TokenType: PowerBiTokenType.Embed,
                Expiration: dto.Expiration ?? DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException ex)
        {
            throw new PowerBiEmbedException(
                $"Could not reach the Power BI config endpoint '{url}'.", ex);
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        // An explicit config wins and needs no fetch.
        if (EmbedConfig is not null)
        {
            _resolved = EmbedConfig;
            _error = null;
            return;
        }

        // Already resolved on a prior parameter set — don't re-fetch every render.
        if (_resolved is not null)
        {
            return;
        }

        // Defer the embed-config fetch to the interactive phase. During static
        // prerender there is no browser HttpClient base address (a relative config
        // endpoint cannot resolve), and the embed cannot run anyway — so we render
        // only the loading state and fetch once the component is interactive. This
        // re-runs automatically when the render mode flips to interactive.
        if (!RendererInfo.IsInteractive)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(ConfigEndpoint))
            {
                _resolved = await FetchFromEndpointAsync(ConfigEndpoint).ConfigureAwait(false);
            }
            else if (!string.IsNullOrWhiteSpace(ReportId))
            {
                // No endpoint: try a directly-resolvable embed service (Interactive
                // Server). If absent (e.g. WebAssembly without an endpoint), surface a
                // clear error rather than crashing.
                var service = Services.GetService(typeof(IPowerBiEmbedService)) as IPowerBiEmbedService;
                if (service is null)
                {
                    throw new PowerBiEmbedException(
                        "DxPowerBiReport needs either an EmbedConfig, a ConfigEndpoint, " +
                        "or a resolvable IPowerBiEmbedService to obtain an embed token.");
                }

                _resolved = await service.CreateEmbedConfigAsync(ReportId!).ConfigureAwait(false);
            }

            _error = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any resolution failure (a PowerBiEmbedException, a transport error, or a
            // misconfigured client) surfaces in the accessible error region rather
            // than crashing the render.
            _resolved = null;
            _error = ex is PowerBiEmbedException
                ? ex.Message
                : "The Power BI embed configuration could not be loaded.";
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Embed once, after we have a config and the container exists in the DOM.
        // Off-browser the interop is a no-op, so this is harmless during prerender.
        if (_embedded || _loadFailed || _resolved is null || _error is not null)
        {
            return;
        }

        try
        {
            await Interop.EmbedAsync(elementId, SerializeConfig(_resolved)).ConfigureAwait(false);
            _embedded = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed embed (e.g. the SDK could not load) must not crash the circuit;
            // surface it in the accessible error region instead.
            _loadFailed = true;
            _error = "The Power BI report could not be embedded.";
            StateHasChanged();
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-powerbi {Class}".TrimEnd());

        if (Toolbar is not null)
        {
            builder.AddContent(2, Toolbar);
        }

        if (_error is not null)
        {
            BuildError(builder, _error);
        }
        else if (_resolved is null || !_embedded)
        {
            BuildLoading(builder);
        }

        // The embed container is always present so the JS wrapper has a stable target
        // and the SDK can render its iframe inside it. role="application" tells AT this
        // is an interactive widget with its own keyboard model (Power BI's).
        builder.OpenElement(20, "div");
        builder.AddAttribute(21, "id", elementId);
        builder.AddAttribute(22, "class", "dx-powerbi-frame");
        builder.AddAttribute(23, "role", "application");
        builder.AddAttribute(24, "aria-label", string.IsNullOrWhiteSpace(Label) ? "Power BI report" : Label);
        builder.CloseElement();

        builder.CloseElement(); // root
    }

    private void BuildLoading(RenderTreeBuilder builder)
    {
        builder.OpenElement(10, "div");
        builder.AddAttribute(11, "class", "dx-powerbi-loading");
        builder.AddAttribute(12, "role", "status");
        builder.AddAttribute(13, "aria-live", "polite");

        if (Loading is not null)
        {
            builder.AddContent(14, Loading);
        }
        else
        {
            builder.AddContent(15, "Loading the report…");
        }

        builder.CloseElement();
    }

    private void BuildError(RenderTreeBuilder builder, string message)
    {
        if (Error is not null)
        {
            builder.AddContent(16, Error(message));
            return;
        }

        builder.OpenElement(17, "p");
        builder.AddAttribute(18, "class", "dx-powerbi-error");
        builder.AddAttribute(19, "role", "alert");
        builder.AddContent(31, message);
        builder.CloseElement();
    }

    /// <summary>
    /// Serializes the browser-bound subset of the config to the JSON the wrapper
    /// expects. Only the embed token + URL + report id cross over — the embed token
    /// is meant for the client; no Azure AD token is present in the config at all.
    /// Built through the source-gen context (zero reflection).
    /// </summary>
    private static string SerializeConfig(PowerBiEmbedConfig config)
    {
        var dto = new EmbedConfigDto
        {
            EmbedUrl = config.EmbedUrl,
            EmbedToken = config.EmbedToken,
            ReportId = config.ReportId,
            Expiration = config.Expiration,
        };
        return JsonSerializer.Serialize(dto, EmbedConfigJsonContext.Default.EmbedConfigDto);
    }

    private static string AppendReportId(string endpoint, string? reportId)
    {
        if (string.IsNullOrWhiteSpace(reportId))
        {
            return endpoint;
        }

        char separator = endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return endpoint + separator + "reportId=" + Uri.EscapeDataString(reportId);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await Interop.UnmountAsync(elementId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Teardown is best-effort; a disposing circuit must not throw.
        }
    }
}

/// <summary>
/// The browser-bound embed config DTO: the small JSON shape both the host endpoint
/// returns and the <c>dx-powerbi.js</c> wrapper consumes. It deliberately carries no
/// Azure AD token — only the embed token (meant for the client), the embed URL, the
/// report id, and the token's expiration.
/// </summary>
public sealed class EmbedConfigDto
{
    /// <summary>The report's embed URL the SDK navigates the iframe to.</summary>
    [JsonPropertyName("embedUrl")]
    public string? EmbedUrl { get; set; }

    /// <summary>The short-lived embed token (browser-bound by design).</summary>
    [JsonPropertyName("embedToken")]
    public string? EmbedToken { get; set; }

    /// <summary>The report this config is for.</summary>
    [JsonPropertyName("reportId")]
    public string? ReportId { get; set; }

    /// <summary>When the embed token expires (UTC).</summary>
    [JsonPropertyName("expiration")]
    public DateTimeOffset? Expiration { get; set; }
}

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for <see cref="EmbedConfigDto"/>
/// (ADR 0002: zero reflection, trim/AOT-clean).
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(EmbedConfigDto))]
public sealed partial class EmbedConfigJsonContext : JsonSerializerContext;
