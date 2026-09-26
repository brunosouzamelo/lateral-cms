# Lateral CMS

A service that ingests live events from a CMS webhook, applies them to a local copy of the content, and serves
that content over a REST API to authenticated consumers. Every entity is treated as confidential: no route is
anonymous, and what a caller sees depends on who they are.

`.NET 10` · `ASP.NET Core` · `EF Core` · `SQL Server` · `MediatR` · `FluentValidation` · `xUnit`

---

## Quick start

The only prerequisite is a SQL Server instance. The default connection string points at a local one using
Windows authentication:

```
Server=localhost;Database=LateralCms;Trusted_Connection=True;TrustServerCertificate=True
```

If that is what you have, there is nothing to configure:

```bash
dotnet run --project src/Lateral.CMS.API
```

The database itself is created and migrated on start. The API listens on <http://localhost:49699> (and
<https://localhost:49698>); the reference UI is at <http://localhost:49699/scalar/v1>.

On macOS, or against any instance that uses SQL authentication, override the connection string instead of
editing the file — see [Pointing at a different database](#pointing-at-a-different-database).

Send a batch of events as the CMS would:

```bash
curl -u cms-webhook-client:fd23cb16-5bd5-4334-9115-b9a0ebde5a3d \
  -X POST http://localhost:49699/cms/events \
  -H "Content-Type: application/json" \
  -d '[
        { "type": "publish",   "id": "X", "version": 2, "payload": { "title": "A title" }, "timestamp": "2026-01-01T00:00:00Z" },
        { "type": "delete",    "id": "Y", "timestamp": "2026-01-01T00:00:00Z" },
        { "type": "unPublish", "id": "Z", "version": 4, "payload": { "title": "Hidden" },  "timestamp": "2026-01-01T00:00:00Z" }
      ]'
```

Then read the content back as a consumer:

```bash
curl -u content-consumer-1:ec12ccd4-e9dc-4cb3-bae6-e5f08a7a8359 \
  http://localhost:49699/api/v1/entities
```

`dotnet test` runs the whole suite and needs no database at all — see [Tests](#tests).

### Postman

`postman/Lateral.CMS.postman_collection.json` walks through the whole service: the webhook, what each audience
sees, the admin override and the authentication and authorization boundaries. Import it and **Run collection** —
the folders are ordered so a run works end to end, identifiers are generated per run so it can be run
repeatedly against the same database, and every request asserts its expected status, so a run either passes or
points at what broke. Credentials and `baseUrl` are collection variables and match `appsettings.json`.

It also runs headless:

```bash
npx newman run postman/Lateral.CMS.postman_collection.json
```

### Users

Three accounts are configured under `Authentication:Basic`, all using Basic authentication. The CMS and the
consumers are deliberately separate: the account the CMS delivers with cannot read content, and the accounts
that read content cannot deliver events.

| User | Password | Role | May |
| --- | --- | --- | --- |
| `cms-webhook-client` | `fd23cb16-5bd5-4334-9115-b9a0ebde5a3d` | `Organization` | Post to `/cms/events`, nothing else |
| `content-consumer-1` | `ec12ccd4-e9dc-4cb3-bae6-e5f08a7a8359` | `User` | Read published entities that are not disabled |
| `content-admin-root` | `226b714d-e4bf-4656-859b-67c0950e23ff` | `User`, `Admin` | Read everything, disable entities, read the event log |

The admin is simply the account holding the `Admin` role — `content-admin-root` here. Adding a user is an entry
in the same configuration section; nothing in the code knows these names.

Passwords live in `appsettings.json` so the service runs straight from a clone, which is the wrong place for
them anywhere real. They are ordinary configuration, so any provider overrides them:

```bash
# user secrets, for local work
dotnet user-secrets --project src/Lateral.CMS.API set "Authentication:Basic:Users:0:Password" "<guid>"

# environment variables, for a deployed instance
export Authentication__Basic__Users__0__Password="<guid>"
```

### Pointing at a different database

`ConnectionStrings:Database` is the writer. Override it rather than editing `appsettings.json`, so the change
does not travel with the repository:

```bash
# Windows / PowerShell
$env:ConnectionStrings__Database = "Server=.\SQLEXPRESS;Database=LateralCms;Trusted_Connection=True;TrustServerCertificate=True"

# macOS / Linux, or any instance using SQL authentication
export ConnectionStrings__Database="Server=localhost,1433;Database=LateralCms;User Id=sa;Password=<password>;TrustServerCertificate=True"
```

`ConnectionStrings:DatabaseReadOnly` is optional: set it to a read replica to send every API query there, and
leave it empty to use the writer for both.

---

## What the API does

The CMS owns the content; this service holds a copy of it and a local override on top. The two never mix — an
administrator can hide an entity here without that decision ever reaching the CMS, and a later CMS event does
not undo it.

| Method | Route | Who | What it does |
| --- | --- | --- | --- |
| `POST` | `/cms/events` | `Organization` | Receives a batch of CMS events. Answers `202` with a receipt. |
| `GET` | `/cms/events` | `Admin` | Processing record of every event received, newest first. Never returns payloads. |
| `GET` | `/api/v1/entities` | `User`, `Admin` | Lists the entities visible to the caller. |
| `GET` | `/api/v1/entities/{id}` | `User`, `Admin` | One entity, by the identifier the CMS assigned. |
| `PUT` | `/api/v1/entities/{id}/disabled` | `Admin` | Disables or re-enables an entity locally. |
| `GET` | `/health/live`, `/health/ready` | anonymous | Liveness and readiness probes. |

**Visibility.** One set of endpoints serves both audiences; what changes is what the query returns. A `User`
sees entities that are published in the CMS and not disabled locally. An `Admin` sees all of them, plus the
`status` and `isDisabledByAdmin` filters and the event log. An entity a consumer may not see answers `404`,
exactly like one that does not exist, so the endpoint cannot be used to probe for hidden identifiers.

**Writes.** CMS data — payload, version, published status — cannot be changed through the API by anybody. The
only write is the admin override, and it is stored in its own fields.

**Querying.** List endpoints take `pageIndex`, `pageSize` (up to 100), `sortColumn`, `sortDirection` and
`countMode`, and answer with `{ pageIndex, total, hasNextPage, list }`. Entities can be filtered by `id` prefix.

---

## How events are processed

This is the part that has to be right, so it is worth spelling out. Every rule below is covered by a test in
`CmsEventApplierTests`.

An entity carries two versions: `version`, the latest data the service has ever been given, and
`lastPublishedVersion`, the last one that arrived through a `publish`. They differ exactly in the corner case
the brief describes.

| Event | Effect |
| --- | --- |
| `publish` | Creates the entity, or replaces its data when the event is newer than what is stored. |
| `unPublish` | Keeps the data and marks the entity unpublished. Creates it already unpublished if it was never stored. |
| `delete` | Hard-deletes the entity and records a tombstone. |

**Version X+1 that was never published.** Version 4 is published, version 5 is created in the CMS but never
published — so it was never sent — and then version 5 is unpublished. The `unPublish` event carries the fields
of version 5, so the service takes them: `version` becomes 5 and the payload is version 5's, because that is
the latest data that exists. `lastPublishedVersion` stays 4, which is still the truth about what was published.
Without this, the copy would be left holding version 4's data while the CMS is at 5.

**Ordering.** The version decides first: a higher version is newer, a lower one is stale and ignored. For the
same version the event timestamp decides, which is what makes `publish v4 → unPublish v4 → publish v4` work. A
re-delivery — same version, same timestamp, same resulting status — is ignored as a duplicate. Same version and
timestamp but conflicting statuses is genuinely ambiguous, and there the restrictive one wins: the entity stays
unpublished, because delivery order must not be able to re-expose confidential content.

**Deletes and late events.** A `delete` is a hard delete, so the row is gone — but an event for it may still be
in flight. The service therefore keeps a tombstone: the identifier and the deletion timestamp, no data. Any
event not later than that timestamp is discarded, so a late `publish` cannot resurrect deleted content; a
genuine re-creation, which carries a later timestamp, is accepted and creates the entity again. A `delete` that
predates the stored state does not drop the entity — that would lose a newer change — but its tombstone is
recorded anyway.

**Idempotency.** All of the above holds when the same batch is delivered twice, which webhooks do. Applying an
event is at-least-once and every rule is a comparison against the stored state, so a retry changes nothing.

---

## How it is put together

```
src/
  Lateral.CMS.Domain                        entities, enumerations, roles — no dependencies
  Lateral.CMS.Application                   handlers, validators, DTOs, the event rules
  Lateral.CMS.Infrastructure                EF Core model and configurations (provider-agnostic)
  Lateral.CMS.Infrastructure.Data.SqlServer SQL Server contexts, migrations, design-time factory
  Lateral.CMS.Infrastructure.IoC            composition root
  Lateral.CMS.API                           controllers, authentication, problem details, hosting
tests/
  Lateral.CMS.UnitTests                     the rules in isolation, and the real API in memory
```

Dependencies point inwards. The application layer names an `ICmsDbContext` and an `ICurrentUserService` and
knows nothing about SQL Server or ASP.NET Core; both are supplied at the composition root. Swapping the store
for PostgreSQL is a new `Infrastructure.Data.*` project and one line in `DependencyInjectionExtensions`.

**Requests and handlers.** Every operation is a MediatR request with one handler returning
`NuvTools.Common.ResultWrapper.IResult`. `ApiControllerBase` is the only place that knows how an outcome maps
to a status code — success to `200`/`202`/`204`, a validation failure to `400`, a missing resource to `404` —
so controllers stay one line per endpoint.

**Validation and sanitizing.** Events are validated with FluentValidation and the payload is rebuilt node by
node before it is stored: it must be a JSON object within a size and depth budget, duplicated property names
are rejected as ambiguous, control characters are stripped from names and strings, invalid Unicode is refused,
and the output is compact with HTML-sensitive characters escaped so a consumer can embed it safely. Free text
that reaches a log or a column goes through `TextSanitizer` first, which is what keeps a crafted identifier
from forging log lines.

**Failures are per event, not per batch.** An event that fails validation is stored as `Rejected` and reported
in the receipt by its index in the batch; the rest of the batch is still accepted. One malformed event does not
cost the CMS a whole delivery.

---

## Performance

**Ingestion is asynchronous, and that is the main design decision here.** The webhook validates the batch,
writes it to an inbox table in one transaction and answers `202`. A background service drains the inbox
afterwards. Processing synchronously would have been less code, but it makes the CMS wait for the slowest event
in the batch and turns any processing failure into a delivery failure — the CMS would retry a batch it had
already handed over, and the retry budget of the webhook would be spent on work that has nothing to do with
receiving it. Separating the two means a received batch is never lost once acknowledged, retries are ours to
schedule, and the webhook's latency depends on the batch size alone.

Each event is applied in its own scope and transaction, with the entity change and the inbox status committed
together, so an event that fails is isolated and retried with exponential back-off until
`Ingestion:MaxProcessingAttempts`, then parked as `Failed`. The processor is woken by a signal when a batch
arrives and otherwise polls, which is what picks up events left behind by a restart. `Attempts` doubles as the
row's concurrency token, so two instances racing for the same event cannot both apply it.

**Reads and writes use separate contexts.** `ICmsDbContext` is the writer, bound to the primary database.
`ICmsReadOnlyDbContext` is the reader: its own connection pool, no change tracking, and it exposes `IQueryable`
only — a handler that tried to write through it would not compile. Point `ConnectionStrings:DatabaseReadOnly`
at a replica and every API query moves there with no code change. Queries project to DTOs server-side, so only
the returned columns are read, and the visibility rule is a single expression applied to every read. The
listing index covers the consumer query — published, not disabled, ordered by identifier — without touching
disabled rows.

---

## Observability

Logging is the platform's own — `Microsoft.Extensions.Logging`, no third-party library. The JSON console
formatter writes structured lines outside Development and a readable single-line one inside it, both chosen in
`Program.cs`; levels come from the usual `Logging:LogLevel` section.

Every log line written while a request runs carries a correlation identifier, pushed as a logging scope: the
CMS can send `X-Correlation-ID` to follow one delivery across both systems, and it is echoed back on the
response. Scopes also carry the framework's `TraceId` and `SpanId`, so lines correlate without any extra work.

Every event is logged when it is received and again when it is processed, with its outcome and the reason, and
the message template names each value so a collector can index them:

```json
{"LogLevel":"Information","Category":"...ApplyCmsEventCommandHandler",
 "Message":"CMS event 42 processed. Outcome: Ignored, Type: Publish, Id: X, Version: 2, ...",
 "State":{"CmsEventId":42,"Outcome":"Ignored","ExternalId":"X","Version":2,
          "Reason":"Stale event: version 2 at ... is older than the stored version 3 ..."},
 "Scopes":[{"CorrelationId":"..."}]}
```

Each request also produces one summary line through `UseHttpLogging`, with method, path, status and duration.
An interceptor adds the fields this API is asked about — caller, client address, user agent and correlation
identifier — so the request log is useful on its own.

Failures are logged too, and they are not only in the log: the reason is stored on the event row, so
`GET /cms/events?status=Failed` answers "what did not get through, and why" without grepping anything.

---

## Data and migrations

Two schemas. `Ingestion.CmsEvent` is the inbox — one row per event received, with its status, attempts and
outcome; the payload is cleared once the event is applied or ignored, so confidential data is not kept twice.
`Content.CmsEntity` holds the current state of each entity, and `Content.CmsEntityTombstone` the deletion
markers, which carry an identifier and a timestamp and no data at all.

Payloads are `nvarchar(max)` with an `ISJSON` check constraint: SQL Server has no JSON column type, and the
constraint is the part of one worth keeping — the database refuses to store anything that is not JSON.

Migrations are applied at start-up. Set `Database:MigrateOnStartup` to `false` to apply them separately; the
commands are in `src/Lateral.CMS.Infrastructure.Data.SqlServer/MigrationCommands.txt`.

---

## Tests

```bash
dotnet test
```

No database, no configuration — the suite is self-contained and runs in a couple of seconds.

**The event rules** are tested directly against `CmsEventApplier` over a private SQLite database per test: each
event type, the version-X+1 corner case, stale and duplicate deliveries, the ambiguous-ordering tie-break, and
every tombstone path. Payload sanitizing and event validation have their own tests — size, depth, duplicated
properties, control characters, number precision, missing fields, clock skew.

**The API** is hosted in memory with `WebApplicationFactory` and exercised over HTTP: the webhook receipt, a
batch processed end to end, idempotent re-delivery, hard delete versus unpublish, and what each audience can
see and do. Basic authentication is tested through the real handler — valid pairs, wrong password, unknown
user, an empty password, a malformed or base64-invalid header, the wrong scheme, and a password containing a
colon — along with the authorization boundaries between the three accounts.

Two notes on that host. It runs on SQLite, which has no date type, so `DateTimeOffset` is stored as text; every
timestamp the service writes is UTC, so ordering still holds. And the background processor is switched off so
the test decides when events are applied rather than racing with it — the same `CmsEventProcessor` the hosted
service calls, just called explicitly.

---

## Choices worth explaining

- **A tombstone instead of a soft delete.** The brief asks for a hard delete, but a hard delete alone cannot
  tell a late event from a new one. The tombstone keeps no entity data — an identifier and a timestamp — which
  is the minimum needed to make deletion stick without keeping what was deleted.
- **The restrictive status wins an ambiguous tie.** When two events cannot be ordered, the choice is between
  possibly hiding public content and possibly exposing confidential content. Only one of those is recoverable.
- **The admin override is stored separately from CMS data.** It is not a status change; it is a field of its
  own, which is why a later CMS event does not undo it and nothing ever sends it back.
- **`/cms/events` is not versioned.** The path is fixed by the contract with the CMS, so it is version-neutral
  while the consumer API versions independently under `/api/v1`.
- **Basic authentication with credentials in configuration.** It is what the brief asks for, and it keeps the
  CMS integration unchanged. Passwords are compared in constant time and an unknown user still pays for a
  comparison, so neither can be inferred from timing — but they are shared secrets in plain configuration, and
  a real deployment would hash them and hold them in a secret store.
- **Authorization is closed by default.** The fallback policy requires an authenticated user, so a new endpoint
  is private until it says otherwise. Only the health probes opt out.
- **Logging is the platform's, not a library's.** `Microsoft.Extensions.Logging` with the JSON console
  formatter, `UseHttpLogging` and scopes covers everything this service needs — structured output, per-request
  lines, a correlation identifier — with one dependency fewer and no bespoke configuration dialect. A library
  would earn its place once logs need to be shipped somewhere specific, and by then OpenTelemetry is the more
  likely answer.
- **The wake-up signal is in-process.** One instance is enough for this service, and polling still guarantees
  progress. Several instances would want a broker in place of `InProcessCmsEventProcessingSignal`; it is an
  interface in the application layer for that reason.
- **SQL Server, against whatever instance you already run.** The provider-specific code is confined to
  `Infrastructure.Data.SqlServer`: the model, the entity configurations and every handler are written against
  EF Core alone, so another relational provider is a sibling project and one line in the composition root. The
  tests prove that by running the same model on SQLite.
