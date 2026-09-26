using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Lateral.CMS.UnitTests.Support;

public static class CmsApiClientExtensions
{
    private static int _sequence;

    /// <summary>An identifier no other test uses, since a test class shares one database.</summary>
    public static string NextEntityId() => $"entity-{Interlocked.Increment(ref _sequence):0000}";

    public static HttpClient AsOrganization(this CmsApiFactory factory)
        => factory.As(CmsApiFactory.OrganizationUser, CmsApiFactory.OrganizationPassword);

    public static HttpClient AsConsumer(this CmsApiFactory factory)
        => factory.As(CmsApiFactory.ConsumerUser, CmsApiFactory.ConsumerPassword);

    public static HttpClient AsAdmin(this CmsApiFactory factory)
        => factory.As(CmsApiFactory.AdminUser, CmsApiFactory.AdminPassword);

    public static HttpClient As(this CmsApiFactory factory, string userName, string password)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}")));

        return client;
    }

    public static Task<HttpResponseMessage> PostEventsAsync(this HttpClient client, params object[] events)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client.PostAsJsonAsync("/cms/events", events);
    }

    public static object PublishEvent(string id, int version, DateTimeOffset timestamp, object? payload = null)
        => new { type = "publish", id, version, payload = payload ?? new { title = $"{id} v{version}" }, timestamp };

    public static object UnPublishEvent(string id, int version, DateTimeOffset timestamp, object? payload = null)
        => new { type = "unPublish", id, version, payload = payload ?? new { title = $"{id} v{version}" }, timestamp };

    public static object DeleteEvent(string id, DateTimeOffset timestamp)
        => new { type = "delete", id, timestamp };

    /// <summary>Reads the response as a JSON document, failing with the body when the status is unexpected.</summary>
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.IsSuccessStatusCode, Is.True,
            $"Expected a successful response but got {(int)response.StatusCode}: {body}");

        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
