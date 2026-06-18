---
title: New canonical model and supporting records
classification: Independent
blocked_by: [001-foundation-converters-and-enums]
parent: IMPLEMENTATION-2026-06-17.md - 0.1.0 release broadening the API surface from a single /api.json endpoint to all four upstream models.dev entrypoints (/api.json, /models.json, /catalog.json, plus logos). Three first-class repositories, ~13 new model types, four custom JSON converters, omnibus DI registration.
---

## Goal

Add the eight new record types that round out the 0.1.0 model layer: the four enums' companion records, three records that the extended `AIModelInfo` and `AIModelMetadata` reference, the canonical `AIModelMetadata` itself, and the `AICatalog` wrapper. All records are AOT-safe (mutable `get; set;` properties, no constructors with parameters, no inheritance).

## What to build

### Companion records for the enums (3 files)

- `src/ModelsDotDevSharp/Models/AIModelInterleaved.cs` — wraps the polymorphic `interleaved` field. Single nullable property `Field: AIModelInterleavedField?`. Bound to wire name `interleaved` on `AIModelInfo`. (Wire name on `AIModelMetadata`, if any, is the same.)
- `src/ModelsDotDevSharp/Models/AIModelReasoningOption.cs` — flat record with required `Type: AIModelReasoningOptionType` and nullable `Min: int?`, `Max: int?`, `Values: IReadOnlyList<string>?`. Bound to the wire's `reasoning_options` map values; the map itself is owned by `AIModelInfo`.
- `src/ModelsDotDevSharp/Models/AIModelProviderOverride.cs` — per-model provider override. Five nullable properties: `NpmPackageId: string?` (wire `npm`), `ApiUrl: string?` (wire `api`), `Shape: AIModelProviderShape?` (wire `shape`), `Body: Dictionary<string, JsonElement>?` (wire `body`), `Headers: Dictionary<string, string>?` (wire `headers`). C# property name is `ProviderOverride` to avoid collision with `AIProviderInfo`; the wire name remains `provider`.

### Records with dictionary payloads (2 files)

