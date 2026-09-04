using System.Globalization;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Sets <see cref="CultureInfo.CurrentCulture"/> and <see cref="CultureInfo.CurrentUICulture"/>
/// for the duration of a test, restoring both on dispose.
/// </summary>
/// <remarks>
/// <para>
/// Two different kinds of test need this, and they need it for opposite reasons.
/// </para>
/// <para>
/// <b>Pinning</b> (<see cref="Invariant"/>) makes an assertion about formatted output
/// deterministic. Components format user-visible dates and times through
/// <see cref="CultureInfo.CurrentCulture"/> — as they must, or a French user reads English
/// dates — so a test asserting <c>"Dec 28, 2026"</c> is really asserting something about the
/// machine it runs on until it fixes the culture.
/// </para>
/// <para>
/// <b>Switching</b> (<see cref="For"/>) is how a test proves culture-sensitive behaviour
/// actually works: render under <c>fr-FR</c> and assert the French output. Without this, a
/// component could ignore the culture entirely and every test would still pass.
/// </para>
/// <para>
/// Both cultures are set. <c>CurrentCulture</c> drives formatting (dates, numbers);
/// <c>CurrentUICulture</c> drives resource lookup. A test that changes one and not the other
/// gets a component whose dates and words disagree, which is a state no real user is ever in.
/// </para>
/// </remarks>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo culture;
    private readonly CultureInfo uiCulture;

    private CultureScope(CultureInfo replacement)
    {
        culture = CultureInfo.CurrentCulture;
        uiCulture = CultureInfo.CurrentUICulture;

        CultureInfo.CurrentCulture = replacement;
        CultureInfo.CurrentUICulture = replacement;
    }

    /// <summary>Fixes the culture so assertions on formatted output are machine-independent.</summary>
    public static CultureScope Invariant() => new(CultureInfo.InvariantCulture);

    /// <summary>Runs the test under a specific culture, e.g. <c>"fr-FR"</c>.</summary>
    public static CultureScope For(string name) => new(new CultureInfo(name));

    public void Dispose()
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = uiCulture;
    }
}
