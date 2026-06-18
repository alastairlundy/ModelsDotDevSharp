---
title: DI extension, namespace imports, and final cleanup
classification: Independent
blocked_by: [006-repository-implementations]
parent: IMPLEMENTATION-2026-06-17.md - 0.1.0 release broadening the API surface from a single /api.json endpoint to all four upstream models.dev entrypoints (/api.json, /models.json, /catalog.json, plus logos). Three first-class repositories, ~13 new model types, four custom JSON converters, omnibus DI registration.
---

## Goal

Add the `AddModelsDev` dependency injection extension method that wires the three repositories into a service collection, update `GlobalUsings.cs` to import the new namespaces, and ensure the project builds cleanly with all the new types in scope. The extension method is the public surface for consumers; the global usings are a code-hygiene matter.

## What to build

### `ModelsDevServiceCollectionExtensions`

`src/ModelsDotDevSharp/ModelsDevServiceCollectionExtensions.cs`

- Public static class.
- One method: `public static IServiceCollection AddModelsDev(this IServiceCollection services, Action<ModelsDevOptions> configureOptions)`.
- Body:
  1. `services.Configure(configureOptions);` — registers `ModelsDevOptions`.
  2. `services.AddSingleton<IModelInfoRepository, ModelInfoRepository>();`
  3. `services.AddSingleton<IModelMetadataRepository, ModelMetadataRepository>();`
  4. `services.AddSingleton<ICatalogRepository, CatalogRepository>();`
  5. `return services;`
- Do **not** call `services.AddHttpClient()`; the consumer is responsible for registering `IHttpClientFactory` (this is the standard pattern for libraries that depend on `IHttpClientFactory`).

### Update `GlobalUsings.cs`

`src/ModelsDotDevSharp/GlobalUsings.cs`

- Add `global using ModelsDotDevSharp.Abstractions;` (covers the three interfaces).
- Add `global using ModelsDotDevSharp.Converters;` (covers the three converters and the post-processor).
- Add `global using ModelsDotDevSharp.Contexts;` (covers the three JSON contexts).
- Add `global using Microsoft.Extensions.Options;` (covers `IOptions<>`).
- Confirm the existing global usings still cover `System.Text.Json.Serialization`, `System.Net.Http.Json`, `System.Threading`, `System.Runtime.CompilerServices` (likely already present).

### Final file inventory check

- Confirm `IModelInfoProvider.cs` and `ModelInfoProvider.cs` are gone.
- Confirm `AIProviderJsonContext.cs` and `AIProviderArrayJsonContext.cs` are gone.
- Confirm all 24 new files exist; the 4 extended types are in their post-extension state.

### Anti-slop reminders

- The `AddModelsDev` extension method is the *only* public DI entry point. Do not add per-repository `AddXxx` methods.
- The repositories are registered as singletons (the `IHttpClientFactory` dependency is per-call; the repository is stateless).
- `IHttpClientFactory` registration is the consumer's responsibility. Do not silently add `services.AddHttpClient()`.
- `GlobalUsings.cs` is a project-wide file; only add usings that are genuinely cross-cutting. Per-file `using` directives are preferred for one-off imports.

## Recommended Workflow

### Step 1 — Add `ModelsDevServiceCollectionExtensions`

Where: `src/ModelsDotDevSharp/ModelsDevServiceCollectionExtensions.cs`

- Add the MIT license header.
- Declare `public static class ModelsDevServiceCollectionExtensions`.
- Add the `AddModelsDev` method per the spec.
- Required `using` directives: `Microsoft.Extensions.DependencyInjection`, `ModelsDotDevSharp.Abstractions`.

Verify: build. The method is well-formed; no runtime concerns.

### Step 2 — Update `GlobalUsings.cs`

Where: `src/ModelsDotDevSharp/GlobalUsings.cs`

- Read the existing file (per `AGENTS.md`, `GlobalUsings.cs` imports `System.Text.Json.Serialization`, `ModelsDotDevSharp.Abstractions`, `ModelsDotDevSharp.Contexts`, `ModelsDotDevSharp`).
- Add `global using ModelsDotDevSharp.Converters;` if not already present.
- Add `global using Microsoft.Extensions.Options;` if not already present.
- Confirm the file has no duplicate `global using` directives.

Verify: `dotnet build src/ModelsDotDevSharp.slnx` compiles; the per-file `using` directives in the new files (e.g., `using ModelsDotDevSharp.Abstractions;` in `IModelInfoRepository.cs`) become redundant but not erroneous.

### Step 3 — Final file inventory check

Where: N/A

- Run `git status` to confirm:
  - `IModelInfoProvider.cs` is deleted.
  - `ModelInfoProvider.cs` is deleted (or renamed to `ModelInfoRepository.cs`).
  - `AIProviderJsonContext.cs` is deleted.
  - `AIProviderArrayJsonContext.cs` is deleted.
  - All 24 new files exist.
- Confirm the 4 extended types (`AIProviderInfo.cs`, `AIModelInfo.cs`, `AIModelCostInfo.cs`, `AIModelLimit.cs`) are in their post-extension state.

Verify: file inventory matches the blueprint's "Files touched (summary)" table.

### Step 4 — Build the project

Where: N/A

- Run `dotnet build src/ModelsDotDevSharp.slnx`.
- Expected: clean build with no warnings.

Verify: build output captured; zero warnings.

## Context pointers

**Files** -
- `IMPLEMENTATION-2026-06-17.md` (repo root) — phases 9 and 10 of the implementation order, decision TDP-3 (omnibus `AddModelsDev`); the "Configurable base address" cross-cutting note.
- `AGENTS.md` — `GlobalUsings.cs` is project-wide; namespaces per the "Code layout conventions" section.
- `src/ModelsDotDevSharp/GlobalUsings.cs` (existing) — the file being updated.
- `src/ModelsDotDevSharp/ModelsDotDevSharp.csproj` (existing) — confirms `Microsoft.Extensions.Http` is referenced via `Directory.Packages.props` (Central Package Management).

**ADRs** - None exist for this area; the relevant decisions are in the implementation blueprint.

**Domain terms** - None new for this ticket.

**Ledger records** - [D-L] Options pattern with a single `ModelsDevOptions` class registered in dependency injection; [TDP-3] Omnibus `AddModelsDev(opts => { ... })` extension method that registers options plus all three repositories.

## Acceptance criteria

- [ ] `AddModelsDev(this IServiceCollection, Action<ModelsDevOptions>)` exists and returns `IServiceCollection`.
- [ ] `AddModelsDev` registers `ModelsDevOptions` via `services.Configure(...)` and the three repositories as singletons.
- [ ] `AddModelsDev` does not call `services.AddHttpClient()` (the consumer is responsible).
- [ ] `GlobalUsings.cs` imports `ModelsDotDevSharp.Abstractions`, `ModelsDotDevSharp.Converters`, `ModelsDotDevSharp.Contexts`, `ModelsDotDevSharp`, and `Microsoft.Extensions.Options` (along with the previously imported namespaces).
- [ ] The four files deleted in prior tickets (`IModelInfoProvider.cs`, `ModelInfoProvider.cs`, `AIProviderJsonContext.cs`, `AIProviderArrayJsonContext.cs`) are gone.
- [ ] `dotnet build src/ModelsDotDevSharp.slnx` produces no warnings and no errors.

## Dependencies

**Blocked by** - `006-repository-implementations` — the three repository classes must exist before the DI extension can reference them.
