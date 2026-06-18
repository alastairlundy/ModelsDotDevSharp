---
title: Extend existing model types
classification: Independent
blocked_by: [001-foundation-converters-and-enums, 002-new-canonical-model-records]
parent: IMPLEMENTATION-2026-06-17.md - 0.1.0 release broadening the API surface from a single /api.json endpoint to all four upstream models.dev entrypoints (/api.json, /models.json, /catalog.json, plus logos). Three first-class repositories, ~13 new model types, four custom JSON converters, omnibus DI registration.
---

## Goal

Modify the four existing model types — `AIModelLimit`, `AIModelCostInfo`, `AIProviderInfo`, `AIModelInfo` — to add the 0.1.0 fields, widen the date types to `DateOnly?`, add the computed `LogoUrl`, and add the `[JsonExtensionData]` bag for the cost override post-processor (ticket #5). The biggest change is `AIModelInfo`; the others are small surgical additions.

## What to build

### `AIModelLimit` — small widening

`src/ModelsDotDevSharp/Models/AIModelLimit.cs`

- Change `InputTokens: int` to `InputTokens: int?` (the wire sometimes omits the field; null is the wire-absent state).
- All other properties unchanged.

### `AIModelCostInfo` — typed override surface plus raw bag

`src/ModelsDotDevSharp/Models/AIModelCostInfo.cs`

- Add `ContextOverrides: Dictionary<string, AIModelCostInfo>` — a typed, public, mutable dictionary (initialized to `new()`). The dictionary key matches the wire's `context_over_{N}` suffix (the `context_over_` prefix is dropped, per the convention in `CONTEXT.md` → "Context override").
- Add `[JsonExtensionData] Dictionary<string, JsonElement>? ContextOverridesRaw` — the raw bag the deserializer fills with any unknown properties on the cost object. The post-processor (ticket #5) reads from this bag, populates `ContextOverrides`, then clears it.
- Annotate `ContextOverridesRaw` with `[EditorBrowsable(EditorBrowsableState.Never)]` so the property is hidden from IDE intellisense and consumers.
- Existing properties (`Input`, `Output`, `CacheRead`, `CacheWrite`) are unchanged.

### `AIProviderInfo` — computed `LogoUrl`

`src/ModelsDotDevSharp/Models/AIProviderInfo.cs`

- Add `public string LogoUrl => $"https://models.dev/logos/{Id}.svg";` with `[JsonIgnore]`.
- The URL is **hardcoded** to `https://models.dev`; it does not respect the configured `ModelsDevOptions.BaseAddress`. This is a known limitation flagged in the blueprint; do not "fix" it in this ticket.

### `AIModelInfo` — the biggest extension

`src/ModelsDotDevSharp/Models/AIModelInfo.cs`

Rename and widen date properties:
- `KnowledgeCutOffDate: string` → `Knowledge: DateOnly?` (wire name `knowledge`).
- `ReleaseDate: string` → `ReleaseDate: DateOnly?` (wire name `release_date`).
- `LastUpdatedDate: string` → `LastUpdatedDate: DateOnly?` (wire name `last_updated`).

Add eight new nullable properties:
- `Status: AIModelStatus?` (wire `status`).
- `ReasoningOptions: Dictionary<string, AIModelReasoningOption>?` (wire `reasoning_options`).
- `Interleaved: AIModelInterleaved?` (wire `interleaved`).
- `Modes: Dictionary<string, AIModelCostInfo>?` (wire `modes`).
- `Experimental: AIModelExperimental?` (wire `experimental`).
- `Headers: Dictionary<string, string>?` (wire `headers`).
- `Body: Dictionary<string, JsonElement>?` (wire `body`).
- `ProviderOverride: AIModelProviderOverride?` (wire `provider`; C# name is `ProviderOverride` to avoid collision with `AIProviderInfo`).

Existing properties (`Id`, `Name`, `Cost`, `Limit`, `Modalities`, etc.) are unchanged.

### Anti-slop reminders

- The `[JsonExtensionData]` bag on `AIModelCostInfo` is the only place this pattern is used in the library. Do not retrofit it to other types.
- The `LogoUrl` is a *computed* property, not a `[JsonPropertyName]`-bound one. Use `[JsonIgnore]`.
- The wire's `provider` sub-object is mapped to the C# property `ProviderOverride` on `AIModelInfo` — this is the *only* place the C# name diverges from the wire name in the model layer.
- The `Knowledge` / `ReleaseDate` / `LastUpdatedDate` change from `string` to `DateOnly?` is a **breaking change** for any consumer who reads those properties as strings. The blueprint accepts this as part of the clean break (TDP-1, first release).

## Recommended Workflow

### Step 1 — Widen `AIModelLimit.InputTokens` to `int?`

Where: `src/ModelsDotDevSharp/Models/AIModelLimit.cs`

- Change the property type from `int` to `int?`.
- The default value (when the wire omits the field) becomes `null`.

Verify: `dotnet build src/ModelsDotDevSharp.slnx` compiles. No other source in the project reads `InputTokens`, so the change is local.

### Step 2 — Extend `AIModelCostInfo` with the override surface and the raw bag

Where: `src/ModelsDotDevSharp/Models/AIModelCostInfo.cs`

- Add the `using System.ComponentModel;` and `using System.Text.Json;` and `using System.Text.Json.Serialization;` imports as needed (likely already imported via `GlobalUsings.cs`).
- Add the `ContextOverrides` property initialized to `new()`.
- Add the `ContextOverridesRaw` property with `[JsonExtensionData]` and `[EditorBrowsable(EditorBrowsableState.Never)]`.
- The `[EditorBrowsable]` attribute requires `using System.ComponentModel;`. If `GlobalUsings.cs` does not import it, add the import to that file (this is the only edit to `GlobalUsings.cs` in this ticket; ticket #7 may add more).

Verify: build. The `[JsonExtensionData]` and `[EditorBrowsable]` attributes are valid; no runtime concerns at this point.

### Step 3 — Add `LogoUrl` to `AIProviderInfo`

Where: `src/ModelsDotDevSharp/Models/AIProviderInfo.cs`

- Add `using System.Text.Json.Serialization;` if not already imported.
- Add the computed `LogoUrl` property with `[JsonIgnore]`.
- The expression body `=> $"https://models.dev/logos/{Id}.svg"` is sufficient — no `get`/`set` is needed.

Verify: build. No new warnings.

### Step 4 — Update `AIModelInfo` (the biggest change)

Where: `src/ModelsDotDevSharp/Models/AIModelInfo.cs`

- Rename `KnowledgeCutOffDate` → `Knowledge`; change type to `DateOnly?`; update the `[JsonPropertyName("knowledge")]` attribute (which already exists with the wire name).
- Change `ReleaseDate` and `LastUpdatedDate` from `string` to `DateOnly?`; the wire names are unchanged.
- Add the eight new nullable properties with the correct `[JsonPropertyName]` attributes.
- Verify the existing `Id`, `Name`, `Cost`, `Limit`, `Modalities` properties are unchanged.

Verify: build. Forward references to the new record types (`AIModelStatus`, `AIModelReasoningOption`, `AIModelInterleaved`, `AIModelExperimental`, `AIModelProviderOverride`) are real and resolved by ticket #2 (already complete). Date-only parsing is handled by the converter wired in via the JSON context in ticket #5.

### Step 5 — Build the project

Where: N/A

- Run `dotnet build src/ModelsDotDevSharp.slnx`.
- Expected: clean build with the same warning count as the pre-implementation baseline, plus possibly the unresolved JSON context forward references (resolved by ticket #5).

Verify: build output captured; no new warnings.

## Context pointers

**Files** -
- `IMPLEMENTATION-2026-06-17.md` (repo root) — phase 4 of the implementation order, decisions D-A, D-E, D-F, D-G, D-I, D-J, D-M; the "Extended existing types" table; the "AOT and source-gen" section; the "Known behaviors worth knowing" section in `AGENTS.md` for the legacy `NullReferenceException` → `ArgumentException` pattern.
- `AGENTS.md` — AOT/trimming constraints; record types with mutable properties; `JsonPropertyName` byte-for-byte fidelity to upstream.
- `src/ModelsDotDevSharp/Models/AIModelInfo.cs` (existing) — the file being modified; the convention reference.
- `src/ModelsDotDevSharp/Models/AIModelCostInfo.cs` (existing) — the file being extended with `ContextOverrides` and `ContextOverridesRaw`.
- `src/ModelsDotDevSharp/Models/AIModelLimit.cs` (existing) — the file being widened.
- `src/ModelsDotDevSharp/Models/AIProviderInfo.cs` (existing) — the file being extended with `LogoUrl`.

**ADRs** - None exist for this area; the relevant decisions are in the implementation blueprint.

**Domain terms** -
- *Logo* (CONTEXT.md) — the URL string, not a fetched asset.
- *Context override* (CONTEXT.md) — the dynamic-keyed tiered cost map added to `AIModelCostInfo`.
- *Provider override* (CONTEXT.md) — the per-model sub-object exposed as `AIModelInfo.ProviderOverride`.
- *Mode* (CONTEXT.md) — the alternative pricing mode dictionary on `AIModelInfo.Modes`.
- *Experimental mode* (CONTEXT.md) — the wrapper type `AIModelExperimental`.
- *Interleaved reasoning* (CONTEXT.md) — the polymorphism exposed as `AIModelInfo.Interleaved`.
- *Reasoning option* (CONTEXT.md) — the typed configuration entry in `AIModelInfo.ReasoningOptions`.
- *Status* (CONTEXT.md) — the lifecycle enum exposed as `AIModelInfo.Status`.

**Ledger records** - [D-A] Two distinct model entities; [D-E] `LogoUrl` is a computed string property on `AIProviderInfo` (`$"https://models.dev/logos/{Id}.svg"`); the library does not fetch SVG bytes; [D-F] Two independent types, primitive fields duplicated; [D-G] Model every new field with a dedicated typed shape; [D-I] `knowledge` / `release_date` / `last_updated` are `DateOnly?` with the flexible converter; [D-J] `AIModelCostInfo.ContextOverrides`, `AIModelInfo.Modes`, and `AIModelExperimental.Modes` are `Dictionary<string, AIModelCostInfo>`; [D-M] `interleaved` is a nullable `AIModelInterleaved` record with a custom AOT-safe converter.

## Acceptance criteria

- [ ] `AIModelLimit.InputTokens` is `int?`.
- [ ] `AIModelCostInfo` exposes `ContextOverrides: Dictionary<string, AIModelCostInfo>` and a hidden `ContextOverridesRaw` (decorated with `[EditorBrowsable(EditorBrowsableState.Never)]`).
- [ ] `AIProviderInfo.LogoUrl` returns the expected `https://models.dev/logos/{Id}.svg` URL and is excluded from JSON deserialization.
- [ ] `AIModelInfo` includes the eight new nullable properties (`Status`, `ReasoningOptions`, `Interleaved`, `Modes`, `Experimental`, `Headers`, `Body`, `ProviderOverride`) bound to the correct wire names.
- [ ] `AIModelInfo.KnowledgeCutOffDate` is renamed to `AIModelInfo.Knowledge` (type `DateOnly?`); `ReleaseDate` and `LastUpdatedDate` are `DateOnly?`.
- [ ] `AIModelInfo.ProviderOverride` is the C# name; the wire name remains `provider`.
- [ ] `dotnet build src/ModelsDotDevSharp.slnx` produces no new warnings beyond the pre-implementation baseline (excluding forward-reference errors resolved by ticket #5).

## Dependencies

**Blocked by** -
- `001-foundation-converters-and-enums` — the enums and converters must exist before the properties that reference them.
- `002-new-canonical-model-records` — the new record types referenced by the new `AIModelInfo` properties must exist.
