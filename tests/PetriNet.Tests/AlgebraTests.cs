using System.Numerics;
using PetriNet.Core;

namespace PetriNet.Tests;

public class AlgebraTests
{
    [Fact]
    public void RationalArithmeticNormalizesAndHandlesDefaultZero()
    {
        Assert.Equal(new Rational(-1, 2), new Rational(2, -4));
        Assert.Equal(new Rational(5, 6), new Rational(1, 2) + new Rational(1, 3));
        Assert.Equal((Rational)0, default(Rational));
        Assert.Throws<DivideByZeroException>(() => new Rational(1, 0));
    }

    [Fact]
    public void WeightedConservationUsesPrimitiveIntegerWeights()
    {
        var net = SemanticsTests.Parse("""[{"id":"a","initial":4},{"id":"b","initial":0}]""",
            """[{"id":"t","inputs":{"a":2},"outputs":{"b":3}}]""");
        var invariant = Assert.Single(InvariantAnalyzer.Analyze(net).Invariants);
        Assert.Equal(new BigInteger[] { 3, 2 }, invariant.Weights);
        Assert.Equal(new BigInteger(12), invariant.Constant);
        Assert.Equal(invariant.Constant, invariant.Evaluate(net.Fire(net.Initial, 0)));
    }

    [Fact]
    public void MixedSignBasisCombinationsRecoverNonnegativeInvariant()
    {
        // y0 + y1 - y2 = 0 gives basis (-1,1,0), (1,0,1).
        var net = SemanticsTests.Parse("""[{"id":"a","initial":0},{"id":"b","initial":0},{"id":"c","initial":1}]""",
            """[{"id":"t","inputs":{"c":1},"outputs":{"a":1,"b":1}}]""");
        var analysis = InvariantAnalyzer.Analyze(net);
        Assert.Contains(analysis.Invariants, x => x.Weights.SequenceEqual(new BigInteger[] { 0, 1, 1 }));
        Assert.True(analysis.ProvesBounded);
        Assert.All(analysis.Invariants, x => Assert.True(InvariantAnalyzer.IsInvariant(net, x.Weights)));
    }

    [Fact]
    public void SearchLimitsAreExplicitAndBasisCandidatesAreStillChecked()
    {
        var net = SemanticsTests.Parse("""[{"id":"a","initial":1},{"id":"b","initial":2}]""", "[]");
        var result = InvariantAnalyzer.Analyze(net, new InvariantOptions(2, 1));
        Assert.True(result.SearchTruncated);
        Assert.Equal(1, result.CombinationsTried);
        Assert.True(result.ProvesBounded);
    }

    [Fact]
    public void NullspaceHandlesRowSwapsAndFullRank()
    {
        Assert.Empty(InvariantAnalyzer.Nullspace(new BigInteger[,] { { 0, 1 }, { 1, 0 } }));
        var basis = InvariantAnalyzer.Nullspace(new BigInteger[,] { { 0, 2, -3 }, { 0, 4, -6 } });
        Assert.Equal(2, basis.Count);
        Assert.All(basis, v => Assert.Equal((Rational)0, 2 * v[1] - 3 * v[2]));
    }

    [Fact]
    public void GeneratingTokensHasNoNonzeroPlaceInvariant()
    {
        var net = SemanticsTests.Parse("""[{"id":"p","initial":0}]""",
            """[{"id":"create","inputs":{},"outputs":{"p":1}}]""");
        var result = InvariantAnalyzer.Analyze(net);
        Assert.Empty(result.Invariants);
        Assert.False(result.ProvesBounded);
    }
}
