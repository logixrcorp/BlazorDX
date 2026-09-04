namespace BlazorDX.Components;

/// <summary>
/// The resource-name anchor for every chart's user-facing text —
/// <c>DxChartResources.resx</c> holds the strings for all of them.
/// </summary>
/// <remarks>
/// <para>
/// Charts are the one place in the rollout where a shared resource file is clearly right. There
/// are fifteen of them carrying user-facing text, almost all of it a single accessible summary of
/// the same shape ("Pie chart with {0} slices", "Funnel chart with {0} stages"), plus a zoom
/// vocabulary that four of them share verbatim. Fifteen <c>.resx</c> files averaging two entries
/// would give a translator fifteen files to open to translate one recurring sentence pattern.
/// </para>
/// <para>
/// The marker type exists for the same mechanical reason <c>DxDataGridResources</c> does: the
/// default localizer factory derives a resource name from the type argument, so localizing
/// against each chart type would look for a separate resource per chart — and for the generic
/// charts, a separate one per closed generic. See docs/localization.md.
/// </para>
/// <para>
/// Keys stay chart-specific (<c>PieChartLabel</c>, not <c>Label</c>) even though the file is
/// shared: the summaries differ per chart, and a shared key would force one wording onto all of
/// them. Only genuinely identical strings share a key — the zoom vocabulary does.
/// </para>
/// </remarks>
public sealed class DxChartResources;
