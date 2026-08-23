# AI-USAGE.md

## Tooling
Antigravity (agent-first IDE, VS Code extension), model: Gemini 3.7 Flash High.

## What was AI-generated vs. manual
- **Manual (CLI, no agent):** solution/project scaffolding — `dotnet new sln`,
  `dotnet new webapi/classlib/nunit`, project references, package installs
  (Polly, Swashbuckle). Done manually so the structure was explicit and
  correct before any agent touched the repo.
- **Agent-generated (Phase 1):** `SourceStatus` enum, `SourceResult<T>`
  record, `NormalizedResident` record, `IResidentSource`/`IBenefitsSource`
  interfaces, `ResidentController` route stubs, and the first pass of
  `DECISIONS.md` content — all generated against an exact written spec
  (field names, method signatures, degradation table content provided
  up front), not left to the agent's own design judgment.

## Review process
Every agent task was run in a plan-first mode: the agent produced a file
list/plan before writing anything, which was reviewed and approved before
proceeding. After each run, changed files were diffed and re-read manually
before committing.

Caught one instance where the agent reported a passing test as
verification without the test asserting anything meaningful (a
placeholder that trivially passed). Resolved by reading test file
contents directly rather than trusting the pass/fail claim.

Caught a genuine agent-introduced bug in the Benefits adapter: a misread
of the Python source led to `record.Element("n")` instead of
`record.Element("Name")`, silently routing every real record to
`Malformed`. Caught because the test asserted `Ok` on live search results
rather than just "no exception" — 20/20 came back `Malformed`,
surfacing it immediately. Resolved by checking the raw live HTTP response
directly rather than trusting either the agent's or the author's reading
of the source. Distinct from the placeholder-test issue above: that one
was a test-quality problem, this one was a real implementation bug the
test suite was strong enough to catch.

Phase 3's agent-reported "all tests passed" covered zero new tests — no
automated test exists that simulates a source going down mid-request. The
degradation proof (stopping the live XML service, checking the response,
restarting it) was done manually rather than relying on build/test output.

## What was NOT delegated to AI
- The architectural decisions themselves (API shape, four-way status model,
  degradation policy, retry policy, error-handling pattern) were decided
  by the author first and given to the agent as a spec to implement —
  not asked of the agent as an open-ended design task.
- Recon (Phase 0) — running the services, observing the pagination
  duplicate and the XML 500 firsthand — was done manually, not delegated.
- Phase 6's fix (four tests loosened from a hard `Ok`/`Empty` assertion to
  outcome-shape tolerant of `Unavailable`) — done entirely by the author,
  since it required judgment about what each test was actually supposed to
  prove versus what it had implicitly assumed about failure frequency.

## Phase 6 — Adapting to the 40% Failure Rate Change
No new agent-generated code for this phase. No agent claims needed
verification here, and no build/test-output trust issue arose — the
verification habit from Phases 2–3 (run the suite repeatedly, read what
each run actually showed) is what surfaced a fourth flaky test
(`GetByRefAsync_UnknownRef_ReturnsEmpty`) that an initial three-test audit
had missed.