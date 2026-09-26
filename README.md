# Lateral CMS

A service that ingests live events from a CMS webhook, applies them to a local copy of the content, and serves
that content over a REST API to authenticated consumers. Every entity is treated as confidential: no route is
anonymous, and what a caller sees depends on who they are.

`.NET 10` · `ASP.NET Core` · `EF Core` · `SQL Server` · `MediatR` · `FluentValidation` · `NUnit`

- **Run it:** [Quick start](#quick-start) · [Postman](#postman) · [Simulating the CMS](#simulating-the-cms) · [Tests](#tests)
- **Understand it:** [What the API does](#what-the-api-does) · [How events are processed](#how-events-are-processed) · [How it is put together](#how-it-is-put-together) · [Request lifecycles](#request-lifecycles) · [The code, file by file](#the-code-file-by-file)
- **Look things up:** [Data model](#data-model) · [Configuration](#configuration) · [Performance](#performance) · [Observability](#observability) · [Choices worth explaining](#choices-worth-explaining)

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
<https://localhost:49698>); the reference UI is at <http://localhost:49699/docs>, which the launch profile opens
directly and which the root redirects to. In Development only, it opens with the Basic credentials of the
configured administrator already filled in, so requests can be sent from the page straight away.

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

### Simulating the CMS

`tools/Lateral.CMS.Simulator` stands in for the CMS. It talks to the webhook over HTTP with its own shapes —
no reference to the service projects, so a breaking change to the contract cannot pass unnoticed there.

```bash
dotnet run --project tools/Lateral.CMS.Simulator
```

That runs seven scripted deliveries, each exercising one of the rules in
[How events are processed](#how-events-are-processed), and then reads back what the service stored:

```
── never-published ─────────────────────────────────────────────
   The corner case from the brief: version 5 is created but never published, then withdrawn...

  → Published at version 4 (1 event)
      receipt: 1 accepted, 0 rejected
      processed: 1 Applied
  → Version 5 withdrawn, having never been published (1 event)
      OK   version: 5
      OK   last published version: 4
      OK   status: Unpublished
```

It waits for each batch to leave the inbox before checking, rather than sleeping, so it tests the background
processor instead of racing it. The exit code is non-zero when a check does not match, which makes it usable
as a smoke test after a deploy. Pick one with `--scenario <name>`; `--help` lists them.

For volume instead of rules, `--stream` sends continuous random traffic — a plausible history per entity,
with a share of deliveries repeated as a webhook retry would:

```bash
dotnet run --project tools/Lateral.CMS.Simulator -- --stream --events 500 --duplicates 20 --shuffle --seed 42
```

The re-deliveries come back as `Ignored` in the event log, which is idempotency visible at a glance.

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
| `GET` | `/docs`, `/` | anonymous | The API reference. Development only; the root redirects to it. |

**Visibility.** One set of endpoints serves both audiences; what changes is what the query returns. A `User`
sees entities that are published in the CMS and not disabled locally. An `Admin` sees all of them, plus the
`status` and `isDisabledByAdmin` filters and the event log. An entity a consumer may not see answers `404`,
exactly like one that does not exist, so the endpoint cannot be used to probe for hidden identifiers.

**Writes.** CMS data — payload, version, published status — cannot be changed through the API by anybody. The
only write is the admin override, and it is stored in its own fields.

**Querying.** List endpoints take `pageIndex`, `pageSize` (up to 100), `sortColumn`, `sortDirection` and
`countMode`, and answer with `{ pageIndex, total, hasNextPage, list }`. Entities can be filtered by `id` prefix;
the event log by `status`, `batchId`, `externalId` and `correlationId`.

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
tools/
  Lateral.CMS.Simulator                     stands in for the CMS; HTTP only, no project references
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

## Request lifecycles

Three paths run through this service. Naming the class at each step is the fastest way into the code.

**A batch arrives at the webhook.** Everything here is synchronous and bounded by the size of the batch.

```
POST /cms/events
  ├─ CorrelationIdMiddleware        resolves X-Correlation-ID, or falls back to the trace id,
  │                                 and opens a logging scope around everything below
  ├─ UseHttpLogging                 begins the single summary line the request will produce
  ├─ BasicAuthenticationHandler     decodes the header, constant-time password check, roles → claims
  ├─ Authorization                  policy CmsIngestion → the Organization role, and nothing else
  ├─ CmsEventsController.Receive    one line: hands a ReceiveCmsEventsCommand to MediatR
  │
  └─ ReceiveCmsEventsCommandHandler
       ├─ ReceiveCmsEventsCommandValidator   the batch is not empty and fits MaxBatchSize
       ├─ for each event, on its own:
       │    ├─ CmsEventRequestValidator      type, id, version, payload, timestamp
       │    ├─ CmsEventTypeParser            "unPublish" → CmsEventType.UnPublish
       │    ├─ CmsPayloadSanitizer           rebuilds the JSON node by node
       │    ├─ CmsEventTimestamp             to UTC, truncated to microseconds
       │    └─ TextSanitizer                 identifier, reason, correlation id
       ├─ every event becomes a CmsEvent row: Pending, or Rejected carrying its reason
       ├─ one SaveChanges — the whole batch lands in a single transaction
       └─ ICmsEventProcessingSignal.Notify()

  202 Accepted + CmsEventBatchReceiptDTO     ← the CMS is finished waiting at this point
```

**The processor drains the inbox**, on its own, after the answer has gone out.

```
CmsEventProcessorHostedService              (returns at once if Ingestion:ProcessInBackground is false)
  └─ loop: whichever comes first — the signal above, or PollingInterval
       └─ CmsEventProcessor.ProcessPendingAsync
            ├─ GetPendingCmsEventIdsQuery    pending and due, oldest CMS timestamp first
            └─ for each event, in its own DI scope, DbContext and transaction:
                 ApplyCmsEventCommandHandler
                   ├─ CmsEventApplier.ApplyAsync
                   │    ├─ reads the entity and any tombstone for its identifier
                   │    ├─ CmsEventOrdering.Compare → Newer | Duplicate | Stale
                   │    └─ stages the change; it never saves
                   ├─ writes the outcome onto the same CmsEvent row and clears the payload
                   └─ one SaveChanges: the entity change and the inbox status commit together
                 └─ on exception → RegisterCmsEventFailureCommandHandler
                                   exponential back-off, or Failed at MaxProcessingAttempts
```

Two details that are easy to miss. The applier only *stages* changes, which is what lets the entity and the
inbox row commit in the same transaction — an event can never be marked applied without its effect, or the
other way round. And a pass that fills its batch starts the next one immediately instead of sleeping, so a
backlog drains at full speed while an idle service stays quiet.

**A consumer reads.** No part of this touches the writer database.

```
GET /api/v1/entities
  ├─ …middleware and authentication exactly as above…
  ├─ Authorization                     policy ContentReader → User or Admin
  ├─ CmsEntitiesController.GetPaged    binds GetPagedCmsEntityQuery from the query string
  │
  └─ GetPagedCmsEntityQueryHandler
       ├─ GetPagedCmsEntityQueryValidator    paging bounds, identifier length, enum values
       ├─ ICurrentUserService                is this caller an administrator?
       ├─ CmsEntityVisibility.For(isAdmin)   the single place visibility is decided
       ├─ ICmsReadOnlyDbContext              no tracking, its own connection pool
       ├─ CmsEntityDTO.Projection            server-side; only the returned columns are read
       └─ PagingWrapWithEnumerableListAsync

  ApiControllerBase.Respond → 200, or problem details with the right status
```

---

## The code, file by file

### Lateral.CMS.Domain

No dependencies at all — not even EF Core. It is what the service is about, independent of how it is stored
or served.

| File | What it holds |
| --- | --- |
| `Entities/Content/CmsEntity.cs` | The stored copy: payload, `Version`, `LastPublishedVersion`, CMS status, the admin override fields, and a `ConcurrencyToken` renewed on every write. |
| `Entities/Content/CmsEntityTombstone.cs` | A deletion marker: identifier and timestamp, deliberately no entity data. |
| `Entities/Ingestion/CmsEvent.cs` | One inbox row per received event: what arrived, its status and reason, attempts, who delivered it and under which correlation id. |
| `Entities/Common/AuditModifiedBase.cs` | `AddedDate` / `ModifiedDate`, applied by one EF configuration rather than repeated per entity. |
| `Enumerations/CmsEntityStatus.cs` | `Published`, `Unpublished` — what the CMS decided, never the local override. |
| `Enumerations/CmsEventType.cs` | `Publish`, `UnPublish`, `Delete`. |
| `Enumerations/CmsEventStatus.cs` | `Pending`, `Applied`, `Ignored`, `Rejected`, `Failed`. |
| `Constants/Role.cs` | The three roles, plus the set used to refuse an unknown one in configuration. |
| `Constants/Policy.cs` | Policy names, shared by `Program.cs` and the controllers so the two cannot drift apart. |

### Lateral.CMS.Application

The rules, and the abstractions the host has to satisfy. Knows nothing about SQL Server or ASP.NET Core.

| File | What it does |
| --- | --- |
| `ICmsDbContext.cs` | The writer. Commands and the processor use it. |
| `ICmsReadOnlyDbContext.cs` | The reader. Exposes `IQueryable` only, so a write through it does not compile. |
| `Content/IContentDbContext.cs`, `Ingestion/IIngestionDbContext.cs` | Narrower slices, so a handler reaches only the tables it needs. |
| `ConfigureServicesExtensions.cs` | Registers the validators, MediatR, `IngestionOptions` and the ingestion services. |
| `Common/TextSanitizer.cs` | Removes control characters and truncates. Everything untrusted that reaches a column or a log goes through it — this is what stops a crafted identifier forging log lines. |
| `Common/IDateTimeService.cs`, `DateTimeService.cs` | The clock behind an interface, so a test decides what "now" is. |
| `Common/ICorrelationContext.cs` | The correlation identifier, supplied by the host. |
| `Common/Serialization/RawJsonStringConverter.cs` | Writes a stored JSON string as JSON, so a payload is not parsed and re-serialized on every read. |
| `Common/Paging/PagingFilterValidator.cs` | One set of paging bounds, included by every paged query's validator. |
| `Configuration/IngestionOptions.cs` | Every ingestion knob, with its default. |

**Ingestion — the heart of the service.**

| File | What it does |
| --- | --- |
| `Services/CmsEventApplier.cs` | **The rules.** Decides what one accepted event does to the stored state, and returns an outcome with the reason in words. Stages changes only. |
| `Services/CmsEventOrdering.cs` | Whether an event is `Newer`, a `Duplicate` or `Stale`: version first, then timestamp, then the restrictive-status tie-break. |
| `Services/CmsPayloadSanitizer.cs` | Rebuilds the payload node by node — size, depth, duplicated names, control characters, invalid Unicode, HTML escaping, exact number text. |
| `Services/CmsEventTypeParser.cs` | The wire strings to the enumeration, case-insensitively, refusing the numeric values `Enum.TryParse` would accept. |
| `Services/CmsEventTimestamp.cs` | To UTC, truncated to microseconds, so a re-delivery compares equal to what is stored and registers as a duplicate. |
| `Services/CmsEventProcessor.cs` | One pass over the inbox, each event in its own scope so a failure is isolated. |
| `Services/ICmsEventProcessingSignal.cs` | The wake-up, so the processor need not wait for a poll. |
| `Handlers/ReceiveCmsEventsCommandHandler.cs` | Webhook intake: validate, sanitize, store the batch, signal, answer the receipt. |
| `Handlers/ApplyCmsEventCommandHandler.cs` | Applies one event and records its outcome in the same transaction. |
| `Handlers/GetPendingCmsEventIdsQueryHandler.cs` | What to process next. Reads the writer on purpose — a lagging replica would hide events just received. |
| `Handlers/RegisterCmsEventFailureCommandHandler.cs` | Back-off and retry, then `Failed`. |
| `Handlers/GetPagedCmsEventQueryHandler.cs` | The event log, filtered by status, batch, entity or correlation id. |
| `Validators/*.cs` | Batch size; per event the type, id, version, payload and timestamp with its clock skew. |
| `DTOs/*.cs` | The receipt, its rejections, and the event log row. |

**Content — what consumers see.**

| File | What it does |
| --- | --- |
| `Services/CmsEntityVisibility.cs` | One expression, applied to every read. Users see published and not locally disabled; administrators see everything. |
| `Handlers/GetPagedCmsEntityQueryHandler.cs` | The listing, with the admin-only filters ignored for a consumer rather than refused. |
| `Handlers/GetByCmsEntityIdQueryHandler.cs` | One entity. Not visible and not existing answer the same `404`. |
| `Handlers/SetCmsEntityDisabledCommandHandler.cs` | The only write a caller can make: the local override, in its own fields. |
| `DTOs/CmsEntityDTO.cs` | What a consumer receives, with a server-side projection. |

### Lateral.CMS.Infrastructure

The EF Core model, with nothing provider-specific in it.

| File | What it does |
| --- | --- |
| `Data/CmsDbContext.cs` + `.Content.cs` + `.Ingestion.cs` | The context, split so each slice declares its own sets. |
| `Data/Configuration/**` | Keys, lengths, indexes and concurrency tokens — see [Data model](#data-model). |
| `Data/ReadOnlyDbContextGuard.cs` | The message a reader context throws when something tries to save through it. |
| `Messaging/InProcessCmsEventProcessingSignal.cs` | A bounded channel of one: notifications coalesce, because a single pass drains everything pending. |

### Lateral.CMS.Infrastructure.Data.SqlServer

| File | What it does |
| --- | --- |
| `CmsDbContext.cs` | The shared model plus the `ISJSON` check constraints, which are provider-specific. |
| `CmsReadOnlyDbContext.cs` | The same model bound to the read connection: no tracking, and `SaveChanges` throws. |
| `CmsDbContextFactory.cs` | Design-time factory for `dotnet ef`, reading this project's own `appsettings.json`. |
| `Migrations/` | `InitialCreate`, then `AddCmsEventCorrelationId`. |

### Lateral.CMS.Infrastructure.IoC

`DependencyInjectionExtensions.AddInfrastructure` is the composition root: it binds both contexts to their
connection strings, maps every context interface onto them, registers the processing signal and then calls
`AddApplicationServices`. It is the one file to change to move to another provider.

### Lateral.CMS.API

| File | What it does |
| --- | --- |
| `Program.cs` | The whole host: logging, versioning, authentication and policies, OpenAPI, health, problem details, migration on start, and the pipeline. |
| `Controllers/CmsEventsController.cs` | The webhook and the event log. Version-neutral, because the CMS was given that path. |
| `Controllers/V1/CmsEntitiesController.cs` | The consumer API, versioned under `/api/v1`. |
| `Security/BasicAuthenticationHandler.cs` | RFC 7617: parses the header, refuses invalid base64 and UTF-8, splits at the first colon so a password may contain one. |
| `Security/BasicAuthenticationCredentialStore.cs` | The configured users. Constant-time comparison, and an unknown user still pays for one. Also validates the section at start-up. |
| `Security/CurrentUserService.cs` | The authenticated caller, for the application layer. |
| `Common/ApiControllerBase.cs` | The single place an outcome becomes a status code, and the `ApiError` shape. |
| `Common/GlobalExceptionHandler.cs` | Last line of defence: the same problem-details shape, with the exception kept out of the response. |
| `Common/CorrelationIdMiddleware.cs` | Resolves the identifier, echoes it, puts it on the request and opens the logging scope. |
| `Common/CorrelationContext.cs` | Hands that identifier to the application layer. |
| `Common/CmsHttpLoggingInterceptor.cs` | Adds caller, client address, user agent and correlation id to the request log line. |
| `Common/DatabaseHealthCheck.cs` | Readiness: the writer must answer before the webhook can accept anything. |
| `Common/DatabaseStartupExtensions.cs` | Migrates with retries, so a database that is still starting does not take the service down with it. |
| `Common/BasicAuthenticationDocumentTransformer.cs` | Declares the Basic scheme in the OpenAPI document. |
| `HostedServices/CmsEventProcessorHostedService.cs` | The background loop around `CmsEventProcessor`. |
| `Models/SetCmsEntityDisabledRequest.cs` | The body of the override. |

### tools/Lateral.CMS.Simulator

| File | What it does |
| --- | --- |
| `Program.cs` | Argument handling, Ctrl+C, and the exit code. |
| `SimulatorOptions.cs` | The options and the help text. A bad argument stops the run rather than being ignored. |
| `CmsWebhookClient.cs` | Posts batches, waits for the inbox to drain, reads entities back. |
| `CmsEvents.cs` | The wire shapes, including deliberately malformed ones. |
| `Scenarios.cs` | The scripted deliveries, and what each should produce. |
| `ScenarioRunner.cs` | Runs them, prints what happened, counts mismatches. |
| `StreamGenerator.cs` | Random traffic with a plausible history per entity. |

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

The identifier is **stored, not only logged**. Every event row keeps the one its delivery arrived under, so
`GET /cms/events?correlationId=...` turns a trace in the CMS into the list of what this service did about it —
without reading a log line, and long after the logs have rolled. When the CMS sends none, the request's trace
identifier is used, so no row is left uncorrelated.

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

## Data model

Two schemas, three tables. `Ingestion` is the record of what arrived; `Content` is the state it produced.

### `Ingestion.CmsEvent` — the inbox

One row per event received, whether it was usable or not. It is both the work queue and the audit trail.

| Column | Type | Notes |
| --- | --- | --- |
| `CmsEventId` | `bigint`, identity | Primary key. Also the order in which events arrived. |
| `BatchId` | `uniqueidentifier` | The delivery. A UUIDv7, so it sorts by time. |
| `BatchIndex` | `int` | Position in that delivery, which is how a rejection is reported. |
| `CmsEventTypeId` | `int`, null | Null when the type was missing or unsupported. |
| `ExternalId` | `nvarchar(128)`, null | The entity, as the CMS names it. |
| `Version` | `int`, null | Null for a delete. |
| `Payload` | `nvarchar(max)`, null | **Cleared once the event is applied or ignored**, so confidential data is not kept twice. Kept on a `Failed` row, for a replay. |
| `EventTimestamp` | `datetimeoffset`, null | UTC, truncated to microseconds. |
| `CmsEventStatusId` | `int` | See `CmsEventStatus`. |
| `StatusReason` | `nvarchar(2000)`, null | Why, in words. This is what makes the log useful without the logs. |
| `Attempts` | `int` | **Concurrency token.** Two processors racing for one event: the second `SaveChanges` fails and the event is re-evaluated. |
| `NextAttemptDate` | `datetimeoffset`, null | When a retry becomes due. |
| `ProcessedDate` | `datetimeoffset`, null | When it reached a final status. |
| `ReceivedDate` | `datetimeoffset` | When the webhook took it in. |
| `ReceivedBy` | `nvarchar(50)` | The authenticated account that delivered it. |
| `CorrelationId` | `nvarchar(64)`, null | The delivery's trace — the CMS's own, or the request's. |

Indexes: `(CmsEventStatusId, NextAttemptDate, EventTimestamp)` covers the pick-up query the processor runs on
every pass; `BatchId`, `ExternalId` and `CorrelationId` each back one filter of the event log.

### `Content.CmsEntity` — the current state

| Column | Type | Notes |
| --- | --- | --- |
| `CmsEntityId` | `uniqueidentifier` | Primary key, a UUIDv7 generated here. |
| `ExternalId` | `nvarchar(128)`, unique | The CMS's identifier. The unique index is what makes an entity one row. |
| `Payload` | `nvarchar(max)` | The fields at `Version`. `ISJSON` check constraint. |
| `Version` | `int` | Latest data known, published or not. |
| `LastPublishedVersion` | `int`, null | Last version that arrived through a publish. |
| `CmsEntityStatusId` | `int` | The CMS's decision. |
| `LastEventTimestamp` | `datetimeoffset` | When the entity last changed in the CMS. Half of the ordering rule. |
| `IsDisabledByAdmin` | `bit` | The local override. Never sent to the CMS. |
| `DisabledByAdminDate` | `datetimeoffset`, null | |
| `DisabledByAdminUser` | `nvarchar(50)`, null | |
| `ConcurrencyToken` | `uniqueidentifier` | Renewed on every write. |
| `AddedDate`, `ModifiedDate` | `datetimeoffset` | |

Indexes: unique on `ExternalId`; `(CmsEntityStatusId, IsDisabledByAdmin, ExternalId)` covers the consumer
listing — published, not disabled, ordered by identifier — without reading rows it may not return.

### `Content.CmsEntityTombstone` — deletion markers

| Column | Type | Notes |
| --- | --- | --- |
| `ExternalId` | `nvarchar(128)` | Primary key. |
| `DeletedTimestamp` | `datetimeoffset` | Concurrency token, and the cut-off for late events. |
| `AddedDate` | `datetimeoffset` | |

No payload, no version, no status: a tombstone holds the minimum needed to make a deletion stick, and
nothing of what was deleted.

### Migrations

Applied at start-up, with retries while the database is still coming up. Set `Database:MigrateOnStartup` to
`false` to apply them separately; the commands are in
`src/Lateral.CMS.Infrastructure.Data.SqlServer/MigrationCommands.txt`.

Payloads are `nvarchar(max)` with an `ISJSON` check constraint. SQL Server has no JSON column type, and the
constraint is the part of one worth keeping — the database refuses to store anything that is not JSON.

---

## Configuration

Everything below is ordinary configuration, so any provider overrides it: `appsettings.json`,
`appsettings.{Environment}.json`, user secrets, environment variables (`Section__Key`), command line.

| Setting | Default | What it does |
| --- | --- | --- |
| `ConnectionStrings:Database` | local SQL Server, Windows auth | The writer. Commands, the processor and migrations use it. |
| `ConnectionStrings:DatabaseReadOnly` | *empty* | The reader. Point it at a replica to move every API query there; empty uses the writer. |
| `Database:MigrateOnStartup` | `true` | Applies pending migrations when the host starts. |
| `Database:MigrationTimeout` | `00:02:00` | How long to keep retrying while the database is unreachable, before giving up and failing the start. |
| `Authentication:Basic:Realm` | `Lateral CMS` | Returned in the `WWW-Authenticate` challenge. |
| `Authentication:Basic:Users` | three accounts | `UserName`, `Password`, `Roles`. Validated at start-up: no users, a missing password or an unknown role stops the host rather than surfacing later. |
| `Ingestion:MaxBatchSize` | `1000` | Events accepted in one webhook call. |
| `Ingestion:MaxPayloadBytes` | `262144` | Largest sanitized payload, in UTF-8 bytes. |
| `Ingestion:MaxPayloadDepth` | `32` | Deepest nesting a payload may have. |
| `Ingestion:AllowedClockSkew` | `00:05:00` | How far ahead of this service's clock an event timestamp may be. |
| `Ingestion:ProcessInBackground` | `true` | Runs the in-process processor. Turn it off to process from another host — or, as the tests do, to decide when events are applied. |
| `Ingestion:ProcessingBatchSize` | `200` | Events picked per pass. A full pass starts the next one immediately. |
| `Ingestion:PollingInterval` | `00:00:10` | Longest wait between passes when no batch arrives. This is what picks up events left by a restart and retries that became due. |
| `Ingestion:MaxProcessingAttempts` | `5` | Attempts before an event is parked as `Failed`. |
| `Ingestion:RetryBaseDelay` | `00:00:02` | Base of the exponential back-off, capped at ten minutes. |
| `Logging:LogLevel:*` | `Information` | The usual levels. `Microsoft.AspNetCore.HttpLogging` is kept at `Information` so the per-request line survives the `Microsoft.AspNetCore` override. |

---

## Tests

```bash
dotnet test
```

No database, no configuration — the suite is self-contained and runs in a couple of seconds. NUnit, with the
constraint model (`Assert.That`) throughout and `Assert.Multiple` where a test checks several properties of one
result, so a failure reports all of them rather than stopping at the first.

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
