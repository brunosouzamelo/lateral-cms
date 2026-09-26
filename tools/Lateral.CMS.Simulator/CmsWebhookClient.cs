using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lateral.CMS.Simulator;

/// <summary>What the webhook answered for one delivery.</summary>
public sealed record BatchReceipt(Guid BatchId, int Received, int Accepted, int Rejected, IReadOnlyList<Rejection> Rejections);

/// <summary>One event the webhook refused, and why.</summary>
public sealed record Rejection(int Index, string? Id, IReadOnlyList<string> Errors);

/// <summary>An entity as the service holds it, reduced to what the simulator checks.</summary>
public sealed record StoredEntity(string Id, int Version, int? LastPublishedVersion, string Status, bool IsDisabledByAdmin);

/// <summary>
/// Talks to the service the way the CMS would: pushes batches with the organization account, and — when a
/// reader account is given — reads back what became of them.
/// </summary>
public sealed class CmsWebhookClient : IDisposable
{
    /// <summary>The header the service reads a caller-supplied trace from.</summary>
    public const string CorrelationHeader = "X-Correlation-ID";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly HttpClient _sender;
    private readonly HttpClient? _reader;

    public CmsWebhookClient(SimulatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _sender = Create(options.Url, options.User, options.Password);

        if (!string.IsNullOrWhiteSpace(options.ReaderUser))
            _reader = Create(options.Url, options.ReaderUser, options.ReaderPassword);
    }

    /// <summary>
    /// Delivers one batch and returns the receipt. The correlation identifier travels in
    /// <c>X-Correlation-ID</c>, as a CMS would send its own trace, and the service stores it against every
    /// event of the delivery.
    /// </summary>
    public async Task<BatchReceipt> SendAsync(
        IReadOnlyList<object> events, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/cms/events")
        {
            Content = JsonContent.Create(events, options: Json)
        };

        request.Headers.Add(CorrelationHeader, correlationId);

        var response = await _sender.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new SimulatorException($"The webhook refused the batch with {(int)response.StatusCode}: {Shorten(body)}");

        return JsonSerializer.Deserialize<BatchReceipt>(body, Json)
            ?? throw new SimulatorException("The webhook answered an empty receipt.");
    }

    /// <summary>
    /// Waits until no event of the batch is pending. The webhook acknowledges before processing, so
    /// without this a check would race the background processor instead of testing it.
    /// </summary>
    /// <returns>How each event of the batch ended up, keyed by status.</returns>
    public async Task<IReadOnlyDictionary<string, int>> WaitForProcessingAsync(
        Guid batchId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_reader is null)
            return new Dictionary<string, int>();

        var deadline = DateTimeOffset.UtcNow + timeout;

        while (true)
        {
            var page = await _reader.GetFromJsonAsync<EventPage>(
                $"/cms/events?batchId={batchId}&pageSize=100", Json, cancellationToken);

            var events = page?.List ?? [];

            if (events.Count > 0 && !events.Any(e => e.Status == "Pending"))
            {
                return events.GroupBy(e => e.Status).ToDictionary(g => g.Key, g => g.Count());
            }

            if (DateTimeOffset.UtcNow >= deadline)
                throw new SimulatorException(
                    $"Batch {batchId} was still being processed after {timeout.TotalSeconds:0}s. Is the background processor running?");

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    /// <summary>The stored entity, or null when the service holds none — which a delete is meant to cause.</summary>
    public async Task<StoredEntity?> GetEntityAsync(string externalId, CancellationToken cancellationToken)
    {
        if (_reader is null)
            return null;

        var response = await _reader.GetAsync($"/api/v1/entities/{Uri.EscapeDataString(externalId)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<StoredEntity>(Json, cancellationToken);
    }

    /// <summary>Confirms the service is reachable and the credentials are accepted, before anything is sent.</summary>
    public async Task EnsureReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _sender.GetAsync("/health/ready", cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new SimulatorException($"The service answered {(int)response.StatusCode} on /health/ready. Is the database up?");
        }
        catch (HttpRequestException exception)
        {
            throw new SimulatorException(
                $"Could not reach {_sender.BaseAddress}. Is the API running? ({exception.Message})");
        }

        // An empty batch is refused by validation, which proves the credentials are accepted without
        // storing anything: a 401 or 403 here is an authentication problem, not a validation one.
        var probe = await _sender.PostAsJsonAsync("/cms/events", Array.Empty<object>(), Json, cancellationToken);

        if (probe.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new SimulatorException(
                $"The webhook answered {(int)probe.StatusCode} for user '{_sender.DefaultRequestHeaders.Authorization?.Scheme}'. " +
                "Check --user and --password; the account needs the Organization role.");
    }

    public static string Describe(IReadOnlyList<object> events) => JsonSerializer.Serialize(events, Json);

    private static HttpClient Create(string baseUrl, string user, string password)
    {
        var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

        return client;
    }

    private static string Shorten(string value) => value.Length <= 400 ? value : value[..400] + "...";

    public void Dispose()
    {
        _sender.Dispose();
        _reader?.Dispose();
    }

    private sealed record EventPage(List<EventRow> List);
    private sealed record EventRow(string Status, string? StatusReason);
}

/// <summary>Something the simulator can report in one line instead of a stack trace.</summary>
public sealed class SimulatorException(string message) : Exception(message);
