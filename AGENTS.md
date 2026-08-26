# AGENTS.md

## Project
- C# library that wraps the public models.dev API (`https://models.dev/api.json`, `/models.json`, `/catalog.json`) for .NET consumers.
- Single project: `src/ModelsDotDevSharp/ModelsDotDevSharp.csproj`; solution is `src/ModelsDotDevSharp.slnx` (XML solution format).
- `TargetFramework=net10.0`, `LangVersion=14`, `ImplicitUsings=enable`, `Nullable=enable`. No multi-targeting. No `global.json` — SDK version is whatever is installed locally.

## Build, package, restore
- Build: `dotnet build src/ModelsDotDevSharp.slnx` (or `dotnet build src/ModelsDotDevSharp/ModelsDotDevSharp.csproj`).
- Pack: `dotnet pack src/ModelsDotDevSharp/ModelsDotDevSharp.csproj` — `GeneratePackageOnBuild=true` is set, so a `.nupkg` is produced on build.
- Test: `dotnet test src/ModelsDotDevSharp.slnx` runs the TUnit test project at `tests/ModelsDotDevSharp.Tests/`. Tests DO exist — do not treat `dotnet test` as a no-op.
- No CI workflows exist in `.github/workflows/`. Dependabot (`.github/dependabot.yml`) updates NuGet weekly and looks for GitHub Actions (none yet).

## Dependency management — Central Package Management
- `src/Directory.Packages.props` has `ManagePackageVersionsCentrally=true`.
- All NuGet versions live in `Directory.Packages.props`. The `.csproj` only declares `<PackageReference Include="..." />` with no `Version=`.
- To add/update a package, edit `src/Directory.Packages.props` only. Do not add `Version=` attributes to the `.csproj`.
- Current packages: `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.9, `Microsoft.Extensions.Http` 10.0.9, `Microsoft.Extensions.Options` 10.0.9 (all at the .NET 10.0.9 patch track) + `TUnit` 1.65.51 (test project only).

## AOT / trimming constraints
- The project sets `IsTrimmable=true`, `EnableAoTAnalyzer=true`, `PublishTrimmed=true`. Treat the library as AOT- and trim-safe.
- JSON deserialization goes through `System.Text.Json` **source generators** only — no reflection. The contexts live at:
  - `src/ModelsDotDevSharp/Contexts/ModelInfoJsonContext.cs` (providers + models: `AIProviderInfo`, `AIProviderInfo[]`, `AIModelInfo`, `AIModelInfo[]`, `AIModelCostInfo`, `AIModelModalities`, `AIModelLimit`, `AIModelCostTier`, `AIModelTierInfo`, `AIModelStatus`, `AIModelReasoningOption`, `AIModelInterleaved`, `AIModelExperimental`, `AIModelProviderOverride`)
  - `src/ModelsDotDevSharp/Contexts/ModelMetadataJsonContext.cs` (`AIModelMetadata`, `AIModelMetadata[]`, `AIModelWeightInfo`, `AIModelBenchmark`, `AIModelLimit`, `AIModelModalities`)
  - `src/ModelsDotDevSharp/Contexts/CatalogJsonContext.cs` (`AICatalog` and the provider/model types used by the catalog)
- Custom `JsonConverter`s live in `src/ModelsDotDevSharp/Converters/` and are wired into the contexts via `[JsonSourceGenerationOptions(Converters = [...])]`: `FlexibleDateOnlyConverter`, `InterleavedBooleanOrObjectConverter`, `AIProviderInfoArrayFlatteningConverter`, `AIModelInfoArrayFlatteningConverter`, `ModelsJsonFlatteningConverter`, `CostContextOverridePostProcessor`.
- When you add a new serializable model, you must also add a matching `[JsonSerializable(typeof(YourType))]` to the relevant context (or extend an existing one). Otherwise AOT builds will fail and trimming warnings will appear.
- Repositories take `IHttpClientFactory` + `IOptions<ModelsDevOptions>` via constructor injection — that is the supported HTTP path. Don't add `static HttpClient` fields.

## Compatibility with models.dev is the primary contract
- Every `Models/*.cs` type uses `[JsonPropertyName("...")]` to bind to models.dev's JSON field names. Names like `input_audio`, `output_audio`, `cache_read`, `cache_write`, `structured_output`, `tool_call`, `open_weights`, `last_updated`, `knowledge` must stay byte-for-byte identical to the upstream schema.
- When models.dev's schema changes, update the matching property name and (if needed) add the field. Do not rename C# properties without also updating the `JsonPropertyName`.
- The base address is configurable via `ModelsDevOptions.BaseAddress` (defaults to `https://models.dev`) and set through `AddModelsDotDevSharp(opts => opts.BaseAddress = ...)`. The endpoints are hardcoded per repository: `/api.json` (providers), `/models.json` (model metadata), `/catalog.json` (catalog).
- Public API surface:
  - Abstractions: `IModelInfoRepository`, `IModelMetadataRepository`, `ICatalogRepository` (in `Abstractions/`)
  - Implementations: `ModelInfoRepository`, `ModelMetadataRepository`, `CatalogRepository`
  - DI entry point: `ModelsDevServiceCollectionExtensions.AddModelsDotDevSharp(...)` (uses a C# 14 `extension` block)
  - Options: `ModelsDevOptions` (currently just `BaseAddress`)
  - New methods belong on the interface and the implementation together.

## Code layout conventions
- Namespaces: root types in `ModelsDotDevSharp`, abstractions in `ModelsDotDevSharp.Abstractions`, JSON contexts in `ModelsDotDevSharp.Contexts`, converters in `ModelsDotDevSharp.Converters`. `GlobalUsings.cs` imports all of these plus `System.ComponentModel`, `System.Text.Json.Serialization`, and `Microsoft.Extensions.Options`, so individual files usually skip those usings.
- Models are C# `record` types with mutable `get; set;` properties (not `init`). Keep that pattern for consistency with the existing files.
- Every source file starts with the MIT license header (see any `Models/*.cs`).
- The `.csproj.DotSettings` file is a JetBrains Rider/ReSharper setting (namespace-folder skip). Ignore it unless working in Rider.

## Known behaviors worth knowing
- `GetModelInfoByIdAsync` and `GetProviderInfoByIdAsync` catch `NullReferenceException` from LINQ `First`/`FirstAsync` and rethrow as `ArgumentException`. Don't "fix" the throw type without confirming it isn't part of the public contract.
- `GetCatalogAsync` throws a bare `Exception("Could not connect to the ModelDotDev API")` when the JSON deserializes to `null`. Caller code may depend on that message.
- `GetProviderInfosAsync` returns an empty array (not an exception) when the response body is empty or deserializes to `null`.
- `src/.idea/` is a JetBrains IDE folder; it is gitignored via `/src/.idea` in the root `.gitignore`. Do not commit changes to it.

## What is intentionally not here
- README is packed into the NuGet package via `<PackageReadmeFile>README.md</PackageReadmeFile>` (the file at repo root). Don't expand the README beyond what's needed unless asked.
- No analyzer, formatter, or lint config files (`Directory.Build.props`, `Directory.Build.targets`, `.editorconfig`) are committed. The repo relies on .NET SDK defaults plus the Rider `.DotSettings` file.

## Agent skills

### Issue tracker

Issues and PRDs for this repo live as GitHub issues at `alastairlundy/ModelsDotDevSharp`; use the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Triage labels use the canonical five strings (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`) — no overrides. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout: one `CONTEXT.md` and `docs/adr/` at the repo root (neither exists yet; skills will create them lazily). See `docs/agents/domain.md`.
