# No Wrong Door — Unified Resident API

A single API that returns a unified resident view assembled from two
independent, unreliable source systems: a paginated REST index and a
slow, intermittently-failing XML register.

## Prerequisites

- Python 3.x (any recent version with `pyexpat` support — used only to run
  the provided mock services)
- .NET 10 SDK

## 1. Start the mock services

Two services must be running before the API will return real data.

```powershell
cd services
python rest_service.py --port 8081
```

In a **second terminal**:
```powershell
cd services
python xml_service.py --port 8082 --failure-rate 0.40
```

Confirm both are healthy:
```powershell
Invoke-RestMethod http://127.0.0.1:8081/health
Invoke-RestMethod http://127.0.0.1:8082/health
```

## 2. Start the API

In a **third terminal**, from the repo root:
```powershell
cd src/NoWrongDoor.Api
dotnet run
```

Confirm it's listening on `http://localhost:5220` (the default for this
project). If your console output shows a different port, substitute it
into every command in the next section.

## 3. Test the endpoints

In a **fourth terminal**:

```powershell
# Combined health of both underlying sources
Invoke-RestMethod http://localhost:5220/health

# Look up a resident directly from the REST source
Invoke-RestMethod http://localhost:5220/resident/resident_index/R-10394

# Look up a resident from the XML source (ref may contain slashes)
Invoke-RestMethod http://localhost:5220/resident/benefits_register/AS/2024/4702

# Search both sources by name (returns tagged, unmerged candidates)
Invoke-RestMethod "http://localhost:5220/residents/search?name=Delgado"
```

## 4. Run the test suite

Requires the mock services (step 1) running; one test also requires the
API itself (step 2) running, since it verifies routing behavior directly.

```powershell
dotnet test
```

## Known limitations

- No identity matching across sources — a search returns separate, tagged
  candidates per source; the caller decides if two candidates are the same
  person. See `DECISIONS.md` for rationale.
- **The Benefits Register is intentionally running at a 40% failure rate.**
  This is expected, permanent behavior, not a bug — roughly 2 in 5 calls to
  that source will legitimately return `Unavailable`. The API and test
  suite are both designed around this; a call returning degraded data is
  normal, not a sign something's broken.
- XML name parsing assumes a `"LAST, First"` format with exactly one comma.
  A suffix, a hyphenated surname, or no comma will produce a
  wrong-but-plausible name rather than an error. Not fixed — see
  `DECISIONS.md`.
- Neither source has server-side search; both adapters fetch and filter
  the full record set per search call. Fine at current data volume (620 /
  540 records), would need redesign at meaningfully larger scale.
- No authentication or authorization on any endpoint.
- No caching or circuit breaking implemented — see `DECISIONS.md`'s
  closing summary for what was deliberately deferred and why.

## Documentation

- `DECISIONS.md` — architecture decisions, degradation policy, and what
  was proven vs. assumed at each phase.
- `AI-USAGE.md` — disclosure of AI tooling used during development.