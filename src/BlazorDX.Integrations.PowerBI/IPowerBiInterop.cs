namespace BlazorDX.Integrations.PowerBI;

/// <summary>
/// Browser bridge to the hand-written Power BI ESM wrapper
/// (<c>_content/BlazorDX.Integrations.PowerBI/dx-powerbi.js</c>). The wrapper calls
/// the Power BI client SDK's <c>powerbi.embed(...)</c> with the embed token +
/// embedUrl the component passes across. Only functional under WebAssembly; off
/// browser (static SSR / Interactive Server prerender) the no-op
/// <see cref="NullPowerBiInterop"/> is used so the same component renders without a
/// DOM.
/// </summary>
/// <remarks>
/// The embed token and embed URL are <em>meant</em> for the browser — that is how
/// Power BI "app owns data" embedding works. The Azure AD token used to mint the
/// embed token stays server-side and never reaches this bridge.
/// </remarks>
public interface IPowerBiInterop
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>
    /// Embeds the report described by <paramref name="configJson"/> into the DOM
    /// element with id <paramref name="elementId"/>. The wrapper lazy-loads the real
    /// Power BI SDK from a CDN if a <c>window.powerbi</c> service is not already
    /// present, then calls <c>powerbi.embed(...)</c>.
    /// </summary>
    /// <param name="elementId">The id of the container the Power BI iframe renders into.</param>
    /// <param name="configJson">
    /// A small JSON object carrying <c>embedUrl</c>, <c>embedToken</c>, and
    /// <c>reportId</c> — all browser-bound by design.
    /// </param>
    ValueTask EmbedAsync(string elementId, string configJson);

    /// <summary>Tears down the embedded report in the given element (on dispose).</summary>
    ValueTask UnmountAsync(string elementId);
}
