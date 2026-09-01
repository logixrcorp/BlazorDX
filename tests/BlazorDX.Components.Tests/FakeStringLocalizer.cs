using Microsoft.Extensions.Localization;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Test double for <see cref="IStringLocalizer{T}"/> (ADR 0016's localization spike).
/// Returns a caller-supplied sentinel for every key instead of a real translation, so a
/// test asserting the sentinel appears in the DOM proves the component's string is
/// actually wired through the localizer -- unlike asserting the real English string,
/// which would pass identically whether the component still had it hardcoded.
/// </summary>
internal sealed class FakeStringLocalizer<T> : IStringLocalizer<T>
{
    private readonly Func<string, string> map;

    /// <param name="map">Given a resource key, returns the string to substitute. Defaults to
    /// prefixing the key with "§§" and uppercasing it (e.g. "Dismiss" -> "§§DISMISS§§") when
    /// no custom mapping is supplied.</param>
    public FakeStringLocalizer(Func<string, string>? map = null) =>
        this.map = map ?? (key => $"§§{key.ToUpperInvariant()}§§");

    public LocalizedString this[string name] => new(name, map(name));

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, string.Format(map(name), arguments));

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        throw new NotSupportedException("Not needed by any test using this fake.");
}
