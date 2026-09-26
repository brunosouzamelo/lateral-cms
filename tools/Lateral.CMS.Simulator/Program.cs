using Lateral.CMS.Simulator;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(SimulatorOptions.Help);
    return 0;
}

if (!SimulatorOptions.TryParse(args, out var options, out var error))
{
    Console.Error.WriteLine($"lateral-cms-simulator: {error}");
    Console.Error.WriteLine("Run with --help for the options.");
    return 2;
}

// Ctrl+C ends the run tidily instead of killing it mid-batch.
using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    Console.WriteLine();
    Console.WriteLine("Stopping...");
    cancellation.Cancel();
};

// Identifiers carry the moment the run started, so repeated runs never collide in the same database.
var run = DateTimeOffset.UtcNow.ToString("MMddHHmmss");

using var client = new CmsWebhookClient(options);

try
{
    if (!options.DryRun)
        await client.EnsureReachableAsync(cancellation.Token);

    if (options.Stream)
        return await new StreamGenerator(client, options).RunAsync(run, cancellation.Token);

    var scenarios = Scenarios.All(run);

    if (!string.Equals(options.Scenario, "all", StringComparison.OrdinalIgnoreCase))
    {
        scenarios = [.. scenarios.Where(s => string.Equals(s.Name, options.Scenario, StringComparison.OrdinalIgnoreCase))];

        if (scenarios.Count == 0)
        {
            Console.Error.WriteLine($"lateral-cms-simulator: no scenario called '{options.Scenario}'.");
            Console.Error.WriteLine("Run with --help for the list.");
            return 2;
        }
    }

    Console.WriteLine($"Simulating the CMS against {options.Url}");
    Console.WriteLine(options.Verify
        ? $"Checking the result as '{options.ReaderUser}'. Run identifier: {run}."
        : $"Sending only; nothing will be checked. Run identifier: {run}.");

    return await new ScenarioRunner(client, options).RunAsync(scenarios, cancellation.Token);
}
catch (SimulatorException exception)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"lateral-cms-simulator: {exception.Message}");
    return 1;
}
catch (OperationCanceledException)
{
    return 0;
}
