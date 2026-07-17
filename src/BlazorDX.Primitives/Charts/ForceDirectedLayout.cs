namespace BlazorDX.Primitives.Charts;

/// <summary>One node's settled position after the simulation.</summary>
public readonly record struct ForceNodeLayout(int Index, double X, double Y);

/// <summary>A directed edge between two node indices (order doesn't affect the layout — undirected physically).</summary>
public readonly record struct ForceEdgeInput(int Source, int Target);

/// <summary>
/// A force-directed ("spring embedder", Fruchterman-Reingold-style) graph layout: every node
/// repels every other node, an edge pulls its two endpoints together like a spring, and a weak
/// center-gravity force keeps the whole graph from drifting off-canvas. Iterates a fixed step count
/// with simulated-annealing cooling to settle into a stable layout. Nodes start on a circle (not
/// randomized), so the same input always produces the same output. Realistic network/dependency
/// diagrams run to tens or low hundreds of nodes, well within plain C#'s budget even at O(n²) per
/// step — the same "does this need Rust" judgment call this library already makes elsewhere (see
/// the Scheduler's date math, or the other Tier-2 chart layouts); no Rust/wasm kernel here.
/// </summary>
public static class ForceDirectedLayout
{
    public static IReadOnlyList<ForceNodeLayout> Compute(
        int nodeCount, IReadOnlyList<ForceEdgeInput> edges, double width, double height, int iterations = 300)
    {
        if (nodeCount <= 0 || width <= 0 || height <= 0)
        {
            return [];
        }

        double cx = width / 2;
        double cy = height / 2;
        double radius = Math.Min(width, height) * 0.35;
        double[] x = new double[nodeCount];
        double[] y = new double[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            double angle = 2 * Math.PI * i / nodeCount;
            x[i] = cx + (radius * Math.Cos(angle));
            y[i] = cy + (radius * Math.Sin(angle));
        }

        // Ideal edge length under Fruchterman-Reingold: spreads nodeCount nodes evenly over the area.
        double k = Math.Sqrt(width * height / nodeCount);
        double temperature = Math.Min(width, height) * 0.1;

        double[] dx = new double[nodeCount];
        double[] dy = new double[nodeCount];
        for (int iter = 0; iter < iterations; iter++)
        {
            Array.Clear(dx);
            Array.Clear(dy);

            for (int i = 0; i < nodeCount; i++)
            {
                for (int j = i + 1; j < nodeCount; j++)
                {
                    double ddx = x[i] - x[j];
                    double ddy = y[i] - y[j];
                    double dist = Math.Max(0.01, Math.Sqrt((ddx * ddx) + (ddy * ddy)));
                    double force = k * k / dist;
                    double fx = ddx / dist * force;
                    double fy = ddy / dist * force;
                    dx[i] += fx;
                    dy[i] += fy;
                    dx[j] -= fx;
                    dy[j] -= fy;
                }
            }

            foreach (ForceEdgeInput e in edges)
            {
                if (e.Source == e.Target || (uint)e.Source >= nodeCount || (uint)e.Target >= nodeCount)
                {
                    continue;
                }

                double ddx = x[e.Source] - x[e.Target];
                double ddy = y[e.Source] - y[e.Target];
                double dist = Math.Max(0.01, Math.Sqrt((ddx * ddx) + (ddy * ddy)));
                double force = dist * dist / k;
                double fx = ddx / dist * force;
                double fy = ddy / dist * force;
                dx[e.Source] -= fx;
                dy[e.Source] -= fy;
                dx[e.Target] += fx;
                dy[e.Target] += fy;
            }

            for (int i = 0; i < nodeCount; i++)
            {
                dx[i] += (cx - x[i]) * 0.01;
                dy[i] += (cy - y[i]) * 0.01;

                double dist = Math.Max(0.01, Math.Sqrt((dx[i] * dx[i]) + (dy[i] * dy[i])));
                double capped = Math.Min(dist, temperature);
                x[i] = Math.Clamp(x[i] + (dx[i] / dist * capped), 10, width - 10);
                y[i] = Math.Clamp(y[i] + (dy[i] / dist * capped), 10, height - 10);
            }

            temperature *= 0.98;
        }

        List<ForceNodeLayout> result = new(nodeCount);
        for (int i = 0; i < nodeCount; i++)
        {
            result.Add(new ForceNodeLayout(i, x[i], y[i]));
        }

        return result;
    }
}
