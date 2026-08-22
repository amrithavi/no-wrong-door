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
(e.g. confirming `DECISIONS.md`'s existing content wasn't overwritten,
confirming interface signatures matched the spec exactly) before committing.
Caught one instance where the agent reported a passing test as
verification without the test asserting anything meaningful (a
placeholder that trivially passed). Resolved by reading test file
contents directly rather than trusting the pass/fail claim. Going
forward, agent test claims are checked against what the test actually
asserts, not just the reported result.

## What was NOT delegated to AI
- The architectural decisions themselves (API shape, four-way status model,
  degradation policy, retry policy, error-handling pattern) were decided
  by the author first and given to the agent as a spec to implement —
  not asked of the agent as an open-ended design task.
- Recon (Phase 0) — running the services, observing the pagination
  duplicate and the XML 500 firsthand — was done manually, not delegated.

Caught a genuine agent-introduced bug in the Benefits adapter: a misread
of the Python source led to `record.Element("n")` instead of
`record.Element("Name")`, silently routing every real record to
`Malformed`. Caught because the test asserted `Ok` on live search results
rather than just "no exception" — 20/20 came back `Malformed`,
surfacing it immediately. Resolved by checking the raw live HTTP response
directly rather than trusting either the agent's or the author's reading
of the source. Distinct from the earlier placeholder-test issue: that one
was a test-quality problem, this one was a real implementation bug the
test suite was strong enough to catch.