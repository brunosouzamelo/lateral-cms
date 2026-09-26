namespace Lateral.CMS.Simulator;

/// <summary>
/// Runs the scripted scenarios and checks what the service stored. Every mismatch is reported and counted,
/// and the process ends non-zero when there is one, so a run can be trusted in a pipeline.
/// </summary>
public sealed class ScenarioRunner(CmsWebhookClient client, SimulatorOptions options)
{
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromSeconds(30);

    private int _failures;
    private int _delivery;

    public async Task<int> RunAsync(IReadOnlyList<Scenario> scenarios, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scenarios);

        foreach (var scenario in scenarios)
        {
            Console.WriteLine();
            Console.WriteLine($"── {scenario.Name} {new string('─', Math.Max(1, 60 - scenario.Name.Length))}");
            Console.WriteLine(Wrap(scenario.What));
            Console.WriteLine();

            foreach (var step in scenario.Steps)
                await RunStepAsync(step, cancellationToken);

            if (options.Verify)
                await VerifyAsync(scenario, cancellationToken);
        }

        Console.WriteLine();

        if (!options.Verify)
        {
            Console.WriteLine("Sent. Nothing was checked, because no reader account was given.");
            return 0;
        }

        Console.WriteLine(_failures == 0
            ? $"All {scenarios.Count} scenarios behaved as expected."
            : $"{_failures} check(s) did not match. See the lines marked FAIL above.");

        return _failures == 0 ? 0 : 1;
    }

    private async Task RunStepAsync(ScenarioStep step, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  → {step.Title} ({step.Events.Count} event{(step.Events.Count == 1 ? "" : "s")})");

        if (options.Verbose || options.DryRun)
            Console.WriteLine(Indent(CmsWebhookClient.Describe(step.Events), "      "));

        if (options.DryRun)
            return;

        var correlationId = $"sim-{DateTime.UtcNow:HHmmss}-{++_delivery:000}";
        var receipt = await client.SendAsync(step.Events, correlationId, cancellationToken);

        Console.WriteLine($"      receipt: {receipt.Accepted} accepted, {receipt.Rejected} rejected"
            + $"  (correlation {correlationId})");

        foreach (var rejection in receipt.Rejections)
            Console.WriteLine($"        refused #{rejection.Index}: {string.Join(" | ", rejection.Errors)}");

        if (step.ExpectedRejected is { } expectedRejected)
            Check($"{expectedRejected} event(s) refused", expectedRejected, receipt.Rejected);

        if (!options.Verify)
            return;

        var statuses = await client.WaitForProcessingAsync(receipt.BatchId, ProcessingTimeout, cancellationToken);
        var applied = statuses.Where(s => s.Key != "Rejected").ToList();

        Console.WriteLine($"      processed: {string.Join(", ", statuses.Select(s => $"{s.Value} {s.Key}"))}");

        if (step.ExpectedStatus is { } expected && applied.Count > 0)
            Check($"accepted events end as {expected}", expected, string.Join("+", applied.Select(s => s.Key).Order()));
    }

    private async Task VerifyAsync(Scenario scenario, CancellationToken cancellationToken)
    {
        foreach (var expectation in scenario.Expectations)
        {
            var entity = await client.GetEntityAsync(expectation.EntityId, cancellationToken);

            if (!expectation.ShouldExist)
            {
                Check("the entity is gone", true, entity is null);
                continue;
            }

            if (entity is null)
            {
                Fail("the entity is stored", "nothing stored");
                continue;
            }

            if (expectation.Version is { } version)
                Check("version", version, entity.Version);

            if (expectation.LastPublishedVersion is { } published)
                Check("last published version", published, entity.LastPublishedVersion);

            if (expectation.Status is { } status)
                Check("status", status, entity.Status);
        }
    }

    private void Check<T>(string what, T expected, T actual)
    {
        if (Equals(expected, actual))
            Console.WriteLine($"      OK   {what}: {actual}");
        else
            Fail(what, $"expected {expected}, got {actual}");
    }

    private void Fail(string what, string detail)
    {
        _failures++;
        Console.WriteLine($"      FAIL {what}: {detail}");
    }

    private static string Indent(string text, string prefix)
        => string.Join(Environment.NewLine, text.Split('\n').Select(line => prefix + line.TrimEnd('\r')));

    private static string Wrap(string text, int width = 96)
    {
        var lines = new List<string>();
        var line = new System.Text.StringBuilder("   ");

        foreach (var word in text.Split(' '))
        {
            if (line.Length + word.Length + 1 > width)
            {
                lines.Add(line.ToString());
                line = new System.Text.StringBuilder("   ");
            }

            line.Append(word).Append(' ');
        }

        lines.Add(line.ToString());

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }
}
