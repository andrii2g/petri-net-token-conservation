using System.Diagnostics;
using System.Numerics;

namespace PetriNet.Core;

public sealed class PlaceInvariant
{
    public IReadOnlyList<BigInteger> Weights { get; }
    public BigInteger Constant { get; }

    internal PlaceInvariant(BigInteger[] weights, Marking initial)
    {
        Weights = Array.AsReadOnly((BigInteger[])weights.Clone());
        Constant = Evaluate(initial);
    }

    public BigInteger Evaluate(Marking marking)
    {
        if (marking.Count != Weights.Count) throw new ArgumentException("Invariant dimension mismatch.");
        var total = BigInteger.Zero;
        for (var p = 0; p < Weights.Count; p++) total += Weights[p] * marking[p];
        return total;
    }

    public string Equation(Net net) => string.Join(" + ", Weights.Select((w, i) => (w, i))
        .Where(x => x.w > 0).Select(x => x.w.IsOne ? net.Places[x.i].Id : $"{x.w}*{net.Places[x.i].Id}")) + $" = {Constant}";
}

public sealed record InvariantOptions(int CoefficientBound = 2, long MaxCombinations = 100_000);
public sealed record InvariantAnalysis(
    IReadOnlyList<IReadOnlyList<BigInteger>> IntegerNullspaceBasis,
    IReadOnlyList<PlaceInvariant> Invariants,
    IReadOnlyList<BigInteger?> PlaceBounds,
    long CombinationsTried,
    bool SearchTruncated,
    TimeSpan Runtime,
    InvariantOptions Options)
{
    public bool ProvesBounded => PlaceBounds.All(b => b.HasValue);
}

public static class InvariantAnalyzer
{
    /// <summary>RREF over exact rationals; one nullspace vector per free column.</summary>
    public static IReadOnlyList<IReadOnlyList<Rational>> Nullspace(BigInteger[,] matrix,
        CancellationToken cancellationToken = default)
    {
        var rows = matrix.GetLength(0);
        var columns = matrix.GetLength(1);
        var a = new Rational[rows, columns];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++) a[r, c] = matrix[r, c];
        var pivots = new List<int>();
        for (var c = 0; c < columns && pivots.Count < rows; c++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = pivots.Count;
            var pivot = row;
            while (pivot < rows && a[pivot, c].IsZero) pivot++;
            if (pivot == rows) continue;
            for (var j = 0; j < columns; j++) (a[row, j], a[pivot, j]) = (a[pivot, j], a[row, j]);
            var scale = a[row, c];
            for (var j = 0; j < columns; j++) a[row, j] /= scale;
            for (var r = 0; r < rows; r++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (r == row || a[r, c].IsZero) continue;
                var factor = a[r, c];
                for (var j = 0; j < columns; j++) a[r, j] -= factor * a[row, j];
            }
            pivots.Add(c);
        }
        var basis = new List<IReadOnlyList<Rational>>();
        for (var free = 0; free < columns; free++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pivots.Contains(free)) continue;
            var vector = new Rational[columns];
            vector[free] = 1;
            for (var r = 0; r < pivots.Count; r++) vector[pivots[r]] = -a[r, free];
            basis.Add(Array.AsReadOnly(vector));
        }
        return basis.AsReadOnly();
    }

    public static InvariantAnalysis Analyze(Net net, InvariantOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new InvariantOptions();
        if (options.CoefficientBound < 0 || options.CoefficientBound > 100)
            throw new ArgumentOutOfRangeException(nameof(options), "Coefficient bound must be between 0 and 100.");
        if (options.MaxCombinations < 1) throw new ArgumentOutOfRangeException(nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        var incidence = net.IncidenceMatrix();
        var transpose = new BigInteger[net.Transitions.Count, net.Places.Count];
        for (var p = 0; p < net.Places.Count; p++)
            for (var t = 0; t < net.Transitions.Count; t++) transpose[t, p] = incidence[p, t];
        var basis = Nullspace(transpose, cancellationToken).Select(Primitive).ToArray();
        var found = new Dictionary<string, PlaceInvariant>(StringComparer.Ordinal);

        void Consider(BigInteger[] candidate)
        {
            if (candidate.All(x => x <= 0)) candidate = candidate.Select(x => -x).ToArray();
            if (candidate.Any(x => x < 0) || candidate.All(x => x.IsZero)) return;
            candidate = Normalize(candidate);
            if (!IsInvariant(net, candidate)) throw new InvalidOperationException("Invalid nullspace candidate.");
            found.TryAdd(string.Join(",", candidate), new PlaceInvariant(candidate, net.Initial));
        }

        foreach (var vector in basis) Consider(vector);
        // Enumerate a bounded coefficient box iteratively to avoid recursion on large nullspaces.
        var coefficients = Enumerable.Repeat(-options.CoefficientBound, basis.Length).ToArray();
        long tried = 0;
        var done = basis.Length == 0 || options.CoefficientBound == 0;
        while (!done && tried < options.MaxCombinations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = new BigInteger[net.Places.Count];
            for (var b = 0; b < basis.Length; b++)
                for (var p = 0; p < vector.Length; p++) vector[p] += coefficients[b] * basis[b][p];
            Consider(vector);
            tried++;
            var digit = 0;
            while (digit < coefficients.Length && coefficients[digit] == options.CoefficientBound)
                coefficients[digit++] = -options.CoefficientBound;
            if (digit == coefficients.Length) done = true;
            else coefficients[digit]++;
        }
        var invariants = found.Values.OrderBy(x => x.Weights.Aggregate(BigInteger.Zero, (a, b) => a + b))
            .ThenBy(x => string.Join(",", x.Weights), StringComparer.Ordinal).ToArray();
        var bounds = new BigInteger?[net.Places.Count];
        foreach (var invariant in invariants)
            for (var p = 0; p < bounds.Length; p++)
                if (invariant.Weights[p] > 0)
                {
                    var bound = invariant.Constant / invariant.Weights[p];
                    if (!bounds[p].HasValue || bound < bounds[p]!.Value) bounds[p] = bound;
                }
        return new InvariantAnalysis(Array.AsReadOnly(basis.Select(v => (IReadOnlyList<BigInteger>)Array.AsReadOnly(v)).ToArray()),
            Array.AsReadOnly(invariants), Array.AsReadOnly(bounds), tried, !done, watch.Elapsed, options);
    }

    public static bool IsInvariant(Net net, IReadOnlyList<BigInteger> weights)
    {
        if (weights.Count != net.Places.Count) return false;
        for (var t = 0; t < net.Transitions.Count; t++)
        {
            var sum = BigInteger.Zero;
            for (var p = 0; p < net.Places.Count; p++) sum += (net.Output(p, t) - net.Input(p, t)) * weights[p];
            if (!sum.IsZero) return false;
        }
        return true;
    }

    private static BigInteger[] Primitive(IReadOnlyList<Rational> vector)
    {
        var lcm = BigInteger.One;
        foreach (var value in vector)
            lcm = lcm / BigInteger.GreatestCommonDivisor(lcm, value.Denominator) * value.Denominator;
        return Normalize(vector.Select(x => x.Numerator * (lcm / x.Denominator)).ToArray());
    }

    private static BigInteger[] Normalize(BigInteger[] vector)
    {
        var gcd = vector.Aggregate(BigInteger.Zero, (g, x) => BigInteger.GreatestCommonDivisor(g, BigInteger.Abs(x)));
        return gcd.IsZero ? vector : vector.Select(x => x / gcd).ToArray();
    }
}
