using System.Numerics;
using PetriNet.Core;

namespace PetriNet.Tests;

public class ExplorationTests
{
    internal static Net Example(string name) => NetJson.Load(Path.Combine(AppContext.BaseDirectory, "examples", name + ".json"));

    [Theory]
    [InlineData("semaphore", 3, 4, 0)]
    [InlineData("producer-consumer", 4, 6, 0)]
    [InlineData("resource-sharing", 3, 4, 0)]
    [InlineData("resource-deadlock", 6, 8, 1)]
    public void BoundedExamplesHaveExpectedGraphs(string name, int states, int edges, int terminals)
    {
        var net = Example(name);
        var analysis = InvariantAnalyzer.Analyze(net);
        var result = Explorer.Explore(net, analysis.Invariants);
        Assert.True(analysis.ProvesBounded);
        Assert.Equal(ExplorationStatus.Complete, result.Status);
        Assert.Equal(states, result.States.Count);
        Assert.Equal(edges, result.Edges.Count);
        Assert.Equal(terminals, result.TerminalStates.Count);
        Assert.Empty(result.Violations);
        Assert.Equal(states, result.ExpandedStates);
        foreach (var edge in result.Edges)
            Assert.Equal(result.States[edge.To], net.Fire(result.States[edge.From], edge.Transition));
    }

    [Fact]
    public void DeadlockHasShortestWitnessWhileResourcesRemainConserved()
    {
        var net = Example("resource-deadlock");
        var analysis = InvariantAnalyzer.Analyze(net);
        var result = Explorer.Explore(net, analysis.Invariants);
        var terminal = Assert.Single(result.TerminalStates);
        var trace = result.TraceTo(terminal);
        Assert.Equal(3, trace.Count);
        Assert.Empty(net.EnabledTransitions(trace[^1].Marking));
        Assert.Contains(analysis.Invariants, i => i.Weights.SequenceEqual(new BigInteger[] { 1, 0, 0, 1, 1, 0, 0, 1 }));
        Assert.All(analysis.Invariants, i => Assert.All(trace, s => Assert.Equal(i.Constant, i.Evaluate(s.Marking))));
    }

    [Fact]
    public void StateLimitDoesNotPretendToProveUnboundednessOrCompleteness()
    {
        var net = Example("unbounded");
        var result = Explorer.Explore(net, [], new ExplorationOptions(MaxStates: 5));
        Assert.Equal(ExplorationStatus.StateLimit, result.Status);
        Assert.Equal(5, result.States.Count);
        Assert.Equal(4, result.Edges.Count);
        Assert.Empty(result.TerminalStates);
    }

    [Fact]
    public void RevisitedStatesAndSelfLoopsRetainEdges()
    {
        var net = SemanticsTests.Parse("""[{"id":"p","initial":1}]""",
            """[{"id":"loop","inputs":{"p":1},"outputs":{"p":1}}]""");
        var result = Explorer.Explore(net, [], new ExplorationOptions(1, 1));
        Assert.Equal(ExplorationStatus.Complete, result.Status);
        Assert.Single(result.States);
        Assert.Equal(new FiringEdge(0, 0, 0), Assert.Single(result.Edges));
    }

    [Fact]
    public void EdgeLimitAndCancellationHaveDistinctStatuses()
    {
        var net = Example("semaphore");
        Assert.Equal(ExplorationStatus.EdgeLimit, Explorer.Explore(net, [], new ExplorationOptions(MaxEdges: 1)).Status);
        Assert.Equal(ExplorationStatus.Cancelled, Explorer.Explore(net, [], cancellationToken: new CancellationToken(true)).Status);
        Assert.Equal(ExplorationStatus.TimeLimit,
            Explorer.Explore(net, [], new ExplorationOptions(TimeLimit: TimeSpan.FromTicks(1))).Status);
    }

    [Fact]
    public void NetWithNoTransitionsHasAnInitialTerminalMarking()
    {
        var net = SemanticsTests.Parse("""[{"id":"p","initial":1}]""", "[]");
        var result = Explorer.Explore(net, InvariantAnalyzer.Analyze(net).Invariants);
        Assert.Equal(0, Assert.Single(result.TerminalStates));
        Assert.Single(result.TraceTo(0));
    }
}
