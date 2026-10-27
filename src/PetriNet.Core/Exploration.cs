using System.Diagnostics;

namespace PetriNet.Core;

public enum ExplorationStatus { Complete, StateLimit, EdgeLimit, TimeLimit, Cancelled }
public sealed record ExplorationOptions(int MaxStates = 10_000, int MaxEdges = 100_000, TimeSpan? TimeLimit = null);
public sealed record FiringEdge(int From, int Transition, int To);
public sealed record InvariantViolation(int Edge, int Invariant, string Expected, string Actual);
public sealed record TraceStep(int? Transition, Marking Marking);

public sealed class ExplorationResult
{
    public IReadOnlyList<Marking> States { get; }
    public IReadOnlyList<FiringEdge> Edges { get; }
    public IReadOnlyList<int> TerminalStates { get; }
    public IReadOnlyList<InvariantViolation> Violations { get; }
    public ExplorationStatus Status { get; }
    public TimeSpan Runtime { get; }
    public int ExpandedStates { get; }
    private readonly IReadOnlyList<int?> parentEdges;

    internal ExplorationResult(List<Marking> states, List<FiringEdge> edges, List<int> terminals,
        List<InvariantViolation> violations, List<int?> parents, ExplorationStatus status, TimeSpan runtime, int expanded)
    {
        States = states.AsReadOnly();
        Edges = edges.AsReadOnly();
        TerminalStates = terminals.AsReadOnly();
        Violations = violations.AsReadOnly();
        parentEdges = parents.AsReadOnly();
        Status = status;
        Runtime = runtime;
        ExpandedStates = expanded;
    }

    /// <summary>A shortest firing trace from the initial marking to a discovered state.</summary>
    public IReadOnlyList<TraceStep> TraceTo(int state)
    {
        if ((uint)state >= (uint)States.Count) throw new ArgumentOutOfRangeException(nameof(state));
        var steps = new List<TraceStep>();
        while (parentEdges[state] is int edgeId)
        {
            var edge = Edges[edgeId];
            steps.Add(new TraceStep(edge.Transition, States[state]));
            state = edge.From;
        }
        steps.Add(new TraceStep(null, States[0]));
        steps.Reverse();
        return steps.AsReadOnly();
    }
}

public static class Explorer
{
    public static ExplorationResult Explore(Net net, IReadOnlyList<PlaceInvariant> invariants,
        ExplorationOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ExplorationOptions();
        if (options.MaxStates < 1 || options.MaxEdges < 1 || options.TimeLimit is { Ticks: <= 0 })
            throw new ArgumentOutOfRangeException(nameof(options), "Exploration limits must be positive.");
        foreach (var invariant in invariants)
            if (!InvariantAnalyzer.IsInvariant(net, invariant.Weights) || invariant.Evaluate(net.Initial) != invariant.Constant)
                throw new ArgumentException("Invariants must be valid for this net and initial marking.", nameof(invariants));
        var watch = Stopwatch.StartNew();
        var states = new List<Marking> { net.Initial };
        var indices = new Dictionary<Marking, int> { [net.Initial] = 0 };
        var parents = new List<int?> { null };
        var edges = new List<FiringEdge>();
        var terminals = new List<int>();
        var violations = new List<InvariantViolation>();
        var expanded = 0;

        ExplorationResult Finish(ExplorationStatus status) =>
            new(states, edges, terminals, violations, parents, status, watch.Elapsed, expanded);
        ExplorationStatus? StopReason() => cancellationToken.IsCancellationRequested ? ExplorationStatus.Cancelled :
            options.TimeLimit.HasValue && watch.Elapsed >= options.TimeLimit ? ExplorationStatus.TimeLimit : null;

        // Discovery order is breadth first; states doubles as the queue.
        for (var from = 0; from < states.Count; from++)
        {
            if (StopReason() is { } reason) return Finish(reason);
            var enabled = net.EnabledTransitions(states[from]);
            if (enabled.Count == 0) terminals.Add(from);
            foreach (var transition in enabled)
            {
                if (StopReason() is { } innerReason) return Finish(innerReason);
                if (edges.Count >= options.MaxEdges) return Finish(ExplorationStatus.EdgeLimit);
                var next = net.Fire(states[from], transition);
                var isNew = !indices.TryGetValue(next, out var to);
                if (isNew && states.Count >= options.MaxStates) return Finish(ExplorationStatus.StateLimit);
                for (var i = 0; i < invariants.Count; i++)
                {
                    var value = invariants[i].Evaluate(next);
                    if (value != invariants[i].Constant)
                        violations.Add(new InvariantViolation(edges.Count, i, invariants[i].Constant.ToString(), value.ToString()));
                }
                if (isNew)
                {
                    to = states.Count;
                    indices.Add(next, to);
                    states.Add(next);
                    parents.Add(edges.Count);
                }
                edges.Add(new FiringEdge(from, transition, to));
            }
            expanded++;
        }
        return Finish(ExplorationStatus.Complete);
    }
}
