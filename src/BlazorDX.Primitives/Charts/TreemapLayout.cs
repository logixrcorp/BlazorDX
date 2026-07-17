namespace BlazorDX.Primitives.Charts;

/// <summary>One item's laid-out rectangle within its parent's allotted area.</summary>
/// <param name="Index">The item's index into the <c>values</c> list passed to <see cref="TreemapLayout.Compute"/>.</param>
public readonly record struct TreemapRect(int Index, double X, double Y, double Width, double Height);

/// <summary>
/// Squarified treemap layout (Bruls/Huizing/van Wijk 2000): subdivides a rectangle among items
/// sized proportionally to <c>values</c>, keeping each item's aspect ratio close to 1 instead of
/// the thin slivers a naive proportional-slice layout produces. Geometry-agnostic and single-level
/// by design — it operates on plain sizes and an index, not a tree type — so a caller builds a
/// full hierarchy (<see cref="BlazorDX.Components.TreeNode"/>) by recursing: lay out a node's
/// children in its own rect, then recurse into each child's own rect with its own children.
/// </summary>
public static class TreemapLayout
{
    private readonly record struct Rect(double X, double Y, double Width, double Height);

    public static IReadOnlyList<TreemapRect> Compute(IReadOnlyList<double> values, double x, double y, double width, double height)
    {
        List<TreemapRect> result = new(values.Count);
        if (values.Count == 0 || width <= 0 || height <= 0)
        {
            return result;
        }

        // Squarify only produces near-square rows when items are processed largest-first.
        List<(int Index, double Value)> items = new(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] > 0)
            {
                items.Add((i, values[i]));
            }
        }

        if (items.Count == 0)
        {
            return result;
        }

        items.Sort((a, b) => b.Value.CompareTo(a.Value));

        double total = items.Sum(it => it.Value);
        double area = width * height;

        // Normalize so every item's "value" is already its target area — avoids re-deriving the
        // value-to-area scale on every row.
        List<(int Index, double Area)> scaled = items.Select(it => (it.Index, it.Value / total * area)).ToList();

        Rect rect = new(x, y, width, height);
        int cursor = 0;
        while (cursor < scaled.Count)
        {
            double side = Math.Min(rect.Width, rect.Height);
            double rMax = scaled[cursor].Area;   // first remaining item is the largest (sorted desc)
            double rowSum = 0;
            double bestWorst = double.PositiveInfinity;
            int bestEnd = cursor;
            double bestSum = 0;

            int probe = cursor;
            while (probe < scaled.Count)
            {
                double candidateSum = rowSum + scaled[probe].Area;
                double candidateRMin = scaled[probe].Area;   // smallest so far, since sorted desc
                double worst = WorstRatio(side, candidateSum, rMax, candidateRMin);
                if (worst > bestWorst)
                {
                    break;
                }

                bestWorst = worst;
                bestEnd = probe + 1;
                bestSum = candidateSum;
                rowSum = candidateSum;
                probe++;
            }

            LayoutRow(scaled, cursor, bestEnd, bestSum, ref rect, result);
            cursor = bestEnd;
        }

        return result;
    }

    // Closed-form worst aspect ratio for a row of items summing to rowSum, laid out along a strip
    // whose fixed dimension is `side` (the shorter side of the remaining rect) — from the
    // squarify paper. rMax/rMin are the largest/smallest item areas in the candidate row.
    private static double WorstRatio(double side, double rowSum, double rMax, double rMin)
    {
        if (side <= 1e-9 || rowSum <= 1e-9 || rMin <= 1e-9)
        {
            return double.PositiveInfinity;
        }

        double s2 = side * side;
        double sum2 = rowSum * rowSum;
        return Math.Max(s2 * rMax / sum2, sum2 / (s2 * rMin));
    }

    private static void LayoutRow(
        List<(int Index, double Area)> scaled, int start, int end, double rowSum, ref Rect rect, List<TreemapRect> result)
    {
        bool vertical = rect.Width >= rect.Height;   // row is a strip on the wider side's edge
        if (vertical)
        {
            double stripW = rect.Height <= 1e-9 ? 0 : rowSum / rect.Height;
            double cy = rect.Y;
            for (int i = start; i < end; i++)
            {
                double h = stripW <= 1e-9 ? 0 : scaled[i].Area / stripW;
                result.Add(new TreemapRect(scaled[i].Index, rect.X, cy, stripW, h));
                cy += h;
            }

            rect = rect with { X = rect.X + stripW, Width = Math.Max(0, rect.Width - stripW) };
        }
        else
        {
            double stripH = rect.Width <= 1e-9 ? 0 : rowSum / rect.Width;
            double cx = rect.X;
            for (int i = start; i < end; i++)
            {
                double w = stripH <= 1e-9 ? 0 : scaled[i].Area / stripH;
                result.Add(new TreemapRect(scaled[i].Index, cx, rect.Y, w, stripH));
                cx += w;
            }

            rect = rect with { Y = rect.Y + stripH, Height = Math.Max(0, rect.Height - stripH) };
        }
    }
}
