---
title: Foundation types — converters and enums
classification: Independent
blocked_by: []
parent: IMPLEMENTATION-2026-06-17.md - 0.1.0 release broadening the API surface from a single /api.json endpoint to all four upstream models.dev entrypoints (/api.json, /models.json, /catalog.json, plus logos). Three first-class repositories, ~13 new model types, four custom JSON converters, omnibus DI registration.
---

## Goal

Add the four new enums and three custom JSON converters that the rest of the 0.1.0 implementation depends on. The enums are leaf types with no upstream references; the converters are AOT-safe and built specifically to handle the polymorphic / dynamic-keyed wire shapes that `System.Text.Json` does not understand out of the box.

## What to build

### Enums (4 files, leaf types)

All in `src/ModelsDotDevSharp/Models/`, with the MIT license header, namespaced under `ModelsDotDevSharp`, and using `record`/mutable property conventions where applicable. Each enum maps the upstream's `snake_case` wire names to PascalCase C# values via `[JsonStringEnumMemberName(...)]`.

- `AIModelInterleavedField` — members `ReasoningContent` ("reasoning_content"), `ReasoningDetails` ("reasoning_details").
- `AIModelReasoningOptionType` — members `Toggle` ("toggle"), `BudgetTokens` ("budget_tokens"), `Effort` ("effort").
- `AIModelProviderShape` — members `Completions` ("completions"), `Responses` ("responses").
- `AIModelStatus` — members `Alpha` ("alpha"), `Beta` ("beta"), `Deprecated` ("deprecated").

### Converters (3 files, all `public sealed`, AOT/trim-safe)

All in `src/ModelsDotDevSharp/Converters/`, inheriting from `JsonConverter<T>`, with no reflection-based paths.

- `FlexibleDateOnlyConverter` — `JsonConverter<DateOnly?>`. Read: try `yyyy-MM-dd` first, then fall back to `yyyy-MM` (only the month is parseable in that case). Write: always emit `yyyy-MM-dd` for non-null values. Null in / null out.
- `ModelsJsonFlatteningConverter` — `JsonConverter<AIModelMetadata[]>`. Read: expect the wire's dictionary shape `{ "<provider>/<model>": { ... } }`, allocate the array, and for each entry use `JsonSerializer.Deserialize(ref reader, ModelMetadataJsonContext.Default.AIModelMetadata, options)` to deserialize the value, then populate `AIModelMetadata.Id` from the dictionary key. Write: emit a dictionary (used when the flattened view is round-tripped).
- `InterleavedBooleanOrObjectConverter` — `JsonConverter<AIModelInterleaved?>`. Read the four wire states:
  - `true` → record with `Field = null` (general support, no specific field)
  - `false` → `null`
  - object with a `field` string → record with `Field` mapped via a hand-written string-to-enum function
  - `null` → `null`

  String-to-enum mapping is done by hand (no `JsonStringEnumConverter` indirection) so the converter stays self-contained.

### Anti-slop reminders

- Do not add `static HttpClient` fields. No HTTP code lives in this ticket.
- Do not add `Version=` attributes to `ModelsDotDevSharp.csproj` — the existing `Directory.Packages.props` already covers all needed dependencies.
- The converters must be `public sealed` for AOT trim safety.
- The flattening converter must be registered against `AIModelMetadata[]` (the array, not the element) so the inner `JsonSerializer.Deserialize` call does not recurse into the converter.

## Recommended Workflow

### Step 1 — Add the four enums

Where: `src/ModelsDotDevSharp/Models/AIModelInterleavedField.cs`, `AIModelReasoningOptionType.cs`, `AIModelProviderShape.cs`, `AIModelStatus.cs`

- Add the MIT license header to each file.
- Declare each as a public enum in the `ModelsDotDevSharp` namespace.
- Annotate each member with `[JsonStringEnumMemberName("<wire-name>")]` using the wire names from the spec (e.g., `ReasoningContent` → `"reasoning_content"`).
- Use `[JsonConverter]` only at the file level if all members are strings; otherwise rely on `JsonStringEnumMemberName` on each member.

Verify: `dotnet build src/ModelsDotDevSharp.slnx` compiles with no new warnings (the four enums should not change the warning baseline).

### Step 2 — Add `FlexibleDateOnlyConverter`

Where: `src/ModelsDotDevSharp/Converters/FlexibleDateOnlyConverter.cs`

- Declare as `public sealed class FlexibleDateOnlyConverter : JsonConverter<DateOnly?>`.
- Override `Read`: peek at `reader.TokenType`; for `JsonTokenType.Null` return `null`; for `JsonTokenType.String`, attempt `DateOnly.TryParseExact` with `yyyy-MM-dd`, then fall back to `yyyy-MM`; if both fail throw `JsonException` with a clear message.
- Override `Write`: emit `JsonTokenType.Null` for null; otherwise format the value with `yyyy-MM-dd` and write as `JsonTokenType.String`.
- No reflection, no `static` caches that capture reflection metadata.

Verify: spot-test the converter in isolation (a small unit harness is acceptable even without a test project; one-time scratch code that is deleted after the ticket is fine).

### Step 3 — Add `ModelsJsonFlatteningConverter`

Where: `src/ModelsDotDevSharp/Converters/ModelsJsonFlatteningConverter.cs`

