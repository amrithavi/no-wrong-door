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