using System.Globalization;

namespace ManagedCode.LlmTestServer.Hosting;

internal sealed record LlmTestServerSankeyNode(LlmTestServerDashboardSegment Segment, double Y, double Height, double Share)
{
    public double Center => Y + Height / 2;
}

internal sealed record LlmTestServerSankeyBand(string Key, string Path, string Color, string Title);

internal sealed record LlmTestServerSankeyLayout(
    double Height,
    double CenterY,
    double CenterHeight,
    long Total,
    IReadOnlyList<LlmTestServerSankeyNode> Sources,
    IReadOnlyList<LlmTestServerSankeyNode> Targets,
    IReadOnlyList<LlmTestServerSankeyBand> Bands);

internal sealed record LlmTestServerDonutArc(LlmTestServerDashboardSegment Segment, string DashArray, string DashOffset, double Share);

// Server-rendered chart geometry: the dashboard draws these shapes as inline SVG without client-side scripts.
internal static class LlmTestServerDashboardCharts
{
    public const double SankeyWidth = 1000;
    public const double SourceX = 236;
    public const double CenterX = 494;
    public const double TargetX = 752;
    public const double NodeWidth = 12;
    public const double DonutRadius = 52;

    private const double _flowHeight = 210;
    private const double _minNodeHeight = 3;
    private const double _nodeGap = 8;
    private const double _labelPitch = 40;
    private const double _topPadding = 34;
    private const double _bottomPadding = 14;
    private const double _donutGap = 2.5;

    public static LlmTestServerSankeyLayout? Sankey(IEnumerable<LlmTestServerDashboardSegment> sources,
        IEnumerable<LlmTestServerDashboardSegment> targets)
    {
        var left = sources.Where(segment => segment.Tokens > 0).ToArray();
        var right = targets.Where(segment => segment.Tokens > 0).ToArray();
        var total = left.Sum(segment => segment.Tokens);
        if (total <= 0 || right.Sum(segment => segment.Tokens) != total)
        {
            return null;
        }

        var scale = _flowHeight / total;
        var (leftNodes, leftHeight) = Stack(left, scale, total);
        var (rightNodes, rightHeight) = Stack(right, scale, total);
        var columnHeight = Math.Max(_flowHeight, Math.Max(leftHeight, rightHeight));
        var centerY = _topPadding + (columnHeight - _flowHeight) / 2;
        leftNodes = Shift(leftNodes, _topPadding + (columnHeight - leftHeight) / 2);
        rightNodes = Shift(rightNodes, _topPadding + (columnHeight - rightHeight) / 2);

        var bands = new List<LlmTestServerSankeyBand>(leftNodes.Count + rightNodes.Count);
        var offset = centerY;
        foreach (var node in leftNodes)
        {
            var height = node.Segment.Tokens * scale;
            bands.Add(new(node.Segment.Key, Band(SourceX + NodeWidth, node.Y, node.Y + node.Height, CenterX, offset, offset + height),
                node.Segment.Color, $"{node.Segment.Label} → request\n{Tokens(node.Segment.Tokens)} tokens · {Percent(node.Share)}"));
            offset += height;
        }

        offset = centerY;
        foreach (var node in rightNodes)
        {
            var height = node.Segment.Tokens * scale;
            bands.Add(new(node.Segment.Key, Band(CenterX + NodeWidth, offset, offset + height, TargetX, node.Y, node.Y + node.Height),
                node.Segment.Color, $"Request → {node.Segment.Label}\n{Tokens(node.Segment.Tokens)} tokens · {Percent(node.Share)}"));
            offset += height;
        }

        return new(_topPadding + columnHeight + _bottomPadding, centerY, _flowHeight, total, leftNodes, rightNodes, bands);
    }

    public static IReadOnlyList<LlmTestServerDonutArc> Donut(IEnumerable<LlmTestServerDashboardSegment> segments)
    {
        var visible = segments.Where(segment => segment.Tokens > 0).ToArray();
        var total = visible.Sum(segment => segment.Tokens);
        if (total <= 0)
        {
            return [];
        }

        var circumference = 2 * Math.PI * DonutRadius;
        var gap = visible.Length > 1 ? _donutGap : 0;
        var arcs = new List<LlmTestServerDonutArc>(visible.Length);
        var start = 0d;
        foreach (var segment in visible)
        {
            var length = segment.Tokens * circumference / total;
            var drawn = Math.Max(0.8, length - gap);
            arcs.Add(new(segment, $"{Number(drawn)} {Number(circumference - drawn)}", Number(-(start + gap / 2)),
                (double)segment.Tokens / total));
            start += length;
        }

        return arcs;
    }

    public static string Number(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static string Tokens(long value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    public static string Percent(double share)
    {
        return share switch
        {
            <= 0 => "0%",
            < 0.001 => "<0.1%",
            < 0.1 => share.ToString("0.0%", CultureInfo.InvariantCulture),
            _ => share.ToString("0%", CultureInfo.InvariantCulture),
        };
    }

    private static (IReadOnlyList<LlmTestServerSankeyNode> Nodes, double Height) Stack(
        IReadOnlyList<LlmTestServerDashboardSegment> segments, double scale, long total)
    {
        var nodes = new List<LlmTestServerSankeyNode>(segments.Count);
        foreach (var segment in segments)
        {
            var height = Math.Max(_minNodeHeight, segment.Tokens * scale);
            var y = 0d;
            if (nodes.Count > 0)
            {
                // Keep two-line labels readable even when neighbouring nodes are only a few tokens tall.
                var previous = nodes[^1];
                y = Math.Max(previous.Y + previous.Height + _nodeGap, previous.Center + _labelPitch - height / 2);
            }

            nodes.Add(new(segment, y, height, (double)segment.Tokens / total));
        }

        return (nodes, nodes.Count == 0 ? 0 : nodes[^1].Y + nodes[^1].Height);
    }

    private static IReadOnlyList<LlmTestServerSankeyNode> Shift(IReadOnlyList<LlmTestServerSankeyNode> nodes, double offset)
    {
        return nodes.Select(node => node with { Y = node.Y + offset }).ToArray();
    }

    private static string Band(double x0, double y0, double y1, double x1, double y2, double y3)
    {
        var middle = (x0 + x1) / 2;
        return string.Create(CultureInfo.InvariantCulture,
            $"M{Number(x0)} {Number(y0)}C{Number(middle)} {Number(y0)} {Number(middle)} {Number(y2)} {Number(x1)} {Number(y2)}"
            + $"L{Number(x1)} {Number(y3)}C{Number(middle)} {Number(y3)} {Number(middle)} {Number(y1)} {Number(x0)} {Number(y1)}Z");
    }
}
