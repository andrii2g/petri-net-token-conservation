using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using PetriNet.Cli;
using PetriNet.Core;

namespace PetriNet.Tests;

public class CliTests
{
    private static (int Code, string Output, string Error) Run(string[] args, string input = "")
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var code = Commands.Run(args, new StringReader(input), output, error);
        return (code, output.ToString(), error.ToString());
    }

    private static string Example(string name) => Path.Combine(AppContext.BaseDirectory, "examples", name + ".json");

    [Theory]
    [InlineData("validate")]
    [InlineData("invariants")]
    [InlineData("explore")]
    public void ReadOnlyCommandsRunAgainstExample(string command)
    {
        var result = Run([command, Example("semaphore")]);
        Assert.Equal(0, result.Code);
        Assert.Empty(result.Error);
        Assert.Contains("Two workers", result.Output);
    }

    [Fact]
    public void InvalidOptionsAndDisabledSequencesReturnErrors()
    {
        Assert.Equal(1, Run(["explore", Example("semaphore"), "--max-states", "0"]).Code);
        Assert.Equal(1, Run(["explore", Example("semaphore"), "--typo", "1"]).Code);
        Assert.Equal(1, Run(["render", Example("semaphore")]).Code);
        Assert.Equal(1, Run(["simulate", Example("semaphore"), "--sequence", "acquireA,acquireB"]).Code);
    }

    [Fact]
    public void InteractiveSimulationRejectsDisabledFiringThenAcceptsRelease()
    {
        var result = Run(["simulate", Example("semaphore")], "acquireA\nacquireB\nreleaseA\nquit\n");
        Assert.Equal(0, result.Code);
        Assert.Contains("Choose an enabled", result.Output);
        Assert.Equal(2, result.Output.Split("Conservation verified").Length - 1);
    }

    [Fact]
    public void PartialReportNeverClaimsCompleteStateSpaceSize()
    {
        var directory = Path.Combine(Path.GetTempPath(), "petri-test-" + Guid.NewGuid());
        var report = Path.Combine(directory, "report.json");
        try
        {
            var result = Run(["explore", Example("unbounded"), "--max-states", "5", "--report", report]);
            Assert.Equal(2, result.Code);
            using var json = JsonDocument.Parse(File.ReadAllText(report));
            Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("metrics").GetProperty("stateSpaceSize").ValueKind);
            Assert.Equal(5, json.RootElement.GetProperty("states").GetArrayLength());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void RenderWritesParseableArtifactsAndDeadlockWitness()
    {
        var directory = Path.Combine(Path.GetTempPath(), "petri-test-" + Guid.NewGuid());
        try
        {
            var result = Run(["render", Example("resource-deadlock"), "--out", directory]);
            Assert.Equal(0, result.Code);
            foreach (var name in new[] { "net.svg", "timeline.svg", "reachability.svg" })
                Assert.Equal("{http://www.w3.org/2000/svg}svg", XDocument.Load(Path.Combine(directory, name)).Root!.Name.ToString());
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "report.json")));
            var witness = json.RootElement.GetProperty("terminalWitnesses")[0].GetProperty("transitions");
            Assert.Equal(new[] { "takeR1A", "takeR2B" }, witness.EnumerateArray().Select(x => x.GetString()));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void SvgEscapesUserLabelsAndUsesCultureIndependentCoordinates()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var net = new Net("<script>& test", [new Place("p<&", 1, new Position(12.5, 50))], []);
            var document = SvgRenderer.Net(net, net.Initial);
            var text = document.ToString();
            Assert.Contains("&lt;script&gt;&amp; test", text);
            Assert.DoesNotContain("<script>", text);
            Assert.NotNull(XDocument.Parse(text).Root);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
