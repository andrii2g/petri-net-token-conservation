using System.Text.Json;
using PetriNet.Core;

namespace PetriNet.Cli;

internal static class Reports
{
    public static void Write(string path, Net net, InvariantAnalysis analysis, ExplorationResult result)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var c = net.IncidenceMatrix();
        var report = new
        {
            schemaVersion = 1,
            net = net.Name,
            placeOrder = net.Places.Select(p => p.Id),
            transitionOrder = net.Transitions.Select(t => t.Id),
            incidence = Enumerable.Range(0, net.Places.Count).Select(p => Enumerable.Range(0, net.Transitions.Count).Select(t => c[p, t].ToString())),
            status = result.Status.ToString(),
            complete = result.Status == ExplorationStatus.Complete,
            metrics = new
            {
                reachableMarkings = result.States.Count,
                expandedStates = result.ExpandedStates,
                transitionsExplored = result.Edges.Count,
                conservedQuantities = analysis.Invariants.Count,
                invariantViolations = result.Violations.Count,
                terminalMarkings = result.TerminalStates.Count,
                stateSpaceSize = result.Status == ExplorationStatus.Complete ? (int?)result.States.Count : null,
                explorationMilliseconds = result.Runtime.TotalMilliseconds,
                algebraMilliseconds = analysis.Runtime.TotalMilliseconds
            },
            invariantSearch = new
            {
                method = "bounded integer combinations of an exact rational nullspace basis",
                completeInvariantEnumeration = false,
                coefficientBound = analysis.Options.CoefficientBound,
                maxCombinations = analysis.Options.MaxCombinations,
                combinationsTried = analysis.CombinationsTried,
                truncated = analysis.SearchTruncated
            },
            nullspaceBasis = analysis.IntegerNullspaceBasis.Select(v => v.Select(x => x.ToString())),
            invariants = analysis.Invariants.Select(i => new
            {
                weights = i.Weights.Select(w => w.ToString()),
                constant = i.Constant.ToString(),
                equation = i.Equation(net)
            }),
            provesBounded = analysis.ProvesBounded,
            placeBounds = analysis.PlaceBounds.Select(b => b?.ToString()),
            states = result.States.Select((s, id) => new { id, tokens = Enumerable.Range(0, s.Count).Select(p => s[p].ToString()) }),
            edges = result.Edges.Select(e => new { from = e.From, transition = net.Transitions[e.Transition].Id, to = e.To }),
            terminalStates = result.TerminalStates,
            terminalWitnesses = result.TerminalStates.Select(s => new
            {
                state = s,
                transitions = result.TraceTo(s).Skip(1).Select(t => net.Transitions[t.Transition!.Value].Id)
            }),
            violations = result.Violations
        };
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
