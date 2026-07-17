namespace BlazorDX.Primitives.Charts;

/// <summary>One node's angular arc around the circle.</summary>
public readonly record struct ChordArc(int Index, double StartAngle, double EndAngle);

/// <summary>One link's angular slice on each of its two endpoints' arcs.</summary>
public readonly record struct ChordRibbon(int LinkIndex, int From, int To, double FromStart, double FromEnd, double ToStart, double ToEnd);

/// <summary>A directed flow between two node indices, by value.</summary>
public readonly record struct ChordLinkInput(int From, int To, double Value);

/// <summary>
/// Chord diagram layout: each node gets an arc around the circle proportional to its total
/// involvement (sum of every link touching it, either direction); each link gets a slice within
/// its two endpoints' arcs, proportional to the link's own value, stacked in link order — the same
/// value-to-angle scale drives both, so a node's link slices always exactly fill its own arc. A
/// caller draws each <see cref="ChordRibbon"/> as a pair of arcs connected by bezier curves.
/// </summary>
public static class ChordLayout
{
    public static (IReadOnlyList<ChordArc> Arcs, IReadOnlyList<ChordRibbon> Ribbons) Compute(
        int nodeCount, IReadOnlyList<ChordLinkInput> links, double padAngle = 0.02)
    {
        if (nodeCount <= 0)
        {
            return ([], []);
        }

        double[] nodeTotal = new double[nodeCount];
        foreach (ChordLinkInput link in links)
        {
            if (link.From == link.To || (uint)link.From >= nodeCount || (uint)link.To >= nodeCount)
            {
                continue;   // a self-link doesn't make sense on a circle; defensively skip it
            }

            nodeTotal[link.From] += link.Value;
            nodeTotal[link.To] += link.Value;
        }

        double grandTotal = nodeTotal.Sum();
        double availableSweep = Math.Max(0, (2 * Math.PI) - (nodeCount * padAngle));
        double scale = grandTotal <= 0 ? 0 : availableSweep / grandTotal;

        double[] arcStart = new double[nodeCount];
        double[] arcEnd = new double[nodeCount];
        double[] cursor = new double[nodeCount];
        double angle = 0;
        for (int i = 0; i < nodeCount; i++)
        {
            arcStart[i] = angle;
            arcEnd[i] = angle + (nodeTotal[i] * scale);
            cursor[i] = arcStart[i];
            angle = arcEnd[i] + padAngle;
        }

        List<ChordRibbon> ribbons = [];
        for (int i = 0; i < links.Count; i++)
        {
            ChordLinkInput link = links[i];
            if (link.From == link.To || (uint)link.From >= nodeCount || (uint)link.To >= nodeCount)
            {
                continue;
            }

            double sweep = link.Value * scale;
            double fromStart = cursor[link.From];
            double fromEnd = fromStart + sweep;
            cursor[link.From] = fromEnd;

            double toStart = cursor[link.To];
            double toEnd = toStart + sweep;
            cursor[link.To] = toEnd;

            ribbons.Add(new ChordRibbon(i, link.From, link.To, fromStart, fromEnd, toStart, toEnd));
        }

        List<ChordArc> arcs = new(nodeCount);
        for (int i = 0; i < nodeCount; i++)
        {
            arcs.Add(new ChordArc(i, arcStart[i], arcEnd[i]));
        }

        return (arcs, ribbons);
    }
}
