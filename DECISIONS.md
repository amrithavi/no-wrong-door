# DECISIONS.md

## Day 1 — Recon

### Environment confirmed
Ran both mock services locally. Resident Index (REST, :8081) serves 620 records
across 27 pages. Benefits Register (XML, :8082) serves 540 records. Both
`/health` endpoints returned 200 OK reliably — this is the only signal in the
system that isn't subject to the documented slowness/failure behavior, so it's
used as the trustworthy up/down check rather than inferring health from a
data call.

### Pagination duplicate bug confirmed, not assumed
Observed live: `R-10594` and `R-10057`, the last two records on REST page 1,
reappear as the first two records on page 2. This confirms the documented
boundary-slip behavior directly rather than trusting the README's description
of it. Consequence: dedup by stable `id` is mandatory in the Resident adapter,
not an edge case to handle opportunistically.

### XML failure and latency confirmed
Observed 1 failure (HTTP 500) in 15 calls to `/records`, consistent with the
documented ~15% failure rate. Latency observed in the documented 0.7–2.4s
range per call. Treated as steady-state behavior of this source, not a fault
to design around defensively "just in case" — it will happen on a meaningful
fraction of every run.

### Floor failure modes, confirmed against the problem doc
Graceful degradation is required for: source down (connection failure or
500), source slow (XML latency), source returning malformed/unexpected data.
Confirmed by re-reading the problem document rather than assumed from the
data pack README alone — the two documents don't describe identical scope,
and the problem document's floor is the one that's graded.

## Stack

**Chosen:** ASP.NET Core Web API, .NET 10, controller-based (not minimal
APIs). `IHttpClientFactory` + Polly for retry/timeout, transient-failure-only
retry on the XML side. `System.Xml.Linq` for XML parsing over
`XmlSerializer`. `System.Text.Json` for JSON. NUnit for tests. Swashbuckle for
API docs, which also serves as the CLI-friendly demo surface since no UI is
required for this problem.

**Rejected:** Minimal APIs — faster to scaffold, but collapses adapter,
assembly, and endpoint logic together in a way that works against the
explicit "keep source adapters independent" guidance for the day-two change.
Python — the more obvious default for a stdlib-based data pack, but the
existing ASP.NET Core / EF Core / JWT experience from ongoing coursework is a
faster, more reliable path under a two-day clock than picking up unfamiliar
tooling mid-hackathon.

**Why the interface split (`IResidentSource` / `IBenefitsSource`):** each
adapter is isolated so that a change to one source's behavior — which is
guaranteed to happen on day two — doesn't require touching the other adapter
or the assembly layer.

## API design

**Chosen:** two capabilities, not one.
- `GET /resident/{source}/{id}` — scoped lookup against a known source and
  id, returns a normalized single-source record.
- `GET /residents/search?name=&dob=` — queries both sources, returns tagged,
  unmerged candidates per source.

**Rejected:** lookup-only — doesn't solve the actual problem, since staff
would still need to know which system holds a given resident before calling
the API, which is the manual work "no wrong door" exists to remove. Merged/
scored search results — identity matching across sources is an explicit
stretch goal, not the floor; scoring candidates against each other edges into
that territory before the floor is met. Candidates are returned flat and
tagged by source; the human decides if two candidates are the same person.

## API Contract

- `GET /resident/{source}/{id}`
- `GET /residents/search?name=&dob=`
- `GET /health`

## Source Status Model

Four-way `SourceStatus` enum:
- `Ok`: source responded with valid data
- `Empty`: source responded successfully but has no matching record
- `Malformed`: source responded but data was unparseable/unexpected shape
- `Unavailable`: source did not respond successfully (down, timed out, or retries exhausted)

## Degradation Strategy

| Failure | Caller gets | How they know |
|---|---|---|
| Resident Index down | Benefits data only, resident_index marked unavailable | sources_status field in response |
| Benefits Register down (retries exhausted) | Resident data only, benefits_register marked unavailable with retry count noted | sources_status field in response |
| Benefits Register returns malformed XML | Same shape response, status malformed, not retried | sources_status field with reason |
| Source has no matching record | status empty, record null | explicitly distinct from unavailable |
| Both sources down | Empty candidates list, both sources marked unavailable | still returns HTTP 200, never a bare error |

