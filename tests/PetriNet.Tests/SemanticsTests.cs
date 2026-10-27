using System.Numerics;
using PetriNet.Core;

namespace PetriNet.Tests;

public class SemanticsTests
{
    internal static Net Parse(string places, string transitions) => NetJson.Parse(
        "{\"name\":\"test\",\"places\":" + places + ",\"transitions\":" + transitions + "}");

    [Fact]
    public void WeightedArcsFireAtomicallyAndLeaveOriginalUnchanged()
    {
        var net = Parse("""[{"id":"a","initial":3},{"id":"b","initial":0}]""",
            """[{"id":"move","inputs":{"a":2},"outputs":{"b":3}}]""");
        var next = net.Fire(net.Initial, 0);
        Assert.Equal(new Marking([1, 3]), next);
        Assert.Equal(new Marking([3, 0]), net.Initial);
        Assert.False(net.IsEnabled(next, 0));
        Assert.Throws<InvalidOperationException>(() => net.Fire(next, 0));
        Assert.Equal(new BigInteger(-2), net.IncidenceMatrix()[0, 0]);
    }

    [Fact]
    public void SelfLoopRequiresInputEvenThoughItsIncidenceIsZero()
    {
        var net = Parse("""[{"id":"p","initial":0}]""",
            """[{"id":"loop","inputs":{"p":1},"outputs":{"p":1}}]""");
        Assert.Equal(BigInteger.Zero, net.IncidenceMatrix()[0, 0]);
        Assert.False(net.IsEnabled(net.Initial, 0));
        var marked = new Marking([1]);
        Assert.Equal(marked, net.Fire(marked, 0));
    }

    [Theory]
    [InlineData("""[{"id":"p","initial":-1}]""", "[]")]
    [InlineData("""[{"id":"p","initial":0},{"id":"p","initial":1}]""", "[]")]
    [InlineData("""[{"id":"p","initial":0}]""", """[{"id":"t","inputs":{"missing":1},"outputs":{}}]""")]
    [InlineData("""[{"id":"p","initial":0}]""", """[{"id":"t","inputs":{"p":0},"outputs":{}}]""")]
    public void InvalidNetsAreRejected(string places, string transitions) =>
        Assert.Throws<ArgumentException>(() => Parse(places, transitions));

    [Theory]
    [InlineData("""{"name":"n","places":[{"id":"p","initial":0}],"transitions":[],"typo":true}""")]
    [InlineData("""{"name":"n","places":[{"id":"p","initial":0.5}],"transitions":[]}""")]
    [InlineData("""{"name":"n","places":[{"id":"p"}],"transitions":[]}""")]
    [InlineData("""{"name":"n","name":"duplicate","places":[],"transitions":[]}""")]
    public void MalformedDocumentsAreRejected(string json) =>
        Assert.Throws<System.Text.Json.JsonException>(() => NetJson.Parse(json));

    [Fact]
    public void MarkingsCopyTheirInputAndSupportStructuralHashing()
    {
        BigInteger[] input = [1, 2];
        var marking = new Marking(input);
        input[0] = 99;
        Assert.Contains(new Marking([1, 2]), new HashSet<Marking> { marking });
    }

    [Fact]
    public void TokensAreNotLimitedToMachineIntegers()
    {
        var net = Parse("""[{"id":"p","initial":999999999999999999999999999999}]""", "[]");
        Assert.Equal(BigInteger.Parse("999999999999999999999999999999"), net.Initial[0]);
    }
}
