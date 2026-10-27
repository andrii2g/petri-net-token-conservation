using PetriNet.Cli;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
return Commands.Run(args, Console.In, Console.Out, Console.Error, cancellation.Token);
