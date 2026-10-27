using System.Globalization;
using System.Text.Json;
using PetriNet.Core;

namespace PetriNet.Cli;

public static class Commands
{
    private const string Help = """
        Petri Net Token Conservation (.NET 10)
        Usage: PetriNet.Cli <command> <net.json> [options]
          validate     Validate JSON and token-flow structure
          invariants   Print incidence matrix, exact basis and conservation laws
          explore      Explore reachable markings and report terminal states
          simulate     Select enabled transitions, or use --sequence id,id,...
          render       Write net.svg, timeline.svg, reachability.svg and report.json
        Options:
          --bound N              Coefficient bound (default 2, range 0..100)
          --max-combinations N   Combination limit (default 100000)
          --max-states N         State limit (default 10000)
          --max-edges N          Firing-edge limit (default 100000)
          --seconds N           Exploration time limit in seconds (default 10)
          --out DIRECTORY       Render destination (required for render)
          --report FILE         Write JSON report (explore)
          --sequence IDS        Comma-separated transition IDs (simulate/render)
          --state N             Render a discovered state and shortest trace to it
        Exit codes: 0 success; 1 error; 2 partial exploration; 3 invariant violation; 130 cancelled.
        """;

    public static int Run(string[] args, TextReader input, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (args.Length == 0 || args is ["--help"] or ["-h"])
            {
                output.WriteLine(Help);
                return 0;
            }
            if (args.Length < 2) throw new ArgumentException("Provide a command and JSON file. Use --help for usage.");
            var command = args[0];
            if (command is not ("validate" or "invariants" or "explore" or "simulate" or "render"))
                throw new ArgumentException($"Unknown command '{command}'.");
            var options = ParseOptions(args[2..]);
            ValidateOptions(command, options);
            var net = NetJson.Load(args[1]);
            output.WriteLine($"{net.Name}: {net.Places.Count} places, {net.Transitions.Count} transitions");
            if (command == "validate") { output.WriteLine("Valid weighted P/T net."); return 0; }
            var analysis = InvariantAnalyzer.Analyze(net,
                new InvariantOptions(Integer(options, "--bound", 2, 0, 100),
                    LongInteger(options, "--max-combinations", 100_000)), cancellationToken);
            if (command == "invariants") { PrintAlgebra(net, analysis, output); return 0; }
            if (command == "simulate")
            {
                Simulate(net, analysis.Invariants, options, input, output, cancellationToken);
                return 0;
            }
            var seconds = options.TryGetValue("--seconds", out var value)
                ? double.Parse(value, CultureInfo.InvariantCulture) : 10;
            if (!double.IsFinite(seconds) || seconds <= 0 || seconds > TimeSpan.MaxValue.TotalSeconds / 2)
                throw new ArgumentException("--seconds must be a positive finite duration.");
            var exploration = Explorer.Explore(net, analysis.Invariants,
                new ExplorationOptions(Integer(options, "--max-states", 10_000), Integer(options, "--max-edges", 100_000),
                    TimeSpan.FromSeconds(seconds)), cancellationToken);
            PrintExploration(net, analysis, exploration, output);
            if (options.TryGetValue("--report", out var report)) Reports.Write(report, net, analysis, exploration);
            if (command == "render")
            {
                var destination = options["--out"];
                var trace = options.TryGetValue("--sequence", out var sequence)
                    ? Sequence(net, analysis.Invariants, sequence, cancellationToken)
                    : exploration.TraceTo(options.TryGetValue("--state", out var stateText)
                        ? int.Parse(stateText, CultureInfo.InvariantCulture)
                        : exploration.TerminalStates.FirstOrDefault(exploration.States.Count - 1));
                Directory.CreateDirectory(destination);
                SvgRenderer.Net(net, trace[^1].Marking).Save(Path.Combine(destination, "net.svg"));
                SvgRenderer.Timeline(net, trace, analysis.Invariants).Save(Path.Combine(destination, "timeline.svg"));
                if (exploration.States.Count <= 100 && exploration.Edges.Count <= 500)
                    SvgRenderer.Reachability(net, exploration).Save(Path.Combine(destination, "reachability.svg"));
                else
                {
                    var graphPath = Path.Combine(destination, "reachability.svg");
                    if (File.Exists(graphPath)) File.Delete(graphPath);
                    output.WriteLine("Reachability SVG omitted: graph exceeds 100 states or 500 edges.");
                }
                Reports.Write(Path.Combine(destination, "report.json"), net, analysis, exploration);
                output.WriteLine($"Artifacts: {Path.GetFullPath(destination)}");
            }
            return exploration.Violations.Count > 0 ? 3 : exploration.Status switch
            {
                ExplorationStatus.Complete => 0,
                ExplorationStatus.Cancelled => 130,
                _ => 2
            };
        }
        catch (OperationCanceledException) { error.WriteLine("Cancelled."); return 130; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or
            UnauthorizedAccessException or JsonException or FormatException or OverflowException)
        {
            error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
            if (i + 1 == args.Length || !args[i].StartsWith("--", StringComparison.Ordinal) ||
                !options.TryAdd(args[i], args[i + 1])) throw new ArgumentException($"Invalid or repeated option '{args[i]}'.");
        return options;
    }

