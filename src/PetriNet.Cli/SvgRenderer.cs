using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using PetriNet.Core;

namespace PetriNet.Cli;

/// <summary>Dependency-free SVG output with XML-escaped labels and deterministic layouts.</summary>
public static class SvgRenderer
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private const string Ink = "#203449";
    private const string Green = "#087f5b";
    private const string Red = "#b42318";
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static XAttribute A(string name, object value) => new(name, value is double d ? F(d) : value);
    private static XElement E(string name, params object[] content) => new(Svg + name, content);
    private static XElement Text(double x, double y, string text, int size = 14, string anchor = "start", string fill = Ink) =>
        E("text", A("x", x), A("y", y), A("font-size", size), A("text-anchor", anchor), A("fill", fill), text);

    private static XElement Canvas(double width, double height, string title, string subtitle)
    {
        return E("svg", A("width", width), A("height", height), A("viewBox", $"0 0 {F(width)} {F(height)}"),
            A("role", "img"), A("font-family", "Segoe UI, Arial, sans-serif"),
            E("title", title), E("desc", subtitle),
            E("rect", A("width", "100%"), A("height", "100%"), A("fill", "#f8fafc")),
            E("defs", E("marker", A("id", "arrow"), A("viewBox", "0 0 10 10"), A("refX", 9), A("refY", 5),
                A("markerWidth", 7), A("markerHeight", 7), A("orient", "auto-start-reverse"),
                E("path", A("d", "M 0 0 L 10 5 L 0 10 z"), A("fill", "#64748b")))),
            Text(28, 36, title, 22), Text(28, 62, subtitle, 13));
    }

    public static XDocument Net(Net net, Marking marking)
    {
        if (marking.Count != net.Places.Count) throw new ArgumentException("Marking dimension mismatch.");
        // Default bipartite columns. Explicit positions are translated to leave room for the heading.
        var rawPlaces = net.Places.Select((p, i) => p.Position ?? new Position(100, 100 + i * 110)).ToArray();
        var rawTransitions = net.Transitions.Select((t, i) => t.Position ?? new Position(450, 100 + i * 110)).ToArray();
        var all = rawPlaces.Concat(rawTransitions).ToArray();
        var offsetX = 100 - all.Min(p => p.X);
        var offsetY = 135 - all.Min(p => p.Y);
        Position Shift(Position p) => new(p.X + offsetX, p.Y + offsetY);
        var places = rawPlaces.Select(Shift).ToArray();
        var transitions = rawTransitions.Select(Shift).ToArray();
        var width = Math.Max(840, all.Max(p => p.X) + offsetX + 140);
        var height = Math.Max(360, all.Max(p => p.Y) + offsetY + 120);
        var root = Canvas(width, height, net.Name, "Circles: places · bars: transitions · green: enabled · numbers: tokens / arc weights");
        for (var t = 0; t < transitions.Length; t++)
            for (var p = 0; p < places.Length; p++)
            {
                if (net.Input(p, t) > 0) Arc(root, places[p], transitions[t], 31, 19, net.Input(p, t), net.Output(p, t) > 0 ? 16 : 0);
                if (net.Output(p, t) > 0) Arc(root, transitions[t], places[p], 19, 31, net.Output(p, t), net.Input(p, t) > 0 ? 16 : 0);
            }
        for (var p = 0; p < places.Length; p++)
        {
            var point = places[p];
            root.Add(E("circle", A("cx", point.X), A("cy", point.Y), A("r", 30), A("fill", "white"), A("stroke", Ink), A("stroke-width", 2)));
            root.Add(Text(point.X, point.Y + 6, marking[p].ToString(), 19, "middle"));
            root.Add(Text(point.X, point.Y + 53, net.Places[p].Id, 14, "middle"));
        }
        for (var t = 0; t < transitions.Length; t++)
        {
            var point = transitions[t];
            var enabled = net.IsEnabled(marking, t);
            root.Add(E("rect", A("x", point.X - 12), A("y", point.Y - 26), A("width", 24), A("height", 52),
                A("rx", 3), A("fill", enabled ? Green : "#64748b")));
            root.Add(Text(point.X, point.Y + 53, net.Transitions[t].Id, 14, "middle", enabled ? Green : Ink));
        }
        return new XDocument(root);
    }

    private static void Arc(XElement root, Position from, Position to, double startRadius, double endRadius,
        BigInteger weight, double bend)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) return;
        var ux = dx / length;
        var uy = dy / length;
        var x1 = from.X + ux * startRadius;
        var y1 = from.Y + uy * startRadius;
        var x2 = to.X - ux * endRadius;
        var y2 = to.Y - uy * endRadius;
        var mx = (x1 + x2) / 2 - uy * bend;
        var my = (y1 + y2) / 2 + ux * bend;
        root.Add(E("path", A("d", $"M {F(x1)} {F(y1)} Q {F(mx)} {F(my)} {F(x2)} {F(y2)}"),
            A("fill", "none"), A("stroke", "#64748b"), A("stroke-width", 1.6), A("marker-end", "url(#arrow)")));
        if (weight > 1) root.Add(Text(mx + 7, my - 7, weight.ToString(), 13));
    }

    public static XDocument Timeline(Net net, IReadOnlyList<TraceStep> trace, IReadOnlyList<PlaceInvariant> invariants)
    {
        if (trace.Count == 0) throw new ArgumentException("A timeline needs an initial marking.");
        var shown = invariants.Take(8).ToArray();
        var equations = shown.Select(i => i.Equation(net)).ToArray();
        var labelWidth = Math.Max(230, Math.Min(1100, equations.Concat(net.Places.Select(p => p.Id)).Max(s => s.Length) * 8 + 35));
        var cellWidth = Math.Max(120, trace.Where(s => s.Transition.HasValue)
            .Select(s => net.Transitions[s.Transition!.Value].Id.Length * 8 + 24).DefaultIfEmpty(120).Max());
        var height = 170 + (net.Places.Count + shown.Length) * 43 + 65;
        var root = Canvas(labelWidth + trace.Count * cellWidth + 30, height, "Token-state timeline",
            net.Name + " · each column is the marking after the named firing");
        for (var col = 0; col < trace.Count; col++)
        {
            var x = labelWidth + col * cellWidth;
            root.Add(Text(x + cellWidth / 2, 103, $"step {col}", 13, "middle"));
            root.Add(Text(x + cellWidth / 2, 126, trace[col].Transition is int t ? net.Transitions[t].Id : "initial", 13, "middle"));
        }
        for (var row = 0; row < net.Places.Count + shown.Length; row++)
        {
            var y = 145 + row * 43;
            var isInvariant = row >= net.Places.Count;
            root.Add(E("rect", A("x", 20), A("y", y), A("width", labelWidth + trace.Count * cellWidth - 10), A("height", 40),
                A("fill", isInvariant ? "#e6f4ee" : row % 2 == 0 ? "#eaf0f6" : "white")));
            root.Add(Text(30, y + 26, isInvariant ? equations[row - net.Places.Count] : net.Places[row].Id, 14));
            for (var col = 0; col < trace.Count; col++)
            {
                var value = isInvariant ? shown[row - net.Places.Count].Evaluate(trace[col].Marking) : trace[col].Marking[row];
                var valid = !isInvariant || value == shown[row - net.Places.Count].Constant;
                root.Add(Text(labelWidth + col * cellWidth + cellWidth / 2, y + 26, value.ToString(), 15, "middle", valid ? Ink : Red));
            }
        }
        root.Add(Text(28, height - 24, $"Showing {shown.Length} of {invariants.Count} discovered conservation equations; all are checked after each firing.", 12));
        return new XDocument(root);
    }

    public static XDocument Reachability(Net net, ExplorationResult result)
    {
        if (result.States.Count > 100 || result.Edges.Count > 500) throw new ArgumentException("Graph exceeds SVG limits.");
        const int columns = 4;
        const int cellWidth = 270;
        var rows = (result.States.Count + columns - 1) / columns;
        Position Point(int id) => new(145 + id % columns * cellWidth, 165 + id / columns * 170);
        var root = Canvas(columns * cellWidth + 40, rows * 170 + 170, "Reachability graph",
            $"{net.Name} · {result.Status} · red: terminal · edges labeled by transition ID");
        foreach (var edge in result.Edges)
        {
            var from = Point(edge.From);
            var to = Point(edge.To);
            var label = net.Transitions[edge.Transition].Id;
            if (edge.From == edge.To)
            {
                root.Add(E("path", A("d", $"M {F(from.X - 15)} {F(from.Y - 32)} c -65 -80 95 -80 30 0"),
                    A("fill", "none"), A("stroke", "#64748b"), A("marker-end", "url(#arrow)")));
                root.Add(Text(from.X, from.Y - 95, label, 12, "middle"));
            }
            else
            {
                Arc(root, from, to, 42, 42, 1, 36);
                var dx = to.X - from.X;
                var dy = to.Y - from.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                root.Add(Text((from.X + to.X) / 2 - dy / length * 20, (from.Y + to.Y) / 2 + dx / length * 20 - 4,
                    label, 11, "middle"));
            }
        }
        for (var s = 0; s < result.States.Count; s++)
        {
            var point = Point(s);
            var terminal = result.TerminalStates.Contains(s);
            root.Add(E("circle", A("cx", point.X), A("cy", point.Y), A("r", 40),
                A("fill", terminal ? "#fee4e2" : "white"), A("stroke", terminal ? Red : Ink), A("stroke-width", s == 0 ? 4 : 2),
                E("title", string.Join(", ", net.Places.Select((p, i) => $"{p.Id}={result.States[s][i]}")))));
            root.Add(Text(point.X, point.Y + 5, $"s{s}", 18, "middle"));
            root.Add(Text(point.X, point.Y + 64, result.States[s].ToString(), 12, "middle"));
        }
        root.Add(Text(28, rows * 170 + 140, "Token vector order: " + string.Join(", ", net.Places.Select(p => p.Id)), 12));
        return new XDocument(root);
    }
}