- Declare as `public sealed class ModelsJsonFlatteningConverter : JsonConverter<AIModelMetadata[]>`.
- Override `Read`:
  - Expect `JsonTokenType.StartObject` at the top level (the wire's dictionary).
  - Allocate `List<AIModelMetadata>` with a capacity hint.
  - For each property name (the wire key `"{provider}/{model}"`) and value, call `JsonSerializer.Deserialize(ref reader, ModelMetadataJsonContext.Default.AIModelMetadata, options)` and assign the returned record's `Id` to the key.
  - Return the list as an array.
- Override `Write`: emit a `JsonTokenType.StartObject` and serialize each `AIModelMetadata` keyed by its `Id`.
- Reference the source-gen context type from `ModelsDotDevSharp.Contexts` (a forward-reference; the context class is created in ticket #5 — the converter is allowed to reference the type symbol because both will be compiled in the same assembly).

Verify: build the project. A missing-context type symbol is expected to be a compile error at this point — that error is resolved by ticket #5. Record the error in the ticket's notes; do not "fix" by hardcoding the type.

### Step 4 — Add `InterleavedBooleanOrObjectConverter`

Where: `src/ModelsDotDevSharp/Converters/InterleavedBooleanOrObjectConverter.cs`

- Declare as `public sealed class InterleavedBooleanOrObjectConverter : JsonConverter<AIModelInterleaved?>`.
- Override `Read`:
  - For `JsonTokenType.True` → return a new `AIModelInterleaved` with `Field = null`.
  - For `JsonTokenType.False` or `JsonTokenType.Null` → return `null`.
  - For `JsonTokenType.StartObject` → deserialize to `AIModelInterleaved` (this type is created in ticket #2; the forward reference is expected).
- Override `Write`: serialize the inner `Field` as a `JsonTokenType.String` using the enum's `[JsonStringEnumMemberName]` (or write `true` when `Field == null` and `false` when the record itself is null).
- Hand-write a `string → AIModelInterleavedField?` mapping function (do not use `JsonStringEnumConverter`).

Verify: build the project. Forward-reference errors to `AIModelInterleaved` are expected and resolved by ticket #2.

### Step 5 — Build the project

Where: N/A

- Run `dotnet build src/ModelsDotDevSharp.slnx`.
- Expected: the project builds with the same warning count as the pre-implementation baseline, plus possibly forward-reference errors that will be resolved by tickets #2 and #5.
- Do not silence warnings with `#pragma warning disable` — the baseline is already clean.

Verify: build output captured. If there are no unresolved errors beyond forward references, the foundation is complete.

## Context pointers

**Files** -
- `IMPLEMENTATION-2026-06-17.md` (repo root) — phase 1 of the implementation order, decisions D-H.1, D-I, D-J, D-K, D-M, and the "Cross-cutting implementation notes → AOT and source-gen" section.
- `AGENTS.md` — AOT/trimming constraints (`IsTrimmable=true`, `PublishTrimmed=true`, `EnableAoTAnalyzer=true`); JSON source-gen only; no `static HttpClient`; Central Package Management.
- `src/ModelsDotDevSharp/Models/AIModelCostInfo.cs` (existing) — to confirm the convention for enum file layout and license header.

**ADRs** - None exist for this area; the relevant decisions are in the implementation blueprint.

**Domain terms** -
- *Interleaved reasoning* (CONTEXT.md) — the polymorphism that the boolean-or-object converter handles.
- *Reasoning option* (CONTEXT.md) — the type whose discriminator is `AIModelReasoningOptionType`.
- *Status* (CONTEXT.md) — the closed set of lifecycle values that `AIModelStatus` enumerates.

**Ledger records** - [D-I] `DateOnly?` with a custom AOT-safe converter that accepts both `YYYY-MM` and `YYYY-MM-DD`; [D-H.1] `Shape` is an `AIModelProviderShape` enum with `Completions` and `Responses`; [D-K] `reasoning_options` is a flat record with `Type: AIModelReasoningOptionType` (enum: `Toggle`, `BudgetTokens`, `Effort`); [D-M] `interleaved` is a nullable `AIModelInterleaved` record with a custom AOT-safe converter that handles boolean or object wire shape; [D-J] dynamic-keyed cost-related maps use `Dictionary<string, AIModelCostInfo>` (materially relevant when the converters are wired into JSON contexts in ticket #5).

## Acceptance criteria

- [ ] `AIModelInterleavedField`, `AIModelReasoningOptionType`, `AIModelProviderShape`, `AIModelStatus` are declared as public enums with `[JsonStringEnumMemberName]` matching the wire names from the spec.
- [ ] `FlexibleDateOnlyConverter` accepts both `YYYY-MM` and `YYYY-MM-DD` on read; emits `YYYY-MM-DD` on write; returns `null` for null.
- [ ] `ModelsJsonFlatteningConverter` reads `{ "<provider>/<model>": { ... } }` and produces a flat `AIModelMetadata[]` with each record's `Id` populated from the dictionary key.
- [ ] `InterleavedBooleanOrObjectConverter` produces the correct value for all four wire states (`true` → record with `Field = null`; `false` / `null` → `null`; object → record with `Field` from the wire's `field` value; the record itself is `null` for `null`).
- [ ] All three converters are `public sealed` and free of reflection-based paths.
- [ ] `dotnet build src/ModelsDotDevSharp.slnx` produces no new AOT/trim warnings beyond the pre-implementation baseline.

## Dependencies

**Blocked by** - None — can start immediately. This is the leaf of the dependency graph.
