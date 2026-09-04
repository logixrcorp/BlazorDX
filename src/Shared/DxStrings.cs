using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace BlazorDX;

// Shared source, not a shared package. This file is linked into BlazorDX.Components,
// .Primitives, .Htmx, .Integrations.PowerBI and .Integrations.Reporting, so each assembly
// compiles its own internal copy from one definition.
//
// A thirteenth published package was the obvious alternative and was rejected:
// BlazorDX.Integrations.PowerBI deliberately references no BlazorDX project at all, and the
// other integration packages reference only what they need. Making every package take a
// dependency to reach a forty-line helper would trade that independence for nothing — the type
// is internal, so there is no public surface to share in the first place.
//
// Namespace BlazorDX rather than BlazorDX.Components, so every consuming namespace resolves it
// without a using: they are all BlazorDX.*.

/// <summary>
/// Per-component localized-string lookup where localization is <b>optional</b>: every call site
/// supplies the English text, and that text is what renders when no <see cref="IStringLocalizer{T}"/>
/// is registered. A consumer who never localizes needs no <c>AddLocalization()</c> call and sees
/// no behavior change — which is the whole point.
/// </summary>
/// <remarks>
/// <para>
/// The alternative — <c>[Inject] IStringLocalizer&lt;T&gt;</c> directly, as ADR 0016's two pilots
/// originally did — resolves <b>unconditionally at component activation</b>, so a consumer who
/// hasn't called <c>AddLocalization()</c> gets an <see cref="InvalidOperationException"/> rather
/// than English. At two components that was an obscure edge; across the ~83 components carrying
/// user-facing text it would silently become a mandatory registration for the entire library.
/// See docs/adr/0021-optional-localization-and-rollout-guardrails.md and docs/localization.md.
/// </para>
/// <para>
/// The fallback also covers a <i>registered but incomplete</i> localizer:
/// <see cref="LocalizedString.ResourceNotFound"/> means the key had no resource entry, in which
/// case <see cref="IStringLocalizer"/> returns the raw <i>key</i> as its value. Rendering
/// "SelectAllRows" to a user is worse than rendering "Select all rows", so a not-found lookup
/// falls back too — this is the same class of bug ADR 0016 hit during Phase 0, where a broken
/// lookup returning the key was indistinguishable from a correct one.
/// </para>
/// <para>
/// Resolution is deferred and cached: the service lookup happens on first use, not at activation,
/// so a component that never renders a localized string never touches DI at all.
/// </para>
/// </remarks>
/// <typeparam name="T">
/// The type naming the resource file — normally the component itself (<c>DxAlert</c> →
/// <c>DxAlert.resx</c>). A generic component must use a non-generic marker type instead (see
/// <c>DxDataGridResources</c>): the default factory derives the resource name from the
/// <i>closed</i> generic type, so <c>DxDataGrid&lt;Person&gt;</c> and <c>DxDataGrid&lt;Order&gt;</c>
/// would each look for a differently-named resource.
/// </typeparam>
internal sealed class DxStrings<T>(IServiceProvider services)
{
    private IStringLocalizer<T>? localizer;
    private bool resolved;

    private IStringLocalizer<T>? Localizer
    {
        get
        {
            if (!resolved)
            {
                // GetService, not GetRequiredService: absent registration is the supported
                // "this consumer doesn't localize" case, not a configuration error.
                localizer = services.GetService<IStringLocalizer<T>>();
                resolved = true;
            }

            return localizer;
        }
    }

    /// <summary>
    /// The localized text for <paramref name="key"/>, falling back to <paramref name="english"/>
    /// when no localizer is registered or the key has no resource entry.
    /// </summary>
    public string this[string key, string english]
    {
        get
        {
            if (Localizer is not { } l)
            {
                return english;
            }

            LocalizedString localized = l[key];
            return localized.ResourceNotFound ? english : localized.Value;
        }
    }

    /// <summary>
    /// Composite-formatting overload: <paramref name="english"/> is a composite format string
    /// (<c>"{0} of {1} columns"</c>) used both as the fallback and — deliberately — with the same
    /// argument order the resource entry must use, so a translator sees the same placeholders.
    /// </summary>
    public string this[string key, string english, params object[] args]
    {
        get
        {
            if (Localizer is not { } l)
            {
                return string.Format(CultureInfo.CurrentCulture, english, args);
            }

            LocalizedString localized = l[key, args];
            return localized.ResourceNotFound
                ? string.Format(CultureInfo.CurrentCulture, english, args)
                : localized.Value;
        }
    }
}
