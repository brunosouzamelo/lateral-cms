namespace Lateral.CMS.Simulator;

/// <summary>
/// Continuous, random traffic: a stand-in for a busy CMS. Useful for watching the background processor
/// drain the inbox, and for seeing what the service does with re-deliveries and out-of-order batches at
/// a volume no scripted scenario reaches.
/// </summary>
public sealed class StreamGenerator(CmsWebhookClient client, SimulatorOptions options)
{
    public async Task<int> RunAsync(string run, CancellationToken cancellationToken)
    {
        var random = new Random(options.Seed);

        // Each entity keeps its own version counter and clock, so the traffic is a plausible history
        // rather than a pile of unrelated events.
        var versions = new int[options.Entities];
        var clocks = new DateTimeOffset[options.Entities];
        var deleted = new bool[options.Entities];

        var start = DateTimeOffset.UtcNow.AddHours(-1);
        for (var i = 0; i < options.Entities; i++)
            clocks[i] = start;

        Console.WriteLine($"Streaming to {options.Url}/cms/events");
        Console.WriteLine($"  {options.Entities} entities, batches of {options.BatchSize} every {options.Interval.TotalMilliseconds:0}ms, "
            + $"{options.DuplicatePercent}% re-delivered{(options.Shuffle ? ", shuffled" : "")}, seed {options.Seed}");
        Console.WriteLine(options.Events == 0 ? "  running until Ctrl+C" : $"  {options.Events} events");
        Console.WriteLine();

        var sent = 0;
        var accepted = 0;
        var rejected = 0;
        var batches = 0;

        while (!cancellationToken.IsCancellationRequested && (options.Events == 0 || sent < options.Events))
        {
            var remaining = options.Events == 0 ? options.BatchSize : Math.Min(options.BatchSize, options.Events - sent);
            var batch = new List<object>(remaining);

            while (batch.Count < remaining)
            {
                var index = random.Next(options.Entities);
                var id = $"sim-{run}-{index:0000}";

                batch.Add(NextEvent(id, index, versions, clocks, deleted, random));

                // A webhook delivers at least once, so the same event turning up twice is normal traffic.
                if (batch.Count < remaining && random.Next(100) < options.DuplicatePercent)
                    batch.Add(batch[^1]);
            }

            if (options.Shuffle)
                Shuffle(batch, random);

            sent += batch.Count;
            batches++;

            if (options.DryRun)
            {
                Console.WriteLine(CmsWebhookClient.Describe(batch));
            }
            else
            {
                var receipt = await client.SendAsync(batch, $"sim-{run}-b{batches:0000}", cancellationToken);

                accepted += receipt.Accepted;
                rejected += receipt.Rejected;

                Console.WriteLine($"  batch {batches,4}: {receipt.Accepted,3} accepted, {receipt.Rejected,3} rejected  ({sent} events sent)");
            }

            if (options.Interval > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(options.Interval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Sent {sent} events in {batches} batches: {accepted} accepted, {rejected} rejected.");

        if (!options.DryRun)
            Console.WriteLine("The event log at GET /cms/events shows what became of them.");

        return 0;
    }

    private static object NextEvent(
        string id, int index, int[] versions, DateTimeOffset[] clocks, bool[] deleted, Random random)
    {
        // The CMS clock only moves forward for a given entity.
        clocks[index] = clocks[index].AddSeconds(random.Next(1, 120));
        var timestamp = clocks[index];

        if (deleted[index])
        {
            // A deleted identifier comes back as a fresh entity, which is a re-creation, not a resurrection.
            deleted[index] = false;
            versions[index] = 1;

            return CmsEvents.Publish(id, 1, timestamp);
        }

        return random.Next(100) switch
        {
            < 70 => CmsEvents.Publish(id, ++versions[index], timestamp),
            < 90 => CmsEvents.UnPublish(id, ++versions[index], timestamp),
            _ => Delete(id, index, timestamp, deleted)
        };
    }

    private static object Delete(string id, int index, DateTimeOffset timestamp, bool[] deleted)
    {
        deleted[index] = true;
        return CmsEvents.Delete(id, timestamp);
    }

    private static void Shuffle(List<object> batch, Random random)
    {
        for (var i = batch.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (batch[i], batch[j]) = (batch[j], batch[i]);
        }
    }
}
