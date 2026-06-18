---
title: JSON source-gen contexts and cost override post-processor
classification: Independent
blocked_by: [001-foundation-converters-and-enums, 002-new-canonical-model-records, 003-extend-existing-model-types]
parent: IMPLEMENTATION-2026-06-17.md - 0.1.0 release broadening the API surface from a single /api.json endpoint to all four upstream models.dev entrypoints (/api.json, /models.json, /catalog.json, plus logos). Three first-class repositories, ~13 new model types, four custom JSON converters, omnibus DI registration.
---

## Goal

Add the three per-endpoint `JsonSerializerContext` source-gen contexts and the `CostContextOverridePostProcessor` that finalizes the cost override surface after deserialization. Replace the two old contexts (`AIProviderJsonContext`, `AIProviderArrayJsonContext`) with the per-endpoint organization. This is the last step that resolves the AOT/trim warning baseline.

## What to build

### Three per-endpoint `JsonSerializerContext` partial classes

All in `src/ModelsDotDevSharp/Contexts/`, all `partial class` and inheriting from `JsonSerializerContext`. Source generation emits type info at build time, so reflection-based deserialization is not used at runtime.

- `ModelInfoJsonContext` — registers the per-provider view types: `AIProviderInfo`, `AIModelInfo`, `AIModelCostInfo`, `AIModelModalities`, `AIModelLimit`, `AIModelCostTier`, `AIModelTierInfo`, plus the new types referenced by `AIModelInfo` (extended in ticket #3). Converters: `FlexibleDateOnlyConverter`, `InterleavedBooleanOrObjectConverter`. Replaces the old `AIProviderJsonContext` and `AIProviderArrayJsonContext`.
- `ModelMetadataJsonContext` — registers the canonical view types: `AIModelMetadata[]`, `AIModelMetadata`, `AIModelWeightInfo`, `AIModelBenchmark`, `AIModelLimit` (registered explicitly to avoid reflection-based fallback), `AIModelModalities`. Converters: `FlexibleDateOnlyConverter`, `ModelsJsonFlatteningConverter`.
- `CatalogJsonContext` — registers the union: `AICatalog`, `AIModelMetadata` (as part of the `Models` dictionary), `AIProviderInfo` (as part of the `Providers` dictionary), and the per-provider view types (so the `Providers` dictionary deserializes end-to-end without reflection). Per-endpoint shape is the trade-off accepted in TDP-4: a type rename must be replicated in two contexts (mitigation: code-review checklist).

### `CostContextOverridePostProcessor`

`src/ModelsDotDevSharp/Converters/CostContextOverridePostProcessor.cs`

- Public static class with one method: `public static void Process(AIModelCostInfo cost, JsonSerializerOptions options)`.
- Behavior:
  1. If `cost.ContextOverridesRaw` is null or empty, return immediately.
  2. For each `key → value` pair in `ContextOverridesRaw`, where the key starts with the prefix `context_over_`:
     - Strip the `context_over_` prefix to get the suffix.
     - Deserialize the `JsonElement` value via `JsonSerializer.Deserialize<AIModelCostInfo>(value, options)` (using the `AIModelCostInfo` source-gen type from `ModelInfoJsonContext`).
     - Add the deserialized record to `cost.ContextOverrides[suffix]`.
  3. Clear `cost.ContextOverridesRaw` (set to `null` or `new()`).
- AOT-safe: no reflection. The `Deserialize` call uses the source-gen type info.

### Delete the old contexts

- Delete `src/ModelsDotDevSharp/Contexts/AIProviderJsonContext.cs`.
- Delete `src/ModelsDotDevSharp/Contexts/AIProviderArrayJsonContext.cs`.

### Anti-slop reminders

- All three contexts must be `partial class` so the source generator can extend them.
- All converters are `public sealed` (already enforced by ticket #1).
- `AIModelLimit` must be registered explicitly in `ModelMetadataJsonContext` and `CatalogJsonContext` even though it is only a property of `AIModelMetadata` — without explicit registration, the source-gen falls back to reflection.
- The post-processor is called *after* deserialization completes, not as part of the deserialization pipeline. The repository implementations (ticket #6) are responsible for invoking it on every deserialized `AIModelCostInfo`.
- The `context_over_` prefix is the wire's prefix; the convention in `CONTEXT.md` is to drop the prefix and use the suffix as the dictionary key.

## Recommended Workflow

### Step 1 — Add `ModelInfoJsonContext`

Where: `src/ModelsDotDevSharp/Contexts/ModelInfoJsonContext.cs`

- Declare `public partial class ModelInfoJsonContext : JsonSerializerContext`.
- Add `[JsonSerializable(typeof(AIProviderInfo))]`, `[JsonSerializable(typeof(AIProviderInfo[]))]`, `[JsonSerializable(typeof(AIModelInfo))]`, `[JsonSerializable(typeof(AIModelCostInfo))]`, plus the smaller record types: `AIModelModalities`, `AIModelLimit`, `AIModelCostTier`, `AIModelTierInfo`, `AIModelStatus`, `AIModelReasoningOption`, `AIModelInterleaved`, `AIModelExperimental`, `AIModelProviderOverride`.
- Set `ConverterTypes` via the `[JsonSourceGenerationOptions]` attribute: `typeof(FlexibleDateOnlyConverter)`, `typeof(InterleavedBooleanOrObjectConverter)`.

Verify: build. The source generator emits the type info; if a `[JsonSerializable]` type is missing, the build reports an AOT/trim warning — fix by adding the missing registration.

### Step 2 — Add `ModelMetadataJsonContext`

Where: `src/ModelsDotDevSharp/Contexts/ModelMetadataJsonContext.cs`

- Declare `public partial class ModelMetadataJsonContext : JsonSerializerContext`.
- Add `[JsonSerializable(typeof(AIModelMetadata))]`, `[JsonSerializable(typeof(AIModelMetadata[]))]`, plus the value types referenced by `AIModelMetadata`: `AIModelWeightInfo`, `AIModelBenchmark`, `AIModelLimit`, `AIModelModalities`.
- Set `ConverterTypes`: `typeof(FlexibleDateOnlyConverter)`, `typeof(ModelsJsonFlatteningConverter)`.

Verify: build. The flattening converter's forward reference to `ModelMetadataJsonContext.Default.AIModelMetadata` is resolved by this step.

### Step 3 — Add `CatalogJsonContext`

Where: `src/ModelsDotDevSharp/Contexts/CatalogJsonContext.cs`

- Declare `public partial class CatalogJsonContext : JsonSerializerContext`.
- Add `[JsonSerializable(typeof(AICatalog))]`, `[JsonSerializable(typeof(AIModelMetadata))]`, `[JsonSerializable(typeof(AIProviderInfo))]`, `[JsonSerializable(typeof(AIProviderInfo[]))]`, plus the union of types referenced by both.
- Set `ConverterTypes`: `typeof(FlexibleDateOnlyConverter)`, `typeof(InterleavedBooleanOrObjectConverter)`.

Verify: build. A rename of `AIModelMetadata` (or any shared type) requires updating two contexts (`ModelInfoJsonContext` and `CatalogJsonContext`).

### Step 4 — Add `CostContextOverridePostProcessor`

Where: `src/ModelsDotDevSharp/Converters/CostContextOverridePostProcessor.cs`

- Declare `public static class CostContextOverridePostProcessor`.
- Add the `Process(AIModelCostInfo cost, JsonSerializerOptions options)` method per the spec.
- Use `ModelInfoJsonContext.Default.AIModelCostInfo` (the source-gen type info) when calling `JsonSerializer.Deserialize`.
- The prefix to strip is `context_over_` (12 characters). A null or empty `ContextOverridesRaw` is a no-op.

Verify: build.

### Step 5 — Delete the old contexts

Where: `src/ModelsDotDevSharp/Contexts/AIProviderJsonContext.cs`, `src/ModelsDotDevSharp/Contexts/AIProviderArrayJsonContext.cs`

- Delete both files. The legacy `ModelInfoProvider.GetProviderInfosAsync` still references `AIProviderArrayJsonContext.Default.AIProviderInfoArray`; this reference is resolved by ticket #6 (which renames and refactors the implementation to use `ModelInfoJsonContext.Default.AIProviderInfoArray`).

Verify: build. The expected intermediate error is `AIProviderArrayJsonContext` not found (resolved by ticket #6).

### Step 6 — Build the project

Where: N/A

- Run `dotnet build src/ModelsDotDevSharp.slnx`.
- Expected: any remaining errors are references to the deleted legacy contexts in `ModelInfoProvider.cs` (resolved by ticket #6). No new AOT/trim warnings beyond the baseline.

Verify: build output captured; no new warnings introduced.

## Context pointers

**Files** -
- `IMPLEMENTATION-2026-06-17.md` (repo root) — phases 6 and 7 of the implementation order, decisions TDP-4 (per-endpoint JSON contexts), D-I, D-J, D-M; the "AOT and source-gen" section; the "Recursion guards" section; the "JSON context type duplication" cross-cutting note.
- `AGENTS.md` — AOT/trimming constraints; the two existing JSON contexts (`AIProviderJsonContext`, `AIProviderArrayJsonContext`) live in `Contexts/`; the rule "when you add a new serializable model, you must also add a matching `[JsonSerializable(typeof(YourType))]` partial context".
- `src/ModelsDotDevSharp/Contexts/AIProviderJsonContext.cs` (existing) — the file being replaced.
- `src/ModelsDotDevSharp/Contexts/AIProviderArrayJsonContext.cs` (existing) — the file being replaced.
- `src/ModelsDotDevSharp/ModelInfoProvider.cs` (existing) — the implementation that will switch to the new contexts in ticket #6.

**ADRs** - None exist for this area; the relevant decisions are in the implementation blueprint.

**Domain terms** -
- *Context override* (CONTEXT.md) — the dynamic-keyed tiered cost map that the post-processor finalizes.

**Ledger records** - [D-I] `DateOnly?` with the flexible converter; [D-J] Dynamic-keyed cost-related maps use `Dictionary<string, AIModelCostInfo>`; [D-M] The interleaved converter handles the boolean-or-object polymorphism; [TDP-4] Per-endpoint JSON contexts (`ModelInfoJsonContext`, `ModelMetadataJsonContext`, `CatalogJsonContext`).

## Acceptance criteria

- [ ] `ModelInfoJsonContext`, `ModelMetadataJsonContext`, `CatalogJsonContext` are `public partial class` and inherit from `JsonSerializerContext`.
- [ ] `ModelInfoJsonContext` registers per-provider types and uses `FlexibleDateOnlyConverter` + `InterleavedBooleanOrObjectConverter`.
- [ ] `ModelMetadataJsonContext` registers canonical types and uses `FlexibleDateOnlyConverter` + `ModelsJsonFlatteningConverter`.
- [ ] `CatalogJsonContext` registers the union of per-provider and canonical types.
- [ ] `AIModelLimit` is registered explicitly in `ModelMetadataJsonContext` and `CatalogJsonContext`.
- [ ] `CostContextOverridePostProcessor.Process` reads from `ContextOverridesRaw`, strips the `context_over_` prefix, deserializes each value as `AIModelCostInfo` using source-gen type info, populates `ContextOverrides`, and clears the raw bag.
- [ ] `AIProviderJsonContext.cs` and `AIProviderArrayJsonContext.cs` are deleted.
- [ ] `dotnet build src/ModelsDotDevSharp.slnx` produces no new AOT/trim warnings beyond the pre-implementation baseline (excluding the expected `ModelInfoProvider` reference errors resolved by ticket #6).

## Dependencies

**Blocked by** -
- `001-foundation-converters-and-enums` — the converters are wired into the contexts' `ConverterTypes`.
- `002-new-canonical-model-records` — the canonical types must exist before being registered in `ModelMetadataJsonContext` and `CatalogJsonContext`.
- `003-extend-existing-model-types` — the per-provider types must be in their post-extension shape (with `LogoUrl`, `ContextOverrides`, the new `AIModelInfo` properties).