    private static void ValidateOptions(string command, Dictionary<string, string> options)
    {
        string[] algebra = ["--bound", "--max-combinations"];
        string[] exploration = ["--max-states", "--max-edges", "--seconds"];
        var allowed = command switch
        {
            "validate" => Array.Empty<string>(),
            "invariants" => algebra,
            "simulate" => [.. algebra, "--sequence"],
            "explore" => [.. algebra, .. exploration, "--report"],
            _ => [.. algebra, .. exploration, "--out", "--sequence", "--state"]
        };
        foreach (var key in options.Keys)
            if (!allowed.Contains(key)) throw new ArgumentException($"Option '{key}' is not supported by {command}.");
        if (command == "render" && !options.ContainsKey("--out")) throw new ArgumentException("render requires --out DIRECTORY.");
        if (options.ContainsKey("--state") && options.ContainsKey("--sequence")) throw new ArgumentException("Choose --state or --sequence.");
    }

    private static int Integer(Dictionary<string, string> options, string key, int fallback, int min = 1, int max = int.MaxValue)
    {
        var value = options.TryGetValue(key, out var text) ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;
        if (value < min || value > max) throw new ArgumentException($"{key} must be between {min} and {max}.");
        return value;
    }

    private static long LongInteger(Dictionary<string, string> options, string key, long fallback)
    {
        var value = options.TryGetValue(key, out var text) ? long.Parse(text, CultureInfo.InvariantCulture) : fallback;
        if (value < 1) throw new ArgumentException($"{key} must be positive.");
        return value;
    }

    private static void PrintAlgebra(Net net, InvariantAnalysis analysis, TextWriter output)
    {
        output.WriteLine("Incidence C = Post - Pre (rows: places; columns: transitions)");
        output.WriteLine("          " + string.Join(" ", net.Transitions.Select(t => t.Id)));
        var c = net.IncidenceMatrix();
        for (var p = 0; p < net.Places.Count; p++)
            output.WriteLine($"{net.Places[p].Id}: [{string.Join(", ", Enumerable.Range(0, net.Transitions.Count).Select(t => c[p, t]))}]");
        output.WriteLine($"Exact nullspace dimension: {analysis.IntegerNullspaceBasis.Count}");
        foreach (var basis in analysis.IntegerNullspaceBasis) output.WriteLine($"  [{string.Join(", ", basis)}]");
        output.WriteLine("Discovered nonnegative invariants (incomplete search; not necessarily independent or minimal):");
        foreach (var invariant in analysis.Invariants) output.WriteLine("  " + invariant.Equation(net));
        output.WriteLine($"Combinations tried: {analysis.CombinationsTried}; search truncated: {analysis.SearchTruncated}");
        output.WriteLine($"Boundedness certificate covers all places: {analysis.ProvesBounded}");
        output.WriteLine($"Algebra runtime: {analysis.Runtime.TotalMilliseconds:F3} ms");
    }

