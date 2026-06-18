---
title: Options class and repository interfaces
classification: Independent
blocked_by: []
parent: IMPLEMENTATION-2026-06-17.md - 0.1.0 release broadening the API surface from a single /api.json endpoint to all four upstream models.dev entrypoints (/api.json, /models.json, /catalog.json, plus logos). Three first-class repositories, ~13 new model types, four custom JSON converters, omnibus DI registration.
---

## Goal

Add the `ModelsDevOptions` class for the configurable base address, and introduce the three repository interfaces (`IModelInfoRepository`, `IModelMetadataRepository`, `ICatalogRepository`) that the rest of the 0.1.0 surface binds to. This ticket is the rename target for the existing `IModelInfoProvider` interface and the public surface contract for the three repositories.

## What to build

### `ModelsDevOptions` (new)

`src/ModelsDotDevSharp/ModelsDevOptions.cs`

- Public class with one property: `BaseAddress: string { get; set; } = "https://models.dev"`.
- Namespace: `ModelsDotDevSharp`.
- Documented as the "Options for `ModelsDotDevSharp`; configure via `AddModelsDev(opts => { ... })`".

### Repository interfaces (3 files, in `Abstractions/`)

All under namespace `ModelsDotDevSharp.Abstractions`.

- `src/ModelsDotDevSharp/Abstractions/IModelInfoRepository.cs` — renamed from `IModelInfoProvider`. Same four methods as the existing interface, with the type's namespace unchanged. The interface contract:
  - `Task<AIProviderInfo> GetProviderInfoByIdAsync(string providerId, CancellationToken cancellationToken = default)`
  - `Task<AIProviderInfo[]> GetProviderInfosAsync(CancellationToken cancellationToken = default)`
  - `Task<AIModelInfo> GetModelInfoByIdAsync(string providerId, string modelId, CancellationToken cancellationToken = default)`
  - `IAsyncEnumerable<AIProviderInfo> EnumerateProviderInfosAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)`
  - Plus `using System.Runtime.CompilerServices;` for `[EnumeratorCancellation]`.

