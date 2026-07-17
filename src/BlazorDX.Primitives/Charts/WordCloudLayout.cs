namespace BlazorDX.Primitives.Charts;

/// <summary>One word's placement after layout.</summary>
public readonly record struct WordCloudPlacement(int Index, double X, double Y, double FontSize);

/// <summary>One word and the weight driving its font size.</summary>
public readonly record struct WordCloudInput(string Text, double Weight);

/// <summary>
/// Spiral-packing word-cloud layout (the classic Wordle/d3-cloud approach): words are placed
/// largest-first, spiraling outward from center until a non-overlapping spot is found. A word's
/// extent is approximated as an axis-aligned box (text-length x font-size heuristic) rather than
/// true glyph shapes — the same simplification every lightweight word-cloud implementation makes,
/// since exact glyph metrics aren't available without an actual font-measurement pass. A word that
/// can't fit within the search radius is dropped, not thrown — <see cref="Compute"/> can return
/// fewer placements than inputs; compare counts to detect this.
/// </summary>
public static class WordCloudLayout
{
    public static IReadOnlyList<WordCloudPlacement> Compute(
        IReadOnlyList<WordCloudInput> words, double width, double height, double minFontSize = 12, double maxFontSize = 64)
    {
        if (words.Count == 0 || width <= 0 || height <= 0)
        {
            return [];
        }

        double maxWeight = words.Max(w => w.Weight);
        double minWeight = words.Min(w => w.Weight);
        double weightSpan = Math.Max(1e-9, maxWeight - minWeight);

        List<(int Index, WordCloudInput Word, double FontSize)> sized = words
            .Select((w, i) => (i, w, minFontSize + ((w.Weight - minWeight) / weightSpan * (maxFontSize - minFontSize))))
            .OrderByDescending(t => t.Item3)
            .ToList();

        double cx = width / 2;
        double cy = height / 2;
        double maxRadius = Math.Max(width, height);
        List<(double X, double Y, double W, double H)> placed = [];
        List<WordCloudPlacement> result = new(words.Count);

        foreach ((int index, WordCloudInput word, double fontSize) in sized)
        {
            double w = Math.Max(fontSize, word.Text.Length * fontSize * 0.62);
            double h = fontSize * 1.15;

            if (TryPlace(cx, cy, w, h, placed, maxRadius, out double px, out double py))
            {
                placed.Add((px, py, w, h));
                result.Add(new WordCloudPlacement(index, px + (w / 2), py + (h / 2), fontSize));
            }
        }

        return result;
    }

    // Archimedean spiral (r = a * angle) sampled at small angle steps, centered on (cx, cy).
    private static bool TryPlace(
        double cx, double cy, double w, double h, List<(double X, double Y, double W, double H)> placed,
        double maxRadius, out double x, out double y)
    {
        const double angleStep = 0.32;
        const double growth = 2.0;
        double angle = 0;

        while (true)
        {
            double r = growth * angle;
            if (r > maxRadius)
            {
                break;
            }

            x = cx + (r * Math.Cos(angle)) - (w / 2);
            y = cy + (r * Math.Sin(angle)) - (h / 2);

            if (!Overlaps(x, y, w, h, placed))
            {
                return true;
            }

            angle += angleStep;
        }

        x = 0;
        y = 0;
        return false;
    }

    private static bool Overlaps(double x, double y, double w, double h, List<(double X, double Y, double W, double H)> placed)
    {
        const double pad = 2;
        foreach ((double px, double py, double pw, double ph) in placed)
        {
            if (x < px + pw + pad && x + w + pad > px && y < py + ph + pad && y + h + pad > py)
            {
                return true;
            }
        }

        return false;
    }
}
