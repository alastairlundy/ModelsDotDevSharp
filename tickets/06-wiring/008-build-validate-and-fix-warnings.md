---
title: Build, validate against the checklist, and fix warnings
classification: Independent
blocked_by: [007-di-extension-and-cleanup]
parent: IMPLEMENTATION-2026-06-17.md - 0.1.0 release broadening the API surface from a single /api.json endpoint to all four upstream models.dev entrypoints (/api.json, /models.json, /catalog.json, plus logos). Three first-class repositories, ~13 new model types, four custom JSON converters, omnibus DI registration.
---

## Goal

Run the full solution build with AOT analyzers enabled, address any new AOT/trim warnings, and walk through the 10-item validation checklist from the implementation blueprint. This ticket is the final gate before the 0.1.0 release.

## What to build

No new code; this is a verification ticket. The deliverable is a clean build, a validation report, and a list of any discrepancies that need follow-up.

### Validation checklist (from the blueprint)

1. `dotnet build src/ModelsDotDevSharp.slnx` produces zero errors and zero new AOT/trim warnings (compared to the pre-implementation baseline).
2. All 13 new model records and 4 new enums compile and register cleanly in their respective JSON contexts.
3. `services.AddModelsDev(opts => { opts.BaseAddress = "https://models.dev"; })` registers all three repositories in the DI container.
4. `IModelInfoRepository.GetProviderInfosAsync()` returns a non-null array (empty if the response is `null`).
5. `IModelMetadataRepository.EnumerateModelMetadataAsync()` yields `AIModelMetadata` records with `Id` populated as `{provider}/{model}` strings.
6. `ICatalogRepository.GetCatalogAsync()` returns an `AICatalog` with `Models` and `Providers` dictionaries.
7. `AIModelInfo` records include the new fields (`Status`, `ReasoningOptions`, `Interleaved`, `Modes`, `Experimental`, `Headers`, `Body`, `ProviderOverride`) when present in the wire.
8. `AIModelInfo.LogoUrl` returns the expected `https://models.dev/logos/{id}.svg` URL. (Wait — `LogoUrl` is on `AIProviderInfo`, not `AIModelInfo`. The blueprint's checklist has a typo; the implementer should verify the property is on `AIProviderInfo` per the actual implementation in ticket #3.)
9. Date fields (`Knowledge`, `ReleaseDate`, `LastUpdatedDate`) parse both `YYYY-MM` and `YYYY-MM-DD` formats from the wire.
10. `AIModelCostInfo.ContextOverrides` is populated for cost objects that have `context_over_*` keys in the wire.
11. `AIModelInterleaved` correctly distinguishes the four wire states (true / false / object / null).

### Behavior change callout

`IModelInfoRepository.GetProviderInfosAsync()` behavior change (from ticket #6): a `null` JSON response now returns an empty array (`Array.Empty<AIProviderInfo>()`) instead of throwing `Exception("Could not connect to the ModelDotDev API")`. The misleading "could not connect" message is gone. The CHANGELOG (separate workflow) should mention this for any consumer who was catching the specific exception.

### Known limitations to confirm

- `AIProviderInfo.LogoUrl` is hardcoded to `https://models.dev` and does not respect the configured `ModelsDevOptions.BaseAddress`. This is a known limitation flagged in the blueprint; a follow-up could change `LogoUrl` to a method-style API or thread the base address through a per-record field.

### Anti-slop reminders

- This is a verification ticket. The output is a written report, not code.
- Do not add a test project; tests are deferred to 0.2.0 (TDP-5).
- Do not write a CHANGELOG entry in this ticket; the CHANGELOG is a separate workflow per the handoff doc.

## Recommended Workflow

### Step 1 — Run a clean build

Where: N/A

- Run `dotnet build src/ModelsDotDevSharp.slnx -c Release`.
- Capture the full output.
- Expected: zero errors, zero warnings (AOT/trim).

Verify: build output captured; warning count is 0.

### Step 2 — Validate the 10-item checklist

Where: N/A

- Walk through each item in the validation checklist.
- For each item, run the relevant build / inspect the relevant code.
- For items 4-7, 9, 10, 11: spot-check by reading the implementation (manual verification; no test project).
- Item 8: confirm the `LogoUrl` is on `AIProviderInfo` (not `AIModelInfo` as the checklist says) and returns the expected URL string.
- Capture a one-line status for each item (pass / fail / N/A).

Verify: validation report written; each item has a status.

### Step 3 — Address any new AOT/trim warnings

Where: N/A (location depends on what surfaces)

- For each warning emitted in step 1:
  - Annotate with `[DynamicallyAccessedMembers(...)]` where the source-gen context can be tightened.
  - Add an explicit `[JsonSerializable(typeof(...))]` for any type that the analyzer flags as reflection-based.
  - Confirm no `#pragma warning disable` is used to silence warnings — every warning must be resolved by an annotation or a code change.
- Re-run `dotnet build src/ModelsDotDevSharp.slnx -c Release` to confirm zero warnings.

Verify: clean build.

### Step 4 — Confirm behavior change documentation is in place

Where: `src/ModelsDotDevSharp/ModelInfoRepository.cs`

- Read the XML doc on `GetProviderInfosAsync` and confirm it describes the new behavior (null JSON → empty array, no throw).
- Flag for the CHANGELOG (separate workflow) that the legacy `Exception("Could not connect to the ModelDotDev API")` is gone from this method.

Verify: XML doc on `GetProviderInfosAsync` is updated; ticket for the CHANGELOG is filed (out of scope for this ticket, but noted).

## Context pointers

**Files** -
- `IMPLEMENTATION-2026-06-17.md` (repo root) — the "Validation checklist" section is the source of truth for step 2; the "Cross-cutting implementation notes" section lists the known limitations.
- `AGENTS.md` — AOT/trimming constraints; `IsTrimmable=true`, `PublishTrimmed=true`, `EnableAoTAnalyzer=true`; "no test project" per the conventions; "Don't invent one without asking" applies here.
- `src/ModelsDotDevSharp/ModelInfoRepository.cs` — the file with the `GetProviderInfosAsync` behavior change.

**ADRs** - None exist for this area; the relevant decisions are in the implementation blueprint.

**Domain terms** - None new for this ticket.

**Ledger records** - [TDP-1] Clean break; the `GetProviderInfosAsync` behavior change is accepted; [TDP-5] No tests in 0.1.0; tests deferred to 0.2.0.

## Acceptance criteria

- [ ] `dotnet build src/ModelsDotDevSharp.slnx -c Release` produces zero errors and zero warnings.
- [ ] All 10 validation checklist items are walked through and each has a documented status.
- [ ] Any AOT/trim warnings surfaced during the build are addressed (annotation, code change, or explicit source-gen registration).
- [ ] The `GetProviderInfosAsync` behavior change is documented in the XML doc and flagged for the CHANGELOG.
- [ ] The known limitation (LogoUrl hardcoded to `https://models.dev`) is acknowledged in the validation report.
- [ ] No test project is created (tests are deferred to 0.2.0).
- [ ] No CHANGELOG entry is written in this ticket.

## Dependencies

**Blocked by** - `007-di-extension-and-cleanup` — the project must be in its final state (DI extension wired, cleanup complete) before validation can run end-to-end.

## Out of scope (for the next session, not this ticket)

- ADRs for the hard-to-reverse decisions (the rename `IModelInfoProvider` → `IModelInfoRepository`, the three-repository layout, the polymorphism-avoidance position, the `[JsonExtensionData]` + post-processor pattern, the per-endpoint JSON context organization, the omnibus DI choice).
- CHANGELOG entry for the 0.1.0 release.
- 0.2.0 test project (JSON-validation tests with recorded fixtures).
- 0.1.0 NuGet publishing steps.