## Retry Policy

- **Benefits Register**: retry only on HTTP 500, max 2 retries (3 attempts total), ~3 second timeout per call.
- **Resident Index**: no retries; 404 and bad page parameters are client errors, not retried.

## Identity Matching

Identity matching across sources is deferred because there is no shared key and a wrong-but-quiet match is worse than returning both as separate tagged candidates.

## Error Handling Pattern

**Chosen:** adapters never throw for expected failures (timeouts, 500s,
malformed XML, no matching record). Every adapter call returns
`SourceResult<T>` carrying a `SourceStatus` plus the data, or null with a
reason. Expected failure states are values, not control flow.

**Rejected:** try/catch at the assembly layer around each adapter call.
Works, but makes it easy for a genuine bug (a real exception) to get
silently swallowed alongside expected failures, and blurs the exact
distinction the floor requires between "source down" and "source returned
nothing" — both would just be caught exceptions. `SourceResult<T>` keeps
those two cases structurally different from the start.

## Field Mapping

| Normalized field | Resident Index (REST) | Benefits Register (XML) |
|---|---|---|
| Source | literal `"resident_index"` | literal `"benefits_register"` |
| SourceId | `id` | `Ref` |
| FullName | `first_name` + `last_name` joined | `Name` split on `", "`, reversed |
| DateOfBirth | `date_of_birth` | `Born` |
| AddressLine | `address_line` | `Addr` |
| City | `city` | `Town` |
| Phone | `phone` | *(not present — null)* |
| ProgramStatus | `program_status` | *(not present — null)* |
| LastContact | `last_contact` | *(not present — null)* |
| BenefitCode | *(not present — null)* | `BenefitCode` |
| ReviewDue | *(not present — null)* | `ReviewDue` |

**Chosen:** a field present in only one source is left `null` on records
from the other source, never defaulted to a placeholder string. This keeps
"this source doesn't track this field" structurally distinct from "this
source returned empty" — the same distinction the Source Status Model
draws at the record level, applied here at the field level.

**Known risk, not fixed:** `Name` parsing on the XML side assumes exactly
one comma, `"LAST, First"` format. A name with a suffix, a hyphenated
surname with its own comma, or no comma at all will parse wrong rather
than fail loudly — it won't throw, it'll just produce a bad `FullName`.
Flagging this now rather than after it's found in testing.

## Resident Index Adapter

**Chosen:** `GetByIdAsync` maps REST responses to `SourceStatus` directly —
200 → `Ok`, 404 → `Empty`, JSON parse failure → `Malformed`, network
failure → `Unavailable`. `SearchAsync` pages through the full result set,
dedupes by `SourceId` via a `Dictionary`, then filters in-memory by
name/dob, since the service exposes no server-side search.

**Known cost, accepted for now:** every search call pages through all 27
pages regardless of match count, because dedup requires seeing the full
set anyway. Acceptable at 620 records; would need revisiting if the source
were larger.

**Verified against the live service, not assumed:** no duplicate
`SourceId`s across full pagination; both known boundary-duplicate IDs
(`R-10594`, `R-10057`) present exactly once; unknown ID returns `Empty`.
Confirmed via terminal logs that all 27 pages were actually hit, twice.

## Benefits Register Adapter

**Chosen:** Polly retry restricted to HTTP 500 only, max 2 retries (3
attempts total), ~3s timeout per attempt — matches the retry policy
already logged, now actually implemented against it. `GetByRefAsync`
URL-encodes the reference to handle slash-containing refs like
`NO/2019/4234`. All 7 expected XML fields are validated before a record
is returned as `Ok`; any missing or invalid field routes to `Malformed`
rather than returning a partially-trusted record.

