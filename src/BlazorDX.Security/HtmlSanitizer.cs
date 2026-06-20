using System.Net;
using Microsoft.AspNetCore.Components;

namespace BlazorDX.Security;

/// <summary>
/// The single sanctioned place in BlazorDX to turn a string into a
/// <see cref="MarkupString"/>. Everywhere else, building <see cref="MarkupString"/>
/// from runtime data is a DX1001 analyzer error.
/// </summary>
/// <remarks>
/// The default policy is the safest possible one: HTML-encode the input so it
/// renders as inert text. Teams that genuinely need to render a subset of HTML
/// inject their own vetted sanitizer (e.g. a WAI-reviewed allow-list library)
/// through the constructor — BlazorDX deliberately does not ship a hand-rolled
/// HTML parser, because a weak sanitizer is more dangerous than none.
/// </remarks>
public sealed class HtmlSanitizer
{
    private readonly Func<string, string> sanitize;

    /// <summary>Creates a sanitizer that HTML-encodes all input (inert by default).</summary>
    public HtmlSanitizer()
        : this(WebUtility.HtmlEncode)
    {
    }

    /// <summary>Creates a sanitizer backed by a caller-supplied sanitization policy.</summary>
    /// <param name="policy">Maps raw input to safe HTML. Must be trusted to remove scripts, event handlers, etc.</param>
    public HtmlSanitizer(Func<string, string> policy)
    {
        sanitize = policy;
    }

    /// <summary>Sanitizes <paramref name="html"/> and returns it ready to render.</summary>
    public MarkupString Sanitize(string? html)
    {
        string safe = sanitize(html ?? string.Empty);

        // This is the one audited boundary where MarkupString is created from
        // non-constant data; the sanitization policy above is responsible for safety.
#pragma warning disable DX1001 // Sanctioned MarkupString boundary.
        return new MarkupString(safe);
#pragma warning restore DX1001
    }
}