- `src/ModelsDotDevSharp/Models/AIModelExperimental.cs` — single property `Modes: Dictionary<string, AIModelCostInfo>?`. Bound to wire `experimental`. The `AIModelCostInfo` is the existing type (extended in ticket #3); this record does not introduce a new cost shape.
- `src/ModelsDotDevSharp/Models/AICatalog.cs` — combined view of the catalog endpoint. Two properties: `Models: Dictionary<string, AIModelMetadata>` (wire `models`) and `Providers: Dictionary<string, AIProviderInfo>` (wire `providers`). Both dictionaries are required (the wire is a single object, not a stream).

### Simple records (2 files)

- `src/ModelsDotDevSharp/Models/AIModelWeightInfo.cs` — `Label: string` and `Url: string` (wire `label` / `url`). Used as the value type of `AIModelMetadata.Weights`.
- `src/ModelsDotDevSharp/Models/AIModelBenchmark.cs` — required `Name: string`, `Score: double`, `Metric: string`, `Source: string`; nullable `Harness: string?`, `Dataset: string?`, `Version: string?`, `Variant: string?`; optional `Date: DateOnly?` (uses `FlexibleDateOnlyConverter` once registered). Bound to the wire's `benchmarks` map values on `AIModelMetadata`.

### The canonical model (1 file)

- `src/ModelsDotDevSharp/Models/AIModelMetadata.cs` — 16 properties mapped to the `/models.json` wire shape:
  - `Id: string?` — bound to wire `id`; will be **null after deserialization from `/models.json`** because the flattening converter (ticket #1) populates it from the dictionary key, not the wire's `id` field. Consumers in catalog mode (where the dictionary shape is preserved) must use the dictionary key, not the `Id` property.
  - `Name: string` (wire `name`)
  - `Family: string` (wire `family`)
  - `Attachment: bool` (wire `attachment`)
  - `Reasoning: bool` (wire `reasoning`)
  - `ToolCall: bool` (wire `tool_call`)
  - `StructuredOutput: bool` (wire `structured_output`)
  - `Temperature: bool` (wire `temperature`)
  - `Knowledge: DateOnly?` (wire `knowledge`)
  - `ReleaseDate: DateOnly?` (wire `release_date`)
  - `LastUpdatedDate: DateOnly?` (wire `last_updated`)
  - `OpenWeights: bool` (wire `open_weights`)
  - `Modalities: AIModelModalities` (wire `modalities`)
  - `Limit: AIModelLimit` (wire `limit`)
  - `Weights: IReadOnlyList<AIModelWeightInfo>?` (wire `weights`)
  - `Benchmarks: IReadOnlyList<AIModelBenchmark>?` (wire `benchmarks`)

### Anti-slop reminders

- Records are C# `record` types with mutable `get; set;` properties (no `init`) — match the existing convention in `Models/AIModelInfo.cs`.
- Every source file starts with the MIT license header.
- The dictionary value types (`AIModelCostInfo`, `AIProviderInfo`, `AIModelMetadata`) are the same types used elsewhere; do not introduce parallel "metadata" variants.
- `AIModelMetadata.Id` is intentionally nullable in C#; the `?? operator` is not appropriate here because `null` is a meaningful state (catalog mode vs canonical mode).

## Recommended Workflow

### Step 1 — Add the three simple / leaf records

Where: `src/ModelsDotDevSharp/Models/AIModelInterleaved.cs`, `AIModelReasoningOption.cs`, `AIModelWeightInfo.cs`, `AIModelBenchmark.cs`

- Add the MIT license header to each file.
- Declare each as `public record <Name> { ... }` with mutable `get; set;` properties.
- Apply `[JsonPropertyName("<wire-name>")]` to each property per the wire names above.
- For `AIModelBenchmark.Date`, leave the property as `DateOnly?` — the converter is wired in via the JSON context (ticket #5), not the property itself.
- For `AIModelReasoningOption.Values`, use `IReadOnlyList<string>?` to keep the API surface read-only; populate with the wire's array.

Verify: `dotnet build src/ModelsDotDevSharp.slnx` compiles (forward references to `AIModelCostInfo` from `AIModelExperimental` are expected to be unresolved; resolve those in step 2).

### Step 2 — Add the records with dictionary payloads

Where: `src/ModelsDotDevSharp/Models/AIModelExperimental.cs`, `AIModelProviderOverride.cs`, `AICatalog.cs`

- `AIModelExperimental`: one property `Modes: Dictionary<string, AIModelCostInfo>?` with `[JsonPropertyName("modes")]`. (The dictionary key is the mode name; the value is the existing `AIModelCostInfo` type.)
- `AIModelProviderOverride`: five properties per the spec. Use `Dictionary<string, JsonElement>?` for `Body` so the wire's arbitrary body shape is preserved without writing a typed schema for it.
- `AICatalog`: two required `Dictionary<...>` properties. Use `Dictionary<string, AIModelMetadata>` and `Dictionary<string, AIProviderInfo>`. Mark both properties required (not nullable) — the wire always has both, and a missing key is a wire error, not a normal state.

Verify: build. Forward references to `AIModelMetadata` and `AIProviderInfo` are real (both types exist or will exist by end of this ticket). Forward references to `AIModelCostInfo` (used by `AIModelExperimental`) are real.

### Step 3 — Add `AIModelMetadata`

Where: `src/ModelsDotDevSharp/Models/AIModelMetadata.cs`

- 16 properties, with the wire names from the spec table above.
- Use existing types where they exist: `AIModelModalities` (existing, in `Models/AIModelModalities.cs`), `AIModelLimit` (existing, in `Models/AIModelLimit.cs`). `AIModelLimit` will have `InputTokens` widened from `int` to `int?` in ticket #3 — declare `AIModelMetadata.Limit` against the post-ticket-#3 shape (nullable `int?`).
- `Id` is `string?` and is **not** the source of truth for canonical mode — the dictionary key is. Document this in the XML comment for the property.

Verify: build. The flattening converter references `ModelMetadataJsonContext.Default.AIModelMetadata` (from ticket #1). The forward reference to the JSON context is resolved in ticket #5; build errors related to the missing context are expected.

### Step 4 — Build the project

Where: N/A

- Run `dotnet build src/ModelsDotDevSharp.slnx`.
- Expected: any remaining errors are forward references to JSON context types from ticket #5 (acceptable). No new warnings beyond the baseline.

Verify: build output captured; no warnings introduced by the new types.

## Context pointers

**Files** -
- `IMPLEMENTATION-2026-06-17.md` (repo root) — phases 2 and 3 of the implementation order, decisions D-A (two model entities), D-B (flattening converter populates `Id`), D-F (no shared base), D-G (model every new field with a typed shape), D-H (per-model `provider` sub-object name), D-H.1, D-K.
- `AGENTS.md` — record types with mutable `get; set;` properties; MIT license header; `AIModelInfo` is the reference for the convention.
- `src/ModelsDotDevSharp/Models/AIModelInfo.cs` (existing) — reference for property declaration style, license header, and JSON property name binding.
- `src/ModelsDotDevSharp/Models/AIModalities.cs` and `AIModelLimit.cs` (existing) — used as value types in `AIModelMetadata`.

**ADRs** - None exist for this area; the relevant decisions are in the implementation blueprint.

**Domain terms** -
- *Provider override* (CONTEXT.md) — the relationship named by `AIModelProviderOverride`.
- *Mode* (CONTEXT.md) — the dictionary key for `AIModelExperimental.Modes`.
- *Interleaved reasoning* (CONTEXT.md) — the polymorphism wrapped by `AIModelInterleaved`.
- *Reasoning option* (CONTEXT.md) — the polymorphic configuration entry wrapped by `AIModelReasoningOption`.

**Ledger records** - [D-A] Two distinct entities: `AIModelInfo` (per-provider view) and `AIModelMetadata` (canonical view); [D-B] `AIModelMetadata.Id` is populated by the flattening converter at deserialization time; [D-F] Two independent types, primitive fields duplicated (no shared base, no inheritance); [D-G] Model every new field with a dedicated typed shape; [D-H] The per-model `provider` sub-object is named `AIModelProviderOverride` for the relationship, not the role; [D-H.1] `Shape` field on the override is the `AIModelProviderShape` enum; [D-K] `reasoning_options` is a flat record with `Type: AIModelReasoningOptionType`.

## Acceptance criteria

- [ ] `AIModelInterleaved`, `AIModelReasoningOption`, `AIModelWeightInfo`, `AIModelBenchmark`, `AIModelExperimental`, `AIModelProviderOverride`, `AICatalog`, `AIModelMetadata` are declared as public `record` types with mutable properties.
- [ ] Every wire name is bound via `[JsonPropertyName(...)]` and matches the upstream schema byte-for-byte (notably `input_audio`, `output_audio`, `cache_read`, `cache_write`, `structured_output`, `tool_call`, `open_weights`, `last_updated`, `knowledge`, `release_date`).
- [ ] `AIModelMetadata.Id` is `string?` and is documented as null after deserialization from the dictionary-shaped endpoint.
- [ ] `AIModelProviderOverride` is named for the relationship (per D-H) and the C# property on `AIModelInfo` is `ProviderOverride` to avoid collision with `AIProviderInfo`.
- [ ] `AICatalog.Models` and `AICatalog.Providers` are required `Dictionary<...>` properties (not nullable).
- [ ] `dotnet build src/ModelsDotDevSharp.slnx` produces no new warnings beyond the pre-implementation baseline (excluding forward-reference errors resolved by ticket #5).

## Dependencies

**Blocked by** - `001-foundation-converters-and-enums` — the converters and enums must exist before the records that reference them.