**Verified against the live service, not assumed:** valid records return
`Ok`, unknown ref returns `Empty`, a ref round-tripped through
search→lookup confirmed the URL-encoding actually works end to end, and
20 repeated live calls all resolved to a valid status with no unhandled
exception — confirming the retry/timeout path is genuinely exercised
against the service's real ~15% failure rate, not just reachable in
theory.

## Assembly Layer

**Chosen:** adapters registered via `AddHttpClient`, bound to
`IResidentSource`/`IBenefitsSource` — the controller depends only on the
interfaces, never the concrete adapters. `GET /resident/{source}/{id}`
routes to the matching adapter's lookup; unknown `source` → HTTP 400;
every `SourceStatus` outcome (Ok/Empty/Malformed/Unavailable) returns
HTTP 200, since all four are valid API responses, not API failures.
`GET /residents/search` calls both adapters concurrently via
`Task.WhenAll` so XML latency doesn't serialize behind REST, returns a
flat `candidates` list plus a `sources_status` object, always HTTP 200.
`GET /health` checks both sources' real health endpoints.

**Bug found and fixed:** the lookup route was `resident/{source}/{id}`, a
plain segment parameter. Benefits refs contain slashes
(`AS/2024/4702`), which ASP.NET Core's routing splits on before the
value reaches the controller — every real Benefits lookup silently
returned `Empty` instead of the actual record, with no error indicating
why. Fixed with a catch-all route parameter, `resident/{source}/{*id}`,
which accepts embedded slashes with no client-side encoding required.
Worth flagging on its own: this bug produced a *wrong but valid-looking*
status (`Empty`, not an exception or a 500), the exact failure mode this
whole design is meant to prevent — caught only because the ref was known
to genuinely exist, not because anything in the system signaled a
problem.

**Degradation proven live, not assumed:** stopped the running XML
service mid-session, called `/residents/search` again. `candidates`
still returned all 24 matching REST-side records unchanged,
`sources_status.benefits_register` reported `unavailable` with a
specific connection-failure reason, response stayed HTTP 200 — never a
bare error, never a silently-empty result standing in for a real
failure. Restarted the service, confirmed normal `ok`/`ok` resumed on
both sources.

## Phase 4 — Floor Verification

Ran the floor checklist explicitly rather than assuming prior phases
covered it implicitly.

- **Idempotency:** same `GET /resident/{source}/{id}` fired twice returns
  byte-identical JSON. Confirmed live.
- **Empty vs. unavailable:** a non-matching search returns `Empty` on both
  sources, never conflated with `Unavailable`. Confirmed live.
- **Source down vs. silently empty:** re-confirmed from Phase 3 (stopping
  the XML service mid-session preserved REST results, correctly flagged
  `benefits_register: unavailable`) as part of the formal pass rather than
  left as incidentally covered.
- **Malformed XML doesn't crash:** deterministic unit test
  (`ParseRecordElement_MissingField_ReturnsMalformedNotThrow`) feeds a
  hand-crafted `<Record>` missing four fields; confirms `Malformed`, not
  an exception. Deliberately independent of the live service's random
  failure rate, since malformed-XML handling is a parsing concern, not a
  network one.
- **REST pagination duplicates:** carried forward from Phase 2a
  (`R-10594`/`R-10057` present exactly once across full pagination), not
  re-run here.
- **Routing regression for slash-containing refs:** the `{id}` → `{*id}`
  fix from Phase 3 had only been checked manually and at the adapter
  level, neither of which would catch a regression in the route itself.
  Added `ResidentControllerRouteTests.cs`, which hits the actual routed
  HTTP endpoint with a real slash-containing ref and asserts `Ok` — the
  one test in the suite that requires the API itself running, not just
  the mock services, since it's specifically proving routing behavior.

**Test suite integrity:** the scaffold's placeholder test
(`UnitTest1.cs`, `Assert.Pass()`, no assertions) was deleted rather than
left in the count. It had already been flagged once as something the
agent cited as "verification" that proved nothing; keeping it would have
inflated the test count without adding meaning.

All 9 tests are real and pass. Floor proven against live services, live
routing, and deterministic unit tests — not assumed from build success
or agent-reported "tests passed" summaries alone.