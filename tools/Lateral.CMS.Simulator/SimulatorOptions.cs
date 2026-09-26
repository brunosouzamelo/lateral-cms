namespace Lateral.CMS.Simulator;

/// <summary>How the simulator was asked to run.</summary>
public class SimulatorOptions
{
    /// <summary>Base address of the service, without a path.</summary>
    public string Url { get; private set; } = "http://localhost:49699";

    /// <summary>Credentials of the organization account, which is the only one allowed to push events.</summary>
    public string User { get; private set; } = "cms-webhook-client";

    /// <summary>Password of <see cref="User"/>.</summary>
    public string Password { get; private set; } = "fd23cb16-5bd5-4334-9115-b9a0ebde5a3d";

    /// <summary>
    /// An account allowed to read content. Used only to check what the service made of the events; leave
    /// it empty to send without verifying.
    /// </summary>
    public string ReaderUser { get; private set; } = "content-admin-root";

    /// <summary>Password of <see cref="ReaderUser"/>.</summary>
    public string ReaderPassword { get; private set; } = "226b714d-e4bf-4656-859b-67c0950e23ff";

    /// <summary>Which scenario to run, or <c>all</c>. Ignored in stream mode.</summary>
    public string Scenario { get; private set; } = "all";

    /// <summary>Sends continuous random traffic instead of the scripted scenarios.</summary>
    public bool Stream { get; private set; }

    /// <summary>Stream mode: how many entities to spread the events over.</summary>
    public int Entities { get; private set; } = 20;

    /// <summary>Stream mode: how many events to send in total. Zero runs until interrupted.</summary>
    public int Events { get; private set; } = 200;

    /// <summary>Stream mode: how many events to put in one request.</summary>
    public int BatchSize { get; private set; } = 10;

    /// <summary>Stream mode: pause between requests.</summary>
    public TimeSpan Interval { get; private set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Stream mode: percentage of events re-delivered a second time, as a webhook would.</summary>
    public int DuplicatePercent { get; private set; } = 10;

    /// <summary>Stream mode: shuffles each batch, so events arrive out of the order they happened in.</summary>
    public bool Shuffle { get; private set; }

    /// <summary>Makes a run reproducible. Any value; the same one produces the same traffic.</summary>
    public int Seed { get; private set; } = Environment.TickCount;

    /// <summary>Prints the batches instead of sending them.</summary>
    public bool DryRun { get; private set; }

    /// <summary>Prints every batch as it is sent.</summary>
    public bool Verbose { get; private set; }

    /// <summary>Whether the run should check what the service stored. False when no reader account is given.</summary>
    public bool Verify => !string.IsNullOrWhiteSpace(ReaderUser) && !DryRun;

    /// <summary>
    /// Reads the command line. Unknown or malformed arguments stop the run rather than being ignored,
    /// so a mistyped option never looks like a passing simulation.
    /// </summary>
    public static bool TryParse(string[] args, out SimulatorOptions options, out string? error)
    {
        options = new SimulatorOptions();
        error = null;

        for (var index = 0; index < args.Length; index++)
        {
            var name = args[index];

            switch (name)
            {
                case "--stream": options.Stream = true; continue;
                case "--shuffle": options.Shuffle = true; continue;
                case "--dry-run": options.DryRun = true; continue;
                case "--verbose" or "-v": options.Verbose = true; continue;
                case "--no-verify": options.ReaderUser = string.Empty; continue;
            }

            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unexpected argument '{name}'.";
                return false;
            }

            if (index + 1 >= args.Length)
            {
                error = $"Option '{name}' needs a value.";
                return false;
            }

            var value = args[++index];

            switch (name)
            {
                case "--url": options.Url = value.TrimEnd('/'); break;
                case "--user": options.User = value; break;
                case "--password": options.Password = value; break;
                case "--reader-user": options.ReaderUser = value; break;
                case "--reader-password": options.ReaderPassword = value; break;
                case "--scenario": options.Scenario = value; break;

                case "--entities": if (!TryInt(name, value, 1, out var entities, ref error)) return false; options.Entities = entities; break;
                case "--events": if (!TryInt(name, value, 0, out var events, ref error)) return false; options.Events = events; break;
                case "--batch-size": if (!TryInt(name, value, 1, out var batch, ref error)) return false; options.BatchSize = batch; break;
                case "--seed": if (!TryInt(name, value, int.MinValue, out var seed, ref error)) return false; options.Seed = seed; break;

                case "--duplicates":
                    if (!TryInt(name, value, 0, out var duplicates, ref error)) return false;
                    if (duplicates > 100) { error = "'--duplicates' is a percentage, so it must be between 0 and 100."; return false; }
                    options.DuplicatePercent = duplicates;
                    break;

                case "--interval":
                    if (!TryInt(name, value, 0, out var interval, ref error)) return false;
                    options.Interval = TimeSpan.FromMilliseconds(interval);
                    break;

                default:
                    error = $"Unknown option '{name}'.";
                    return false;
            }
        }

        return true;
    }

    private static bool TryInt(string name, string value, int minimum, out int parsed, ref string? error)
    {
        if (!int.TryParse(value, out parsed))
        {
            error = $"'{name}' needs a whole number, but got '{value}'.";
            return false;
        }

        if (parsed < minimum)
        {
            error = $"'{name}' must be {minimum} or more.";
            return false;
        }

        return true;
    }

    public static string Help =>
        """
        Simulates the CMS: sends event batches to the webhook and reports what the service made of them.

        Usage:
          dotnet run --project tools/Lateral.CMS.Simulator -- [options]

        Scenarios (default): scripted deliveries that each exercise one processing rule, and then check
        what was stored. Every one of them should end with "as expected".

          --scenario <name>     lifecycle | never-published | out-of-order | duplicates |
                                late-delete | recreate | invalid | all   (default: all)

        Stream: continuous random traffic, to watch the background processor work under load.

          --stream              send random traffic instead of the scenarios
          --entities <n>        entities to spread the events over (default 20)
          --events <n>          total events to send; 0 runs until Ctrl+C (default 200)
          --batch-size <n>      events per request (default 10)
          --interval <ms>       pause between requests (default 500)
          --duplicates <pct>    percentage re-delivered, as a webhook retry would (default 10)
          --shuffle             deliver each batch out of order
          --seed <n>            make the run reproducible

        Connection:
          --url <url>           default http://localhost:49699
          --user, --password    organization account, the only one allowed to push events
          --reader-user, --reader-password
                                account used to check the result (an administrator sees everything)
          --no-verify           send only; do not read anything back

        Other:
          --dry-run             print the batches instead of sending them
          --verbose, -v         print every batch as it is sent
          --help, -h            this text
        """;
}