    private static void PrintExploration(Net net, InvariantAnalysis analysis, ExplorationResult result, TextWriter output)
    {
        output.WriteLine($"Exploration: {result.Status}; reachable markings discovered: {result.States.Count}; expanded: {result.ExpandedStates}");
        output.WriteLine($"Firing edges explored: {result.Edges.Count}; terminal markings found: {result.TerminalStates.Count}");
        output.WriteLine($"Discovered conserved quantities: {analysis.Invariants.Count}; invariant violations: {result.Violations.Count}");
        output.WriteLine($"Boundedness certificate: {analysis.ProvesBounded}; invariant search truncated: {analysis.SearchTruncated}");
        output.WriteLine($"Exploration runtime: {result.Runtime.TotalMilliseconds:F3} ms; algebra runtime: {analysis.Runtime.TotalMilliseconds:F3} ms");
        if (result.Status != ExplorationStatus.Complete) output.WriteLine("Partial state space: no completeness or unboundedness conclusion.");
        foreach (var state in result.TerminalStates)
            output.WriteLine($"Terminal s{state} {result.States[state]}; shortest witness: " +
                string.Join(" -> ", result.TraceTo(state).Skip(1).Select(s => net.Transitions[s.Transition!.Value].Id)));
    }

    private static IReadOnlyList<TraceStep> Sequence(Net net, IReadOnlyList<PlaceInvariant> invariants, string text,
        CancellationToken cancellationToken)
    {
        var steps = new List<TraceStep> { new(null, net.Initial) };
        if (text.Length == 0) return steps;
        foreach (var id in text.Split(','))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transition = FindTransition(net, id);
            var next = net.Fire(steps[^1].Marking, transition);
            Verify(invariants, next);
            steps.Add(new TraceStep(transition, next));
        }
        return steps;
    }

    private static int FindTransition(Net net, string id)
    {
        for (var t = 0; t < net.Transitions.Count; t++) if (net.Transitions[t].Id == id) return t;
        throw new ArgumentException($"Unknown transition '{id}'.");
    }

    private static void Verify(IReadOnlyList<PlaceInvariant> invariants, Marking marking)
    {
        if (invariants.Any(i => i.Evaluate(marking) != i.Constant)) throw new InvalidOperationException("Invariant violation after firing.");
    }

    private static void Simulate(Net net, IReadOnlyList<PlaceInvariant> invariants, Dictionary<string, string> options,
        TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        if (options.TryGetValue("--sequence", out var sequence))
        {
            foreach (var step in Sequence(net, invariants, sequence, cancellationToken))
                output.WriteLine($"{(step.Transition is int t ? net.Transitions[t].Id : "initial")}: {step.Marking}");
            output.WriteLine($"Verified {invariants.Count} discovered invariants after every firing.");
            return;
        }
        var marking = net.Initial;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.WriteLine("Marking: " + string.Join(", ", net.Places.Select((p, i) => $"{p.Id}={marking[i]}")));
            var enabled = net.EnabledTransitions(marking);
            if (enabled.Count == 0) { output.WriteLine("Terminal marking: no enabled transitions."); return; }
            output.WriteLine("Enabled: " + string.Join(", ", enabled.Select(t => net.Transitions[t].Id)));
            output.Write("Fire transition ID (or quit): ");
            var id = input.ReadLine();
            if (id is null or "quit") return;
            var selected = enabled.FirstOrDefault(t => net.Transitions[t].Id == id, -1);
            if (selected < 0) { output.WriteLine("Choose an enabled transition ID."); continue; }
            marking = net.Fire(marking, selected);
            Verify(invariants, marking);
            output.WriteLine($"Conservation verified ({invariants.Count} discovered invariants).");
        }
    }
}