- `src/ModelsDotDevSharp/Abstractions/IModelMetadataRepository.cs` — new. Methods:
  - `Task<AIModelMetadata> GetModelMetadataAsync(string id, CancellationToken cancellationToken = default)` — `id` is the `"{provider}/{model}"` form.
  - `IAsyncEnumerable<AIModelMetadata> EnumerateModelMetadataAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)`.
  - The interface should document that the streaming method materializes the array first (the wire is a dictionary, not a true array — see the blueprint's Phase 8 note for ticket #6).

- `src/ModelsDotDevSharp/Abstractions/ICatalogRepository.cs` — new. Methods:
  - `Task<AICatalog> GetCatalogAsync(CancellationToken cancellationToken = default)`.

### Rename `IModelInfoProvider` → `IModelInfoRepository`

`src/ModelsDotDevSharp/Abstractions/IModelInfoProvider.cs` → `IModelInfoRepository.cs`

- File rename and class rename.
- The method shapes and XML comments are preserved (XML comments in the existing file are placeholder `<summary>` tags; the implementer can flesh them out opportunistically, but the contract must not change).
- The implementation `ModelInfoProvider` is renamed in ticket #6 (which depends on this ticket); do not rename the implementation here.

### Anti-slop reminders

- The interfaces are public API; method signatures are part of the contract. Do not add or remove methods beyond what the spec lists.
- `IModelMetadataRepository.EnumerateModelMetadataAsync` is intentionally a non-streaming operation under the hood (it materializes the array first). This is documented in the interface, not hidden.
- `IModelInfoProvider.cs` must be deleted after the new `IModelInfoRepository.cs` is created. Do not leave both files.
- The interfaces' `CancellationToken` parameter is optional with `= default`; this is the project convention.
- The `using` statements required by the new interfaces (`System.Runtime.CompilerServices` for `[EnumeratorCancellation]`, `System.Threading` for `CancellationToken`) are typically imported via `GlobalUsings.cs` — confirm before adding per-file imports.

## Recommended Workflow

### Step 1 — Create `ModelsDevOptions`

Where: `src/ModelsDotDevSharp/ModelsDevOptions.cs`

- Add the MIT license header.
- Declare `public class ModelsDevOptions { public string BaseAddress { get; set; } = "https://models.dev"; }`.
- Add an XML doc comment explaining the class is configured via `AddModelsDev(opts => ...)`.

Verify: build. No dependencies.

### Step 2 — Create `IModelInfoRepository` (renamed from `IModelInfoProvider`)

Where: `src/ModelsDotDevSharp/Abstractions/IModelInfoRepository.cs`

- Add the MIT license header.
- Declare `public interface IModelInfoRepository` with the four methods listed above.
- Add `using System.Runtime.CompilerServices;` for `[EnumeratorCancellation]` and confirm the `using ModelsDotDevSharp;` is present (the file's types are in the `ModelsDotDevSharp` namespace, the interface is in `ModelsDotDevSharp.Abstractions`).
- Delete `src/ModelsDotDevSharp/Abstractions/IModelInfoProvider.cs` after the new file is written.

Verify: build. The existing `ModelInfoProvider` (ticket #6's renaming target) still references `IModelInfoProvider`; the build will fail with "type or namespace not found" until ticket #6 renames the implementation. This is the expected intermediate state.

### Step 3 — Create `IModelMetadataRepository`

Where: `src/ModelsDotDevSharp/Abstractions/IModelMetadataRepository.cs`

- Add the MIT license header.
- Declare `public interface IModelMetadataRepository` with the two methods listed above.
- Add a comment in the interface XML doc explaining that the streaming method materializes the wire's dictionary to an array first.

Verify: build. No implementation references this interface yet; the build passes regardless of method shapes.

### Step 4 — Create `ICatalogRepository`

Where: `src/ModelsDotDevSharp/Abstractions/ICatalogRepository.cs`

- Add the MIT license header.
- Declare `public interface ICatalogRepository` with the single `GetCatalogAsync` method.

Verify: build.

### Step 5 — Build the project

Where: N/A

- Run `dotnet build src/ModelsDotDevSharp.slnx`.
- Expected: `ModelInfoProvider` does not implement the new `IModelInfoRepository` (it still implements `IModelInfoProvider`, which no longer exists). This is the expected intermediate state. The build error is resolved by ticket #6.

Verify: build output captured; the only expected error is `ModelInfoProvider` failing to implement `IModelInfoProvider` (resolved by ticket #6).

## Context pointers

**Files** -
- `IMPLEMENTATION-2026-06-17.md` (repo root) — phase 5 of the implementation order, decisions D-C (Repository suffix), D-D (three first-class repositories), D-L (options pattern), TDP-2 (asymmetric method shapes).
- `AGENTS.md` — `IModelInfoProvider` is part of the public API surface and is in `Abstractions/`; "New methods belong on the interface and the implementation together."
- `src/ModelsDotDevSharp/Abstractions/IModelInfoProvider.cs` (existing) — the file being renamed.
- `src/ModelsDotDevSharp/ModelInfoProvider.cs` (existing) — the implementation that will be renamed in ticket #6.

**ADRs** - None exist for this area; the relevant decisions are in the implementation blueprint.

**Domain terms** -
- *Provider* (CONTEXT.md) — the AI vendor entity, distinct from the data-access layer (a Repository) that fetches it.
- The naming convention section of `CONTEXT.md` — the rule that gives the data-access layer the `Repository` suffix.

**Ledger records** - [D-C] The `Repository` suffix replaces the ambiguous `Provider` suffix; [D-D] Three first-class retrieval abstractions, one per upstream JSON endpoint; [D-L] Options pattern with a single `ModelsDevOptions { string BaseAddress = "https://models.dev" }` class registered in dependency injection; [TDP-2] Asymmetric; each repository's surface matches its view's natural shape; [TDP-1] Clean break — no migration concerns because this is the library's first release.

## Acceptance criteria

- [ ] `ModelsDevOptions` is a public class with `BaseAddress: string = "https://models.dev"` as the only configurable property.
- [ ] `IModelInfoRepository` exists in `Abstractions/` and has the four methods listed above, with `CancellationToken cancellationToken = default` as the trailing parameter on each.
- [ ] `IModelMetadataRepository` exists with `GetModelMetadataAsync` and `EnumerateModelMetadataAsync`, with the latter documented to materialize the dictionary first.
- [ ] `ICatalogRepository` exists with `GetCatalogAsync`.
- [ ] The old `IModelInfoProvider.cs` is deleted; no references to `IModelInfoProvider` remain (the implementation is renamed in ticket #6, not here).
- [ ] `dotnet build src/ModelsDotDevSharp.slnx` reports only the expected `ModelInfoProvider` → `IModelInfoProvider` resolution error (resolved by ticket #6).

## Dependencies

**Blocked by** - None — can start immediately. The interfaces and options class have no dependencies on the converters, models, or JSON contexts.
