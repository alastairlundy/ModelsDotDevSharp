# AGENTS.md

## Project
- C# library that wraps the public models.dev API (`https://models.dev/api.json`) for .NET consumers.
- Single project: `src/ModelsDotDevDotnet/ModelsDotDevDotnet.csproj`; solution is `src/ModelsDotDevDotnet.slnx` (XML solution format).
- `TargetFramework=net10.0`, `LangVersion=14`. No multi-targeting. No `global.json` — SDK version is whatever is installed locally.

## Build, package, restore
- Build: `dotnet build src/ModelsDotDevDotnet.slnx` (or `dotnet build src/ModelsDotDevDotnet/ModelsDotDevDotnet.csproj`).
- Pack: `dotnet pack src/ModelsDotDevDotnet/ModelsDotDevDotnet.csproj` — `GeneratePackageOnBuild=true` is set, so a `.nupkg` is produced.
- No tests: there is no test project. `dotnet test` is a no-op. Don't invent one without asking.
- No CI workflows exist in `.github/workflows/`. Dependabot (`.github/dependabot.yml`) updates NuGet weekly and looks for GitHub Actions (none yet).

## Dependency management — Central Package Management
- `src/Directory.Packages.props` has `ManagePackageVersionsCentrally=true`.
- All NuGet versions live in `Directory.Packages.props`. The `.csproj` only declares `<PackageReference Include="..." />` with no `Version=`.
- To add/update a package, edit `src/Directory.Packages.props` only. Do not add `Version=` attributes to the `.csproj`.
- Current packages: `Microsoft.Extensions.Http` 10.0.9, `System.Net.Http.Json` 10.0.9 (both at the .NET 10.0.9 patch track).

## AOT / trimming constraints
- The project sets `IsTrimmable=true`, `PublishTrimmed=true`, `EnableAoTAnalyzer=true`. Treat the library as AOT- and trim-safe.
- JSON deserialization goes through `System.Text.Json` **source generators** only — no reflection. The two contexts live at:
  - `src/ModelsDotDevDotnet/Contexts/AIProviderJsonContext.cs` (`AIProviderInfo`)
  - `src/ModelsDotDevDotnet/Contexts/AIProviderArrayJsonContext.cs` (`AIProviderInfo[]`)
- When you add a new serializable model, you must also add a matching `[JsonSerializable(typeof(YourType))]` partial context (or extend an existing one). Otherwise AOT builds will fail and trimming warnings will appear.
- `ModelInfoProvider` takes `IHttpClientFactory` via constructor injection — that is the supported HTTP path. Don't add `static HttpClient` fields.

## Compatibility with models.dev is the primary contract
- Every `Models/*.cs` type uses `[JsonPropertyName("...")]` to bind to models.dev's JSON field names. Names like `input_audio`, `output_audio`, `cache_read`, `cache_write`, `structured_output`, `tool_call`, `open_weights`, `last_updated`, `knowledge` must stay byte-for-byte identical to the upstream schema.
- When models.dev's schema changes, update the matching property name and (if needed) add the field. Do not rename C# properties without also updating the `JsonPropertyName`.
- `ModelsDotDevBaseAddress` (`https://models.dev`) and the `/api.json` path are hardcoded constants in `ModelInfoProvider.cs:37,102,125`. The base path is not currently configurable.
- Public API surface: `ModelInfoProvider` (concrete) and `IModelInfoProvider` (abstraction in `Abstractions/`). New methods belong on the interface and the implementation together.

## Code layout conventions
- Namespaces: root types in `ModelsDotDevDotnet`, abstractions in `ModelsDotDevDotnet.Abstractions`, JSON contexts in `ModelsDotDevDotnet.Contexts`. `GlobalUsings.cs` imports all three plus `System.Text.Json.Serialization`, so individual files usually skip those usings.
- Models are C# `record` types with mutable `get; set;` properties (not `init`). Keep that pattern for consistency with the existing files.
- Every source file starts with the MIT license header (see any `Models/*.cs`).
- The `.csproj.DotSettings` file is a JetBrains Rider/ReSharper setting (namespace-folder skip). Ignore it unless working in Rider.

## Known behaviors worth knowing
- `GetProviderInfoByIdAsync` and `GetModelInfoByIdAsync` catch `NullReferenceException` from LINQ `FirstAsync` / collection access and rethrow as `ArgumentException`. Don't "fix" the throw type without confirming it isn't part of the public contract.
- `GetProviderInfosAsync` throws a bare `Exception("Could not connect to the ModelDotDev API")` when the JSON deserializes to `null`. Caller code may depend on that message.
- `src/.idea/` is a JetBrains IDE folder; it is gitignored via `/src/.idea` in the root `.gitignore`. Do not commit changes to it.

## What is intentionally not here
- No README beyond the one-liner. Don't expand the README unless asked.
- No analyzer, formatter, or lint config files (`Directory.Build.props`, `Directory.Build.targets`, `.editorconfig`) are committed. The repo relies on .NET SDK defaults plus the Rider `.DotSettings` file.
