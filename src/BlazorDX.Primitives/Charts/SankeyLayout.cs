namespace BlazorDX.Primitives.Charts;

/// <summary>One node's position and size after layout.</summary>
public readonly record struct SankeyNodeLayout(string Id, double X, double Y, double Width, double Height, int Layer);

/// <summary>
/// One link's endpoints after layout — a caller draws a cubic-bezier ribbon between the source and
/// target ports, at the given thickness.
/// </summary>
public readonly record struct SankeyLinkLayout(
    int LinkIndex, double SourceX, double SourceY, double TargetX, double TargetY, double Thickness);

/// <summary>A source/target/value triple, keyed by node id (before layout).</summary>
public readonly record struct SankeyLinkInput(string Source, string Target, double Value);

/// <summary>
/// A layered ("Sugiyama-style") Sankey layout: each node's layer is its longest path from a source
/// (a node with no incoming links); nodes are stacked vertically within their layer's column,
/// sized proportionally to their total flow; each link is drawn as a ribbon between a proportional
/// slot on its source's outgoing side and its target's incoming side. This is a straightforward
/// layered layout, not full crossing-minimization (d3-sankey's iterative relaxation) — enough for
/// the moderate node/link counts a Sankey diagram realistically shows; ordering within a layer
/// falls back to <c>nodeIds</c>' own order.
/// </summary>
public static class SankeyLayout
{
    public static (IReadOnlyList<SankeyNodeLayout> Nodes, IReadOnlyList<SankeyLinkLayout> Links) Compute(
        IReadOnlyList<string> nodeIds,
        IReadOnlyList<SankeyLinkInput> links,
        double width,
        double height,
        double nodeWidth = 16,
        double nodePadding = 12)
    {
        if (nodeIds.Count == 0)
        {
            return ([], []);
        }

        Dictionary<string, List<int>> outLinks = nodeIds.ToDictionary(id => id, _ => new List<int>());
        Dictionary<string, List<int>> inLinks = nodeIds.ToDictionary(id => id, _ => new List<int>());
        for (int i = 0; i < links.Count; i++)
        {
            if (outLinks.TryGetValue(links[i].Source, out List<int>? outs))
            {
                outs.Add(i);
            }

            if (inLinks.TryGetValue(links[i].Target, out List<int>? ins))
            {
                ins.Add(i);
            }
        }

        // Layer = longest path from a source (indegree 0). Kahn's algorithm over a DAG; a real
        // flow diagram shouldn't have cycles, but a node a Kahn pass never reaches (part of one)
        // just defensively stays at layer 0 rather than looping forever.
        Dictionary<string, int> layer = nodeIds.ToDictionary(id => id, _ => 0);
        Dictionary<string, int> indegree = nodeIds.ToDictionary(id => id, id => inLinks[id].Count);
        Queue<string> queue = new(nodeIds.Where(id => indegree[id] == 0));
        HashSet<string> visited = new(queue);
        while (queue.Count > 0)
        {
            string id = queue.Dequeue();
            foreach (int linkIndex in outLinks[id])
            {
                string target = links[linkIndex].Target;
                layer[target] = Math.Max(layer[target], layer[id] + 1);
                if (visited.Add(target))
                {
                    queue.Enqueue(target);
                }
            }
        }

        Dictionary<string, double> value = nodeIds.ToDictionary(id => id,
            id => Math.Max(outLinks[id].Sum(i => links[i].Value), inLinks[id].Sum(i => links[i].Value)));

        List<IGrouping<int, string>> byLayer = nodeIds.GroupBy(id => layer[id]).OrderBy(g => g.Key).ToList();
        int maxLayer = byLayer.Count == 0 ? 0 : byLayer.Max(g => g.Key);
        int maxCount = byLayer.Count == 0 ? 1 : byLayer.Max(g => g.Count());
        double maxLayerValue = byLayer.Count == 0 ? 1 : Math.Max(1e-9, byLayer.Max(g => g.Sum(id => value[id])));
        double usableHeight = Math.Max(1, height - (nodePadding * Math.Max(0, maxCount - 1)));
        double scale = usableHeight / maxLayerValue;
        double layerGap = maxLayer <= 0 ? 0 : (width - nodeWidth) / maxLayer;

        Dictionary<string, SankeyNodeLayout> positioned = new();
        foreach (IGrouping<int, string> group in byLayer)
        {
            double x = group.Key * layerGap;
            double cursorY = 0;
            foreach (string id in group)
            {
                double h = Math.Max(1, value[id] * scale);
                positioned[id] = new SankeyNodeLayout(id, x, cursorY, nodeWidth, h, group.Key);
                cursorY += h + nodePadding;
            }
        }

        List<SankeyNodeLayout> nodeResult = nodeIds.Select(id => positioned[id]).ToList();

        // Stack each node's links along its own height, proportional to value, in link order.
        Dictionary<string, double> outCursor = nodeIds.ToDictionary(id => id, id => positioned[id].Y);
        Dictionary<string, double> inCursor = nodeIds.ToDictionary(id => id, id => positioned[id].Y);
        List<SankeyLinkLayout> linkResult = new(links.Count);
        for (int i = 0; i < links.Count; i++)
        {
            SankeyLinkInput link = links[i];
            if (!positioned.TryGetValue(link.Source, out SankeyNodeLayout source) ||
                !positioned.TryGetValue(link.Target, out SankeyNodeLayout target))
            {
                continue;
            }

            double thickness = Math.Max(0.5, link.Value * scale);
            double sy = outCursor[link.Source] + (thickness / 2);
            double ty = inCursor[link.Target] + (thickness / 2);
            outCursor[link.Source] += thickness;
            inCursor[link.Target] += thickness;

            linkResult.Add(new SankeyLinkLayout(i, source.X + source.Width, sy, target.X, ty, thickness));
        }

        return (nodeResult, linkResult);
    }
}
