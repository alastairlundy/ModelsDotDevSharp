# Implementation Blueprint — `ModelsDotDevSharp` 0.1.0

> **Context**: This is a standalone implementation blueprint written by an agentic session that grilled the API surface of `ModelsDotDevSharp` (a C# wrapper over the public models.dev API) to better reflect models.dev's multiple HTTP entrypoints. The session used the `domain-grilling` skill (13 domain decisions across 13 branches) followed by the `code-implementation-grilling` skill (5 Technical Decision Points + 31 type resolutions).

---

## Scope Binding

**Linked Spec**: The conversation context dated 2026-06-17 covering the `domain-grilling` and `code-implementation-grilling` sessions. The domain decisions are also captured in `CONTEXT.md` (the project's domain glossary). The 13 domain decisions and 5 Technical Decision Points are summarized in this file under "Decisions".

**This blueprint is a context pointer valid ONLY for the linked spec.** It must not be applied to other specifications without explicit authorization. If a future change to the spec invalidates any of the decisions in this blueprint, the blueprint must be regenerated before implementation proceeds.

---

## Overview

This blueprint describes the implementation of the 0.1.0 release of `ModelsDotDevSharp`, a C# library that wraps the public models.dev API (`https://models.dev`). The 0.1.0 release broadens the API surface from a single endpoint (`/api.json`) to all four upstream entrypoints (`/api.json`, `/models.json`, `/catalog.json`, plus the `LogoUrl` string property for `/logos/{provider}.svg`).

**What's in scope** (this blueprint):
- Three first-class repository interfaces and their implementations, one per upstream JSON endpoint
- Two new entity types (`AIModelMetadata` and `AICatalog`) plus ~10 new supporting types
- Four custom JSON converters (date flexibility, dictionary flattening, boolean-or-object polymorphism, cost-override post-processing)
- Three per-endpoint JSON source-gen contexts
- An omnibus `AddModelsDev(...)` DI extension method
- A `ModelsDevOptions` class for the configurable base address
- Rename of `IModelInfoProvider` → `IModelInfoRepository` and `ModelInfoProvider` → `ModelInfoRepository`

**What's explicitly out of scope** (deferred or not part of this release):
- No test project (deferred to 0.2.0; manual verification by the maintainer for 0.1.0)
- No CHANGELOG entry writing (separate workflow)
- No NuGet publishing steps
- No ADRs in `docs/adr/` (separate workflow after this blueprint lands)

---

## Decisions (the resolved spec)

### Domain-grilling decisions (13 branches)

| # | Decision | Choice |
|---|---|---|
| A | Conceptual identity of "model" | Two distinct entities: `AIModelInfo` (per-provider view) and `AIModelMetadata` (canonical view) |
| B | How the JSON key becomes the model id on `AIModelMetadata` | AOT-safe custom converter that yields `AIModelMetadata[]` with `Id` populated from the wire's `{"{provider}/{model}": {...}}` key |
| C | Suffix for retrieval abstractions | `Repository` (replaces the ambiguous `Provider` suffix) |
| D | How retrieval abstractions are organized across endpoints | Three first-class repositories, one per endpoint |
| E | Logo endpoint coverage | `LogoUrl` is a computed string property on `AIProviderInfo` (`$"https://models.dev/logos/{Id}.svg"`); the library does not fetch SVG bytes |
| F | How the two entity types share overlapping fields | Two independent types, primitive fields duplicated (no shared base, no inheritance) |
| G | Fidelity to the new per-provider fields in `catalog.json` | Model every new field with a dedicated typed shape; dictionaries for `headers` (string→string), `body` (string→`JsonElement`), and `cost.context_over_{N}` (string→`AIModelCostInfo`) |
| H | Name of the per-model `provider` sub-object | `AIModelProviderOverride` (named for the *relationship*, not the role) |
| H.1 | `Shape` field on the override | Enum: `AIModelProviderShape { Completions, Responses }` |
| I | Type for `knowledge` / `release_date` / `last_updated` | `DateOnly?` with a custom AOT-safe converter that accepts both `YYYY-MM` and `YYYY-MM-DD` |
| J | Polymorphism for the cost-related dynamic-keyed maps | All three (`AIModelCostInfo.ContextOverrides`, `AIModelInfo.Modes`, `AIModelExperimental.Modes`) as `Dictionary<string, AIModelCostInfo>` |
| K | Shape for `reasoning_options` | Flat record with `Type: AIModelReasoningOptionType` (enum: `Toggle`, `BudgetTokens`, `Effort`) + optional `Min`, `Max`, `Values` |
| L | Base address configurability | Options pattern with a single `ModelsDevOptions { string BaseAddress = "https://models.dev" }` class registered in DI |
| M | Shape for `interleaved` | Nullable `AIModelInterleaved` record with a custom AOT-safe converter (handles boolean *or* object wire shape) |

### Code-implementation-grilling Technical Decision Points (5 TDPs)

| # | Decision | Choice |
|---|---|---|
| TDP 1 | Backward compat / release strategy | Clean break, ship as `0.1.0` (the library's first release; no existing consumers) |
| TDP 2 | Repository method shapes | Asymmetric; each repository's surface matches its view's natural shape |
| TDP 3 | DI registration pattern | Omnibus `AddModelsDev(opts => { ... })` extension method that registers options + all three repositories |
| TDP 4 | JSON source-gen context organization | Per-endpoint contexts: `ModelInfoJsonContext`, `ModelMetadataJsonContext`, `CatalogJsonContext` |
| TDP 5 | Test / verification strategy | No test project for 0.1.0; tests deferred to 0.2.0 |

### Behavioral notes

- `GetProviderInfosAsync` is refactored to be a `ToArrayAsync` wrapper around `EnumerateProviderInfosAsync`. **Behavior change**: a `null` JSON response now returns an empty array (`Array.Empty<AIProviderInfo>()`) instead of throwing `Exception("Could not connect to the ModelDotDev API")`. The misleading "could not connect" message goes away.
- `AIModelMetadata.Id` is populated by the flattening converter when reading from `/models.json`. In `AICatalog.Models` (which preserves the wire's dictionary shape), `AIModelMetadata.Id` is `null` after deserialization — consumers use the dictionary key.
- `AIModelInfo.LogoUrl` is a *computed* property (not `[JsonPropertyName]`-bound); the wire format does not include a `logo_url` field. The URL is constructed from a hardcoded `https://models.dev` — it does not respect the configured `ModelsDevOptions.BaseAddress` (known limitation; flagged for a follow-up).
- The per-model `provider` sub-object is mapped to the C# property `ProviderOverride` on `AIModelInfo` to avoid collision with the top-level `AIProviderInfo` entity. The wire name remains `provider`.

---

## Type Inventory (31 types)

### New types (24)

| Type | Kind | File | Purpose |
|---|---|---|---|
| `ModelsDevOptions` | class | `src/ModelsDotDevSharp/ModelsDevOptions.cs` | DI options class; carries `BaseAddress` |
| `IModelInfoRepository` | interface | `src/ModelsDotDevSharp/Abstractions/IModelInfoRepository.cs` | Per-provider view (renamed from `IModelInfoProvider`) |
| `IModelMetadataRepository` | interface | `src/ModelsDotDevSharp/Abstractions/IModelMetadataRepository.cs` | Canonical view |
| `ICatalogRepository` | interface | `src/ModelsDotDevSharp/Abstractions/ICatalogRepository.cs` | Combined view |
| `AIModelMetadata` | record | `src/ModelsDotDevSharp/Models/AIModelMetadata.cs` | Canonical model (per Branch A) |
| `AIModelWeightInfo` | record | `src/ModelsDotDevSharp/Models/AIModelWeightInfo.cs` | Weights download location (`label`, `url`) |
| `AIModelBenchmark` | record | `src/ModelsDotDevSharp/Models/AIModelBenchmark.cs` | Benchmark result |
| `AIModelInterleaved` | record | `src/ModelsDotDevSharp/Models/AIModelInterleaved.cs` | Wraps the `interleaved` field's polymorphic shape |
| `AIModelInterleavedField` | enum | `src/ModelsDotDevSharp/Models/AIModelInterleavedField.cs` | `ReasoningContent`, `ReasoningDetails` |
| `AIModelReasoningOption` | record | `src/ModelsDotDevSharp/Models/AIModelReasoningOption.cs` | Reasoning config entry (flat record with `Type` enum) |
| `AIModelReasoningOptionType` | enum | `src/ModelsDotDevSharp/Models/AIModelReasoningOptionType.cs` | `Toggle`, `BudgetTokens`, `Effort` |
| `AIModelProviderOverride` | record | `src/ModelsDotDevSharp/Models/AIModelProviderOverride.cs` | Per-model provider override |
| `AIModelProviderShape` | enum | `src/ModelsDotDevSharp/Models/AIModelProviderShape.cs` | `Completions`, `Responses` |
| `AIModelExperimental` | record | `src/ModelsDotDevSharp/Models/AIModelExperimental.cs` | `experimental` sub-object wrapper |
| `AIModelStatus` | enum | `src/ModelsDotDevSharp/Models/AIModelStatus.cs` | `Alpha`, `Beta`, `Deprecated` |
| `AICatalog` | record | `src/ModelsDotDevSharp/Models/AICatalog.cs` | Combined view (`Models` dict, `Providers` dict) |
| `ModelInfoRepository` | class | `src/ModelsDotDevSharp/ModelInfoRepository.cs` | Renamed from `ModelInfoProvider.cs` |
| `ModelMetadataRepository` | class | `src/ModelsDotDevSharp/ModelMetadataRepository.cs` | New |
| `CatalogRepository` | class | `src/ModelsDotDevSharp/CatalogRepository.cs` | New |
| `ModelsDevServiceCollectionExtensions` | static class | `src/ModelsDotDevSharp/ModelsDevServiceCollectionExtensions.cs` | Omnibus `AddModelsDev` |
| `FlexibleDateOnlyConverter` | converter | `src/ModelsDotDevSharp/Converters/FlexibleDateOnlyConverter.cs` | `DateOnly?` with `YYYY-MM` and `YYYY-MM-DD` |
| `ModelsJsonFlatteningConverter` | converter | `src/ModelsDotDevSharp/Converters/ModelsJsonFlatteningConverter.cs` | Dictionary → flat `AIModelMetadata[]` with `Id` |
| `InterleavedBooleanOrObjectConverter` | converter | `src/ModelsDotDevSharp/Converters/InterleavedBooleanOrObjectConverter.cs` | Boolean or object → `AIModelInterleaved?` |
| `CostContextOverridePostProcessor` | static class | `src/ModelsDotDevSharp/Converters/CostContextOverridePostProcessor.cs` | Post-processes extension data → `ContextOverrides` |
| `ModelInfoJsonContext` | context | `src/ModelsDotDevSharp/Contexts/ModelInfoJsonContext.cs` | Per-provider view JSON context |
| `ModelMetadataJsonContext` | context | `src/ModelsDotDevSharp/Contexts/ModelMetadataJsonContext.cs` | Canonical view JSON context |
| `CatalogJsonContext` | context | `src/ModelsDotDevSharp/Contexts/CatalogJsonContext.cs` | Combined view JSON context |

### Extended existing types (3)

| Type | File | Change |
|---|---|---|
| `AIProviderInfo` | `src/ModelsDotDevSharp/Models/AIProviderInfo.cs` | Add `LogoUrl` (computed property using `$"https://models.dev/logos/{Id}.svg"`) |
| `AIModelInfo` | `src/ModelsDotDevSharp/Models/AIModelInfo.cs` | Add `Status`, `ReasoningOptions`, `Interleaved`, `Modes`, `Experimental`, `Headers`, `Body`, `ProviderOverride`; change `KnowledgeCutOffDate` → `Knowledge` (DateOnly?); change `ReleaseDate` and `LastUpdatedDate` to `DateOnly?` |
| `AIModelCostInfo` | `src/ModelsDotDevSharp/Models/AIModelCostInfo.cs` | Add `ContextOverrides: Dictionary<string, AIModelCostInfo>` (populated by post-processor); add transient `[JsonExtensionData] Dictionary<string, JsonElement>? ContextOverridesRaw` (hidden via `[EditorBrowsable(EditorBrowsableState.Never)]`) |
| `AIModelLimit` | `src/ModelsDotDevSharp/Models/AIModelLimit.cs` | Change `InputTokens` from `int` to `int?` |

### Files to delete (1)

| File | Reason |
|---|---|
| `src/ModelsDotDevSharp/Contexts/AIProviderJsonContext.cs` | Replaced by `ModelInfoJsonContext` |
| `src/ModelsDotDevSharp/Contexts/AIProviderArrayJsonContext.cs` | Replaced by `ModelInfoJsonContext` |
| `src/ModelsDotDevSharp/ModelInfoProvider.cs` | Renamed to `ModelInfoRepository.cs` |

---

## Implementation Order

The implementation should follow dependency order. Each step is roughly 30-90 minutes of focused work; the full sequence is approximately 12-18 hours of work for a single developer, or 8-12 hours split across 2-3 parallel developers (with the interface and JSON context work in parallel).

### Phase 1 — Foundation (converters and enums)

These have no dependencies on other types and can be written first.

1. `FlexibleDateOnlyConverter` (Branch I) — `JsonConverter<DateOnly?>`; tries `yyyy-MM-dd` first, then `yyyy-MM`; normalizes writes to `yyyy-MM-dd`.
2. `ModelsJsonFlatteningConverter` (Branch B Option 3) — `JsonConverter<AIModelMetadata[]>`; reads the wire's dictionary shape and projects to a flat array with `Id` populated from each key.
3. `InterleavedBooleanOrObjectConverter` (Branch M) — `JsonConverter<AIModelInterleaved?>`; handles the four wire states (`true` → record with `Field = null`; `false` → `null`; object → record with `Field = <enum>`; null → `null`).
4. `AIModelInterleavedField` enum — `ReasoningContent`, `ReasoningDetails` with `[JsonStringEnumMemberName]`.
5. `AIModelReasoningOptionType` enum — `Toggle`, `BudgetTokens`, `Effort` with `[JsonStringEnumMemberName]`.
6. `AIModelProviderShape` enum — `Completions`, `Responses` with `[JsonStringEnumMemberName]`.
7. `AIModelStatus` enum — `Alpha`, `Beta`, `Deprecated` with `[JsonStringEnumMemberName]`.

### Phase 2 — New records

These depend on the enums from Phase 1.

8. `AIModelInterleaved` — single nullable enum property `Field: AIModelInterleavedField?`.
9. `AIModelReasoningOption` — `Type: AIModelReasoningOptionType` + nullable `Min`, `Max`, `Values`.
10. `AIModelProviderOverride` — five nullable properties: `NpmPackageId`, `ApiUrl`, `Shape: AIModelProviderShape?`, `Body: Dictionary<string, JsonElement>?`, `Headers: Dictionary<string, string>?`.
11. `AIModelExperimental` — `Modes: Dictionary<string, AIModelCostInfo>?` (uses `AIModelCostInfo` from the existing type).
12. `AIModelWeightInfo` — `Label`, `Url` (both `string`).
13. `AIModelBenchmark` — `Name`, `Score: double`, `Metric`, `Source` (required) + `Harness`, `Dataset`, `Version`, `Variant` (nullable `string?`) + `Date: DateOnly?`.

### Phase 3 — New canonical model and catalog

14. `AIModelMetadata` — 16 properties; the canonical view's `Id` is bound to the wire's `id` (or null) and is populated by the flattening converter at deserialization time.
15. `AICatalog` — `Models: Dictionary<string, AIModelMetadata>` and `Providers: Dictionary<string, AIProviderInfo>`.

### Phase 4 — Extend existing types

16. `AIModelLimit` — change `InputTokens` from `int` to `int?`.
17. `AIModelCostInfo` — add `ContextOverrides: Dictionary<string, AIModelCostInfo>` (initialized to `new()`); add transient `[JsonExtensionData] Dictionary<string, JsonElement>? ContextOverridesRaw` with `[EditorBrowsable(EditorBrowsableState.Never)]`.
18. `AIProviderInfo` — add computed `LogoUrl` property: `public string LogoUrl => $"https://models.dev/logos/{Id}.svg";` with `[JsonIgnore]`.
19. `AIModelInfo` — biggest extension: add 8 new nullable properties (`Status`, `ReasoningOptions`, `Interleaved`, `Modes`, `Experimental`, `Headers`, `Body`, `ProviderOverride`); rename `KnowledgeCutOffDate` → `Knowledge` and change all three date fields to `DateOnly?`.

### Phase 5 — Options and interfaces

20. `ModelsDevOptions` — class with `BaseAddress: string = "https://models.dev"`.
21. `IModelInfoRepository` (rename from `IModelInfoProvider`) — same four methods as the existing interface.
22. `IModelMetadataRepository` — `GetModelMetadataAsync(string id, ...)` + `EnumerateModelMetadataAsync(...)`.
23. `ICatalogRepository` — `GetCatalogAsync(...)`.

### Phase 6 — JSON contexts

24. `ModelInfoJsonContext` — registers per-provider types; converters: `FlexibleDateOnlyConverter`, `InterleavedBooleanOrObjectConverter`.
25. `ModelMetadataJsonContext` — registers canonical types; converters: `FlexibleDateOnlyConverter`, `ModelsJsonFlatteningConverter`.
26. `CatalogJsonContext` — registers the union of per-provider and canonical types (with duplication of type registrations, per TDP 4); converters: `FlexibleDateOnlyConverter`, `InterleavedBooleanOrObjectConverter`.

### Phase 7 — Post-processor

27. `CostContextOverridePostProcessor` — static class with `Process(AIModelCostInfo cost, JsonSerializerOptions options)`; iterates `ContextOverridesRaw`, deserializes `context_over_*` keys, populates `ContextOverrides`, clears the bag.

### Phase 8 — Repository implementations

28. `ModelInfoRepository` (rename from `ModelInfoProvider`) — `IHttpClientFactory` + `IOptions<ModelsDevOptions>` constructor; same four methods; `GetProviderInfosAsync` is refactored to `ToArrayAsync(EnumerateProviderInfosAsync(...))` (behavior change: null JSON → empty array, no throw); uses `ModelInfoJsonContext`.
29. `ModelMetadataRepository` — `IHttpClientFactory` + `IOptions<ModelsDevOptions>`; streaming method materializes the array first (the wire is a dictionary, not a true array); uses `ModelMetadataJsonContext.Default.AIModelMetadataArray`; throws `ArgumentException` for not-found in `GetModelMetadataAsync` (matching the existing pattern).
30. `CatalogRepository` — `IHttpClientFactory` + `IOptions<ModelsDevOptions>`; `GetCatalogAsync` deserializes the single `AICatalog` object; throws `Exception("Could not connect to the ModelDotDev API")` on `null` (preserving the legacy message).

### Phase 9 — DI extension

31. `ModelsDevServiceCollectionExtensions` — `AddModelsDev(this IServiceCollection services, Action<ModelsDevOptions> configureOptions)`; registers `ModelsDevOptions` via `services.Configure(...)`; registers the three repositories as singletons; does **not** call `services.AddHttpClient()` (consumer's responsibility).

### Phase 10 — Cleanup

- Delete the old files: `AIProviderJsonContext.cs`, `AIProviderArrayJsonContext.cs`, `ModelInfoProvider.cs`.
- Update `GlobalUsings.cs` to include the new namespaces if needed (e.g., `ModelsDotDevSharp.Converters`, `Microsoft.Extensions.Options`).
- Update `Directory.Packages.props` if any new packages are required (none expected; all converters and types use the existing dependencies).

---

## Cross-cutting implementation notes

### AOT and source-gen

- All three JSON contexts are `partial class` and inherit from `JsonSerializerContext`. The source-gen emits the type info at build time.
- All converters are `public sealed` (no inheritance) for AOT trim safety.
- The `ModelsJsonFlatteningConverter` calls `JsonSerializer.Deserialize(ref reader, ModelMetadataJsonContext.Default.AIModelMetadata)` for the inner deserialization — using the source-gen type info to avoid reflection.
- The `InterleavedBooleanOrObjectConverter` does string-to-enum mapping by hand (not via `JsonStringEnumConverter`) to keep the converter self-contained.
- The `AIModelLimit` type is registered explicitly in `ModelMetadataJsonContext` and `CatalogJsonContext` (even though it's only used as a property of `AIModelMetadata`) — without explicit registration, the source-gen would fall back to reflection-based deserialization for `AIModelLimit`, which would fail AOT.

### Recursion guards

- `ModelsJsonFlatteningConverter` is only registered against `AIModelMetadata[]` (not `AIModelMetadata` itself), so the inner `JsonSerializer.Deserialize(ref reader, ...)` for each value does not recurse into the flattening converter.
- `InterleavedBooleanOrObjectConverter` is only registered against `AIModelInterleaved?` (a leaf type), so the converter's own `Write` (which doesn't go through other converters) is the only serialization path.
- `CostContextOverridePostProcessor` is called *after* deserialization completes, not as part of the deserialization pipeline. There's no recursion concern.

### Error handling

- The existing `NullReferenceException` → `ArgumentException` pattern in `ModelInfoRepository.GetProviderInfoByIdAsync` and `GetModelInfoByIdAsync` is preserved exactly (per `AGENTS.md`).
- The `ModelMetadataRepository.GetModelMetadataAsync` uses the same pattern: catch `NullReferenceException` from `FirstAsync` and rethrow as `ArgumentException` with a descriptive message.
- The `Exception("Could not connect to the ModelDotDev API")` from the legacy `GetProviderInfosAsync` is **removed** (because the refactored array method is a `ToArrayAsync` wrapper, and `ToArrayAsync` returns an empty array for an empty stream — there's no `null` to throw on). The same message is **preserved** in `CatalogRepository.GetCatalogAsync` (because the catalog is a single object, not a list, and a `null` response must throw).

### JSON context type duplication

`CatalogJsonContext` re-registers the per-provider types (because `AICatalog.Providers` references them) and `ModelInfoJsonContext` does not include canonical types. A type rename has to be replicated in both `ModelInfoJsonContext` and `CatalogJsonContext` (mitigation: code-review checklist). The per-endpoint shape is the trade-off accepted in TDP 4.

### Configurable base address

`ModelsDevOptions.BaseAddress` is used by all three repositories for the HTTP request URL. The `LogoUrl` property on `AIProviderInfo` does **not** respect the configured base address (it's a hardcoded `https://models.dev`). This is a known limitation; a follow-up could change `LogoUrl` to a method-style API or thread the base address through a per-record field.

---

## Validation checklist (for the implementer)

After implementation, the following should hold:

- [ ] `dotnet build src/ModelsDotDevSharp.slnx` produces zero errors and zero new AOT trim warnings (compared to the pre-implementation baseline).
- [ ] All 13 new model records and 4 new enums compile and register cleanly in their respective JSON contexts.
- [ ] `services.AddModelsDev(opts => { opts.BaseAddress = "https://models.dev"; })` registers all three repositories in the DI container.
- [ ] `IModelInfoRepository.GetProviderInfosAsync()` returns a non-null array (empty if the response is `null`).
- [ ] `IModelMetadataRepository.EnumerateModelMetadataAsync()` yields `AIModelMetadata` records with `Id` populated as `{provider}/{model}` strings.
- [ ] `ICatalogRepository.GetCatalogAsync()` returns an `AICatalog` with `Models` and `Providers` dictionaries.
- [ ] `AIModelInfo` records include the new fields (`Status`, `ReasoningOptions`, `Interleaved`, `Modes`, `Experimental`, `Headers`, `Body`, `ProviderOverride`) when present in the wire.
- [ ] `AIModelInfo.LogoUrl` returns the expected `https://models.dev/logos/{id}.svg` URL.
- [ ] Date fields (`Knowledge`, `ReleaseDate`, `LastUpdatedDate`) parse both `YYYY-MM` and `YYYY-MM-DD` formats from the wire.
- [ ] `AIModelCostInfo.ContextOverrides` is populated for cost objects that have `context_over_*` keys in the wire.
- [ ] `AIModelInterleaved` correctly distinguishes the four wire states (true / false / object / null).

---

## Files touched (summary)

| Action | File |
|---|---|
| New | `src/ModelsDotDevSharp/ModelsDevOptions.cs` |
| New | `src/ModelsDotDevSharp/Abstractions/IModelInfoRepository.cs` |
| New | `src/ModelsDotDevSharp/Abstractions/IModelMetadataRepository.cs` |
| New | `src/ModelsDotDevSharp/Abstractions/ICatalogRepository.cs` |
| New | `src/ModelsDotDevSharp/Models/AIModelMetadata.cs` |
| New | `src/ModelsDotDevSharp/Models/AIModelWeightInfo.cs` |
| New | `src/ModelsDotDevSharp/Models/AIModelBenchmark.cs` |
| New | `src/ModelsDotDevSharp/Models/AIModelInterleaved.cs` |
| New | `src/ModelsDotDevSharp/Models/AIModelInterleavedField.cs` |
| New | `src/ModelsDotDevSharp/Models/AIModelReasoningOption.cs` |
| New | `src/ModelsDotDevSharp/Models/AIModelReasoningOptionType.cs` |
| New | `src/ModelsDotDevSharp/Models/AIModelProviderOverride.cs` |
| New | `src/ModelsDotDevSharp/Models/AIModelProviderShape.cs` |
| New | `src/ModelsDotDevSharp/Models/AIModelExperimental.cs` |
| New | `src/ModelsDotDevSharp/Models/AIModelStatus.cs` |
| New | `src/ModelsDotDevSharp/Models/AICatalog.cs` |
| New | `src/ModelsDotDevSharp/ModelMetadataRepository.cs` |
| New | `src/ModelsDotDevSharp/CatalogRepository.cs` |
| New | `src/ModelsDotDevSharp/ModelsDevServiceCollectionExtensions.cs` |
| New | `src/ModelsDotDevSharp/Converters/FlexibleDateOnlyConverter.cs` |
| New | `src/ModelsDotDevSharp/Converters/ModelsJsonFlatteningConverter.cs` |
| New | `src/ModelsDotDevSharp/Converters/InterleavedBooleanOrObjectConverter.cs` |
| New | `src/ModelsDotDevSharp/Converters/CostContextOverridePostProcessor.cs` |
| New | `src/ModelsDotDevSharp/Contexts/ModelInfoJsonContext.cs` |
| New | `src/ModelsDotDevSharp/Contexts/ModelMetadataJsonContext.cs` |
| New | `src/ModelsDotDevSharp/Contexts/CatalogJsonContext.cs` |
| Extend | `src/ModelsDotDevSharp/Models/AIProviderInfo.cs` |
| Extend | `src/ModelsDotDevSharp/Models/AIModelInfo.cs` |
| Extend | `src/ModelsDotDevSharp/Models/AIModelCostInfo.cs` |
| Extend | `src/ModelsDotDevSharp/Models/AIModelLimit.cs` |
| Rename | `src/ModelsDotDevSharp/Abstractions/IModelInfoProvider.cs` → `IModelInfoRepository.cs` |
| Rename | `src/ModelsDotDevSharp/ModelInfoProvider.cs` → `ModelInfoRepository.cs` |
| Delete | `src/ModelsDotDevSharp/Contexts/AIProviderJsonContext.cs` |
| Delete | `src/ModelsDotDevSharp/Contexts/AIProviderArrayJsonContext.cs` |

**Total: 28 new files, 4 extended, 2 renamed, 2 deleted.**

---

*This blueprint was generated by the `code-implementation-grilling` skill on 2026-06-17. The conversation context, the `CONTEXT.md` glossary, and this file are the three artifacts that drive the 0.1.0 release.*
