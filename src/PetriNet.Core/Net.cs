using System.Collections.ObjectModel;
using System.Numerics;

namespace PetriNet.Core;

public sealed record Position(double X, double Y);
public sealed record Place(string Id, BigInteger Initial, Position? Position = null);

public sealed class Transition
{
    public string Id { get; }
    public IReadOnlyDictionary<string, BigInteger> Inputs { get; }
    public IReadOnlyDictionary<string, BigInteger> Outputs { get; }
    public Position? Position { get; }

    public Transition(string id, IReadOnlyDictionary<string, BigInteger> inputs,
        IReadOnlyDictionary<string, BigInteger> outputs, Position? position = null)
    {
        Id = id;
        Inputs = new ReadOnlyDictionary<string, BigInteger>(new Dictionary<string, BigInteger>(inputs));
        Outputs = new ReadOnlyDictionary<string, BigInteger>(new Dictionary<string, BigInteger>(outputs));
        Position = position;
    }
}

/// <summary>An immutable token vector with structural equality, suitable as a state-space key.</summary>
public sealed class Marking : IEquatable<Marking>
{
    private readonly BigInteger[] tokens;
    public int Count => tokens.Length;
    public BigInteger this[int place] => tokens[place];

    public Marking(IEnumerable<BigInteger> tokens)
    {
        this.tokens = tokens.ToArray();
        if (this.tokens.Any(x => x < 0))
            throw new ArgumentException("Token counts must be nonnegative.", nameof(tokens));
    }

    public bool Equals(Marking? other) => other is not null && tokens.AsSpan().SequenceEqual(other.tokens);
    public override bool Equals(object? obj) => obj is Marking other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var token in tokens) hash.Add(token);
        return hash.ToHashCode();
    }
    public override string ToString() => $"[{string.Join(", ", tokens)}]";
}

/// <summary>Ordinary weighted P/T net. No capacities, inhibitor arcs, or firing priorities.</summary>
public sealed class Net
{
    private readonly BigInteger[,] pre;
    private readonly BigInteger[,] post;
    public string Name { get; }
    public IReadOnlyList<Place> Places { get; }
    public IReadOnlyList<Transition> Transitions { get; }
    public Marking Initial { get; }

    public Net(string name, IEnumerable<Place> places, IEnumerable<Transition> transitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Places = Array.AsReadOnly(places.ToArray());
        Transitions = Array.AsReadOnly(transitions.ToArray());
        if (Places.Count == 0) throw new ArgumentException("At least one place is required.");
        ValidateIds(Places.Select(p => p.Id), "place");
        ValidateIds(Transitions.Select(t => t.Id), "transition");
        foreach (var p in Places)
        {
            if (p.Initial < 0) throw new ArgumentException($"Negative initial tokens for '{p.Id}'.");
            ValidatePosition(p.Position);
        }
        pre = new BigInteger[Places.Count, Transitions.Count];
        post = new BigInteger[Places.Count, Transitions.Count];
        var indices = Places.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
        for (var t = 0; t < Transitions.Count; t++)
        {
            ValidatePosition(Transitions[t].Position);
            SetArcs(Transitions[t].Inputs, pre, t, indices);
            SetArcs(Transitions[t].Outputs, post, t, indices);
        }
        Initial = new Marking(Places.Select(p => p.Initial));
    }

    private static void ValidateIds(IEnumerable<string> ids, string kind)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
            if (string.IsNullOrWhiteSpace(id) || !unique.Add(id))
                throw new ArgumentException($"Invalid or duplicate {kind} ID '{id}'.");
    }

    private static void ValidatePosition(Position? position)
    {
        if (position is not null && (!double.IsFinite(position.X) || !double.IsFinite(position.Y)))
            throw new ArgumentException("Diagram coordinates must be finite.");
    }

    private static void SetArcs(IReadOnlyDictionary<string, BigInteger> arcs, BigInteger[,] matrix,
        int transition, Dictionary<string, int> indices)
    {
        foreach (var (id, weight) in arcs)
        {
            if (!indices.TryGetValue(id, out var p)) throw new ArgumentException($"Unknown place '{id}'.");
            if (weight <= 0) throw new ArgumentException($"Arc weight for '{id}' must be positive.");
            matrix[p, transition] = weight;
        }
    }

    public BigInteger Input(int place, int transition) => pre[place, transition];
    public BigInteger Output(int place, int transition) => post[place, transition];
    public BigInteger[,] IncidenceMatrix()
    {
        var result = new BigInteger[Places.Count, Transitions.Count];
        for (var p = 0; p < Places.Count; p++)
            for (var t = 0; t < Transitions.Count; t++) result[p, t] = post[p, t] - pre[p, t];
        return result;
    }

    private void ValidateMarking(Marking marking)
    {
        if (marking.Count != Places.Count) throw new ArgumentException("Marking dimension does not match the net.");
    }

    public bool IsEnabled(Marking marking, int transition)
    {
        ValidateMarking(marking);
        if ((uint)transition >= (uint)Transitions.Count) throw new ArgumentOutOfRangeException(nameof(transition));
        for (var p = 0; p < Places.Count; p++)
            if (marking[p] < pre[p, transition]) return false;
        return true;
    }

    public IReadOnlyList<int> EnabledTransitions(Marking marking)
    {
        ValidateMarking(marking);
        return Enumerable.Range(0, Transitions.Count).Where(t => IsEnabled(marking, t)).ToArray();
    }

    public Marking Fire(Marking marking, int transition)
    {
        if (!IsEnabled(marking, transition))
            throw new InvalidOperationException($"Transition '{Transitions[transition].Id}' is not enabled.");
        return new Marking(Enumerable.Range(0, Places.Count)
            .Select(p => marking[p] - pre[p, transition] + post[p, transition]));
    }
}
