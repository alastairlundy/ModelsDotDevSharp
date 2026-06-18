---
title: Repository implementations
classification: Independent
blocked_by: [004-options-and-repository-interfaces, 005-json-contexts-and-post-processor]
parent: IMPLEMENTATION-2026-06-17.md - 0.1.0 release broadening the API surface from a single /api.json endpoint to all four upstream models.dev entrypoints (/api.json, /models.json, /catalog.json, plus logos). Three first-class repositories, ~13 new model types, four custom JSON converters, omnibus DI registration.
---

## Goal

Implement the three repository classes — `ModelInfoRepository` (renamed from `ModelInfoProvider`), `ModelMetadataRepository`, `CatalogRepository` — that fetch from the four upstream endpoints (`/api.json`, `/models.json`, `/catalog.json`, and the `LogoUrl` computed property on `AIProviderInfo`). Use `IHttpClientFactory` for HTTP and `IOptions<ModelsDevOptions>` for the configurable base address. Use the new per-endpoint JSON source-gen contexts (ticket #5).

## What to build

### `ModelInfoRepository` (rename of `ModelInfoProvider`)

`src/ModelsDotDevSharp/ModelInfoRepository.cs` (renamed from `ModelInfoProvider.cs`)

- Constructor: `ModelInfoRepository(IHttpClientFactory httpClientFactory, IOptions<ModelsDevOptions> options)`. Store the options as a private field.
- Implements `IModelInfoRepository` (the interface from ticket #4).
- Replace the hardcoded `ModelsDotDevBaseAddress` constant with `_options.Value.BaseAddress`.
- `EnumerateProviderInfosAsync` is unchanged in shape but switches to `ModelInfoJsonContext.Default.AIProviderInfo` (the per-provider type info, not the legacy `AIProviderJsonContext`).
- `GetProviderInfosAsync` is **refactored** to be a `ToArrayAsync` wrapper around `EnumerateProviderInfosAsync` (using `System.Linq.Async`'s `ToArrayAsync`). **Behavior change**: a `null` JSON response now returns an empty array (`Array.Empty<AIProviderInfo>()`) instead of throwing `Exception("Could not connect to the ModelDotDev API")`. The misleading message goes away. Update the XML doc to reflect the new behavior.
- `GetProviderInfoByIdAsync` and `GetModelInfoByIdAsync` preserve the existing `NullReferenceException` → `ArgumentException` pattern exactly (per `AGENTS.md` "Known behaviors worth knowing").
- The `using Microsoft.Extensions.Options;` import is required (likely imported via `GlobalUsings.cs` after ticket #7).

### `ModelMetadataRepository` (new)

`src/ModelsDotDevSharp/ModelMetadataRepository.cs`

- Constructor: `ModelMetadataRepository(IHttpClientFactory httpClientFactory, IOptions<ModelsDevOptions> options)`.
- Implements `IModelMetadataRepository` (ticket #4).
- `EnumerateModelMetadataAsync` is implemented by:
  1. Issuing a `GET /models.json` request with the configured base address.
  2. Deserializing the entire body to `AIModelMetadata[]` via `ModelMetadataJsonContext.Default.AIModelMetadataArray` (the flattening converter produces the array from the wire's dictionary shape).
  3. `yield return`-ing each record (the array is materialized first because the wire is a dictionary, not a true array).
- `GetModelMetadataAsync` is implemented by:
  1. Issuing the same `GET /models.json` request.
  2. Deserializing the same way.
  3. `await`ing `FirstOrDefaultAsync(m => m.Id == id, cancellationToken)`.
  4. If the result is `null`, throw `ArgumentException` with a descriptive message (matching the legacy pattern; the blueprint explicitly says the `NullReferenceException` from `FirstAsync` becomes an `ArgumentException`).

### `CatalogRepository` (new)

`src/ModelsDotDevSharp/CatalogRepository.cs`

- Constructor: `CatalogRepository(IHttpClientFactory httpClientFactory, IOptions<ModelsDevOptions> options)`.
- Implements `ICatalogRepository` (ticket #4).
- `GetCatalogAsync`:
  1. Issue a `GET /catalog.json` request.
  2. Deserialize to `AICatalog` via `CatalogJsonContext.Default.AICatalog`.
  3. If the result is `null`, throw `Exception("Could not connect to the ModelDotDev API")` (preserving the legacy message exactly; the blueprint explicitly mandates this for the catalog path).
  4. For every `AIModelCostInfo` reachable from the catalog, invoke `CostContextOverridePostProcessor.Process(cost, options)` (the post-processor is from ticket #5). A simple recursive walk over the `Cost` / `Modes` / `Experimental.Modes` / `ContextOverrides` properties is sufficient.

### Anti-slop reminders

- All three repositories use `IHttpClientFactory` via constructor injection — do not introduce `static HttpClient` fields.
- All three repositories use `IOptions<ModelsDevOptions>` for the base address; the hardcoded `https://models.dev` constant in the legacy `ModelInfoProvider` is removed.
- The `GetProviderInfosAsync` behavior change (null JSON → empty array) is a *behavior change*; the CHANGELOG (separate workflow) should mention it.
- The `Exception("Could not connect to the ModelDotDev API")` message is preserved in `CatalogRepository.GetCatalogAsync` (the catalog is a single object, not a list — a null response must throw). The same message is removed from `ModelInfoRepository.GetProviderInfosAsync` (which is now a `ToArrayAsync` wrapper).
- `CostContextOverridePostProcessor.Process` must be invoked on every `AIModelCostInfo` reachable from the catalog, including nested ones (in `AIModelInfo.Modes`, `AIModelInfo.Experimental.Modes`, and recursively in `ContextOverrides`).

## Recommended Workflow

### Step 1 — Rename and refactor `ModelInfoProvider` → `ModelInfoRepository`

Where: `src/ModelsDotDevSharp/ModelInfoRepository.cs` (renamed from `ModelInfoProvider.cs`)

- Rename the file via `git mv` (or filesystem rename + delete the old).
- Rename the class `ModelInfoProvider` → `ModelInfoRepository`.
- Change the constructor to take `IOptions<ModelsDevOptions>` and store it.
- Replace `ModelsDotDevBaseAddress` with `_options.Value.BaseAddress` at all call sites.
- Change `AIProviderJsonContext` → `ModelInfoJsonContext` and `AIProviderArrayJsonContext` → `ModelInfoJsonContext`.
- Refactor `GetProviderInfosAsync` to a `ToArrayAsync` wrapper around `EnumerateProviderInfosAsync` (using `System.Linq.Async`). Update the XML doc to describe the new behavior: "Returns an empty array if the response body is empty or deserializes to null."
- Update `using` directives: add `using Microsoft.Extensions.Options;`, `using System.Linq;`, `using System.Threading;`, and any others required.
- Delete the old `ModelInfoProvider.cs` file.
- Preserve the `NullReferenceException` → `ArgumentException` pattern in `GetProviderInfoByIdAsync` and `GetModelInfoByIdAsync` exactly.

Verify: build. The renamed class implements the renamed interface (ticket #4). Build errors related to `IModelInfoProvider` should be gone.

### Step 2 — Implement `ModelMetadataRepository`

Where: `src/ModelsDotDevSharp/ModelMetadataRepository.cs`

- Add the MIT license header.
- Declare `public class ModelMetadataRepository : IModelMetadataRepository`.
- Constructor: store the `IHttpClientFactory` and `IOptions<ModelsDevOptions>`.
- `EnumerateModelMetadataAsync`:
  - Create the client, set `BaseAddress` to `_options.Value.BaseAddress`.
  - `GET /models.json`.
  - Deserialize the body to `AIModelMetadata[]` via `ModelMetadataJsonContext.Default.AIModelMetadataArray`.
  - The deserialized array is iterated via `await foreach`; each `AIModelMetadata` is yielded.
  - The flattening converter (ticket #1) populates `Id` from the wire's dictionary key.
- `GetModelMetadataAsync(string id, CancellationToken cancellationToken = default)`:
  - Same `GET /models.json` request.
  - Deserialize the same way.
  - `await` `FirstOrDefaultAsync(m => m.Id == id, cancellationToken)` (use `System.Linq.Async`).
  - If `null`, throw `ArgumentException($"Model metadata with Id of {id} not found.")`.
- Add the necessary `using` directives: `Microsoft.Extensions.Options`, `System.Linq`, `System.Net.Http.Json`, `System.Runtime.CompilerServices`, `System.Threading`, etc.

Verify: build. The `ModelMetadataJsonContext.Default.AIModelMetadataArray` resolves to the source-gen type emitted in ticket #5.

### Step 3 — Implement `CatalogRepository`

Where: `src/ModelsDotDevSharp/CatalogRepository.cs`

- Add the MIT license header.
- Declare `public class CatalogRepository : ICatalogRepository`.
- Constructor: store the `IHttpClientFactory` and `IOptions<ModelsDevOptions>`.
- `GetCatalogAsync(CancellationToken cancellationToken = default)`:
  - Create the client, set `BaseAddress` to `_options.Value.BaseAddress`.
  - `GET /catalog.json`.
  - Deserialize the body to `AICatalog` via `CatalogJsonContext.Default.AICatalog`.
  - If `null`, throw `Exception("Could not connect to the ModelDotDev API")`.
  - Walk the catalog and invoke `CostContextOverridePostProcessor.Process(cost, options)` on every `AIModelCostInfo` reachable (top-level `Cost`, `Modes`, `Experimental.Modes`, recursively into `ContextOverrides`).
- Add the necessary `using` directives.

Verify: build. The `CatalogJsonContext.Default.AICatalog` resolves correctly.

### Step 4 — Build the project

Where: N/A

- Run `dotnet build src/ModelsDotDevSharp.slnx`.
- Expected: clean build with no new AOT/trim warnings (or, ideally, no warnings at all).

Verify: build output captured. No AOT analyzer warnings; no trim warnings.

## Context pointers

**Files** -
- `IMPLEMENTATION-2026-06-17.md` (repo root) — phase 8 of the implementation order, decisions D-C, D-D, D-L, TDP-1, TDP-2; the "Behavioral notes" section; the "Recursion guards" section; the "Error handling" section.
- `AGENTS.md` — `IHttpClientFactory` via constructor injection (the supported HTTP path; do not add `static HttpClient` fields); the `NullReferenceException` → `ArgumentException` pattern in `ModelInfoRepository.GetProviderInfoByIdAsync` and `GetModelInfoByIdAsync` is part of the public contract.
- `src/ModelsDotDevSharp/ModelInfoProvider.cs` (existing) — the file being renamed; reference for the current behavior.
- `src/ModelsDotDevSharp/Abstractions/IModelInfoRepository.cs` (new in ticket #4) — the interface contract.
- `src/ModelsDotDevSharp/Abstractions/IModelMetadataRepository.cs` (new in ticket #4) — the interface contract.
- `src/ModelsDotDevSharp/Abstractions/ICatalogRepository.cs` (new in ticket #4) — the interface contract.

**ADRs** - None exist for this area; the relevant decisions are in the implementation blueprint.

**Domain terms** -
- *Provider* (CONTEXT.md) — the AI vendor entity whose data is fetched.
- The naming convention section of `CONTEXT.md` — the data-access layer uses the `Repository` suffix.

**Ledger records** - [D-C] `Repository` suffix; [D-D] Three first-class repositories, one per endpoint; [D-L] `ModelsDevOptions.BaseAddress` is used by all three repositories for the HTTP request URL; [TDP-1] Clean break — `GetProviderInfosAsync` behavior change is accepted; [TDP-2] Asymmetric method shapes per repository.

## Acceptance criteria

- [ ] `ModelInfoRepository` is renamed from `ModelInfoProvider` and implements `IModelInfoRepository`.
- [ ] `ModelInfoRepository` uses `IOptions<ModelsDevOptions>.Value.BaseAddress` for the HTTP base address.
- [ ] `ModelInfoRepository.GetProviderInfosAsync` is a `ToArrayAsync` wrapper around `EnumerateProviderInfosAsync`; null JSON returns an empty array.
- [ ] `ModelInfoRepository.GetProviderInfoByIdAsync` and `GetModelInfoByIdAsync` preserve the `NullReferenceException` → `ArgumentException` pattern.
- [ ] `ModelMetadataRepository` implements `IModelMetadataRepository`; `EnumerateModelMetadataAsync` materializes the dictionary-shaped response to an array; `GetModelMetadataAsync` throws `ArgumentException` for not-found ids.
- [ ] `CatalogRepository` implements `ICatalogRepository`; `GetCatalogAsync` throws `Exception("Could not connect to the ModelDotDev API")` on a null response and invokes `CostContextOverridePostProcessor.Process` on every `AIModelCostInfo` reachable.
- [ ] No `static HttpClient` field exists in any of the three repositories.
- [ ] `dotnet build src/ModelsDotDevSharp.slnx` produces no warnings (clean AOT/trim).

## Dependencies

**Blocked by** -
- `004-options-and-repository-interfaces` — the interfaces and options class must exist.
- `005-json-contexts-and-post-processor` — the per-endpoint JSON contexts must exist; the post-processor must exist (called by `CatalogRepository.GetCatalogAsync`).
