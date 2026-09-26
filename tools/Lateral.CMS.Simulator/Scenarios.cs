namespace Lateral.CMS.Simulator;

/// <summary>One delivery, and what the service should make of it.</summary>
/// <param name="Title">What this delivery represents, in words.</param>
/// <param name="Events">The batch, exactly as it goes on the wire.</param>
/// <param name="ExpectedStatus">
/// The status every accepted event of the batch should reach. Null skips the check.
/// </param>
/// <param name="ExpectedRejected">How many events the webhook should refuse outright. Null skips the check.</param>
public sealed record ScenarioStep(
    string Title,
    IReadOnlyList<object> Events,
    string? ExpectedStatus = null,
    int? ExpectedRejected = null);

/// <summary>What the service should hold for one entity once the scenario has run.</summary>
public sealed record Expectation(
    string EntityId,
    bool ShouldExist,
    int? Version = null,
    int? LastPublishedVersion = null,
    string? Status = null);

/// <summary>A scripted sequence of deliveries that exercises one processing rule.</summary>
public sealed record Scenario(
    string Name,
    string What,
    IReadOnlyList<ScenarioStep> Steps,
    IReadOnlyList<Expectation> Expectations);

/// <summary>
/// The scenarios, each written against one of the rules in the README. They use timestamps in the past
/// and identifiers unique to the run, so a run can be repeated against the same database.
/// </summary>
public static class Scenarios
{
    public static IReadOnlyList<Scenario> All(string run)
    {
        // Comfortably in the past: an event timestamp may not be further ahead than the allowed clock skew.
        var start = DateTimeOffset.UtcNow.AddHours(-2);
        DateTimeOffset At(int minutes) => start.AddMinutes(minutes);

        string Id(string name) => $"sim-{run}-{name}";

        return
        [
            Lifecycle(Id("lifecycle"), At),
            NeverPublished(Id("never-published"), At),
            OutOfOrder(Id("out-of-order"), At),
            Duplicates(Id("duplicates"), At),
            LateDelete(Id("late-delete"), At),
            Recreate(Id("recreate"), At),
            Invalid(Id("invalid"), At)
        ];
    }

    private static Scenario Lifecycle(string id, Func<int, DateTimeOffset> at) => new(
        "lifecycle",
        "The ordinary path: an entity is created, changed, withdrawn and published again.",
        [
            new("Created at version 1", [CmsEvents.Publish(id, 1, at(0))], ExpectedStatus: "Applied"),
            new("Changed, published as version 2", [CmsEvents.Publish(id, 2, at(1))], ExpectedStatus: "Applied"),
            new("Withdrawn at version 2", [CmsEvents.UnPublish(id, 2, at(2))], ExpectedStatus: "Applied"),
            new("Changed and published again as version 3", [CmsEvents.Publish(id, 3, at(3))], ExpectedStatus: "Applied")
        ],
        [new Expectation(id, ShouldExist: true, Version: 3, LastPublishedVersion: 3, Status: "Published")]);

    private static Scenario NeverPublished(string id, Func<int, DateTimeOffset> at) => new(
        "never-published",
        "The corner case from the brief: version 5 is created but never published, then withdrawn. Its "
        + "fields arrive with the unPublish, so the stored data must advance to 5 while the last published "
        + "version stays 4.",
        [
            new("Published at version 4", [CmsEvents.Publish(id, 4, at(0))], ExpectedStatus: "Applied"),
            new("Version 5 withdrawn, having never been published", [CmsEvents.UnPublish(id, 5, at(1))], ExpectedStatus: "Applied")
        ],
        [new Expectation(id, ShouldExist: true, Version: 5, LastPublishedVersion: 4, Status: "Unpublished")]);

    private static Scenario OutOfOrder(string id, Func<int, DateTimeOffset> at) => new(
        "out-of-order",
        "Three revisions delivered newest first. The version decides, so the stored state must still end "
        + "at 3 and the older two must be discarded as stale.",
        [
            new("Versions 3, 1 and 2 in one batch, in that order",
                [CmsEvents.Publish(id, 3, at(2)), CmsEvents.Publish(id, 1, at(0)), CmsEvents.Publish(id, 2, at(1))])
        ],
        [new Expectation(id, ShouldExist: true, Version: 3, LastPublishedVersion: 3, Status: "Published")]);

    private static Scenario Duplicates(string id, Func<int, DateTimeOffset> at) => new(
        "duplicates",
        "The same delivery arrives twice, as it does when a webhook times out and retries. The second one "
        + "must change nothing.",
        [
            new("First delivery", [CmsEvents.Publish(id, 1, at(0))], ExpectedStatus: "Applied"),
            new("The very same delivery again", [CmsEvents.Publish(id, 1, at(0))], ExpectedStatus: "Ignored")
        ],
        [new Expectation(id, ShouldExist: true, Version: 1, LastPublishedVersion: 1, Status: "Published")]);

    private static Scenario LateDelete(string id, Func<int, DateTimeOffset> at) => new(
        "late-delete",
        "An event still in flight arrives after the entity was deleted. It predates the deletion, so it "
        + "must not bring the entity back.",
        [
            new("Published at version 1", [CmsEvents.Publish(id, 1, at(0))], ExpectedStatus: "Applied"),
            new("Deleted", [CmsEvents.Delete(id, at(5))], ExpectedStatus: "Applied"),
            new("A publish from before the deletion arrives late", [CmsEvents.Publish(id, 2, at(4))], ExpectedStatus: "Ignored")
        ],
        [new Expectation(id, ShouldExist: false)]);

    private static Scenario Recreate(string id, Func<int, DateTimeOffset> at) => new(
        "recreate",
        "The identifier is used again after a deletion. This event is later than the deletion, so unlike "
        + "the late one it is a genuine re-creation and must be accepted.",
        [
            new("Published, then deleted",
                [CmsEvents.Publish(id, 1, at(0)), CmsEvents.Delete(id, at(5))], ExpectedStatus: "Applied"),
            new("Created again under the same identifier", [CmsEvents.Publish(id, 1, at(6))], ExpectedStatus: "Applied")
        ],
        [new Expectation(id, ShouldExist: true, Version: 1, LastPublishedVersion: 1, Status: "Published")]);

    private static Scenario Invalid(string id, Func<int, DateTimeOffset> at) => new(
        "invalid",
        "A batch where most events are unusable. Each is validated on its own, so the good one is still "
        + "stored and the rest come back in the receipt with a reason.",
        [
            new("One valid event among five",
                [
                    CmsEvents.Publish(id, 1, at(0)),
                    CmsEvents.Malformed("no-version", id, at(0)),
                    CmsEvents.Malformed("bad-type", id, at(0)),
                    CmsEvents.Malformed("no-payload", id, at(0)),
                    CmsEvents.Malformed("payload-not-object", id, at(0)),
                    CmsEvents.Malformed("future", id, at(0))
                ],
                ExpectedStatus: "Applied",
                ExpectedRejected: 5)
        ],
        [new Expectation(id, ShouldExist: true, Version: 1, LastPublishedVersion: 1, Status: "Published")]);
}
