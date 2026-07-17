namespace BlazorDX.Primitives.Charts;

/// <summary>One item's angular slice within its parent's allotted sweep.</summary>
/// <param name="Index">The item's index into the <c>values</c> list passed to <see cref="SunburstLayout.Compute"/>.</param>
public readonly record struct SunburstSlice(int Index, double StartAngle, double EndAngle);

/// <summary>
/// Radial partition layout for a sunburst chart: subdivides an angular sweep among items
/// proportionally to <c>values</c>. Single-level and geometry-agnostic, like
/// <see cref="TreemapLayout"/> — a caller recurses per ring, passing each item's own slice down as
/// the sweep for its children (ring radius is purely a per-depth rendering concern, not a layout
/// one).
/// </summary>
public static class SunburstLayout
{
    public static IReadOnlyList<SunburstSlice> Compute(IReadOnlyList<double> values, double startAngle, double endAngle)
    {
        List<SunburstSlice> result = new(values.Count);
        double total = 0;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] > 0)
            {
                total += values[i];
            }
        }

        if (total <= 0 || endAngle <= startAngle)
        {
            return result;
        }

        double sweep = endAngle - startAngle;
        double angle = startAngle;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] <= 0)
            {
                continue;
            }

            double slice = values[i] / total * sweep;
            result.Add(new SunburstSlice(i, angle, angle + slice));
            angle += slice;
        }

        return result;
    }
}
