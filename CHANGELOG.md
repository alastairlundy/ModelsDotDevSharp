# Changelog

All notable changes to this project are documented in this file. The format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.2.0] - 2026-09-15

### Global (defaulted)

#### Changed

- Converted `CatalogRepository.ProcessAllCosts` from recursion to an iterative stack-based pre-order DFS, eliminating `StackOverflowException` risk on deeply nested `ContextOverrides` cost trees with no public API changes.
- Moved `ModelsDevServiceCollectionExtensions` to the `ModelsDotDevSharp.Extensions` namespace and expanded NuGet packaging metadata (symbols/snupkg, embedded sources, package tags, release notes, corrected repository URL).
- Corrected package versioning metadata (`Version` to `PackageVersion`) and README packaging configuration.
- Hardened HTTP repositories against SSRF, silent failures, and null responses: validated `ModelsDevOptions.BaseAddress` as an absolute http/https URI, added `EnsureSuccessStatusCode` checks and response disposal, null-guarded catalog enumeration, registered `IHttpClientFactory` via `AddHttpClient()`, and introduced internal `HttpClientHelper` (now in `ModelsDotDevSharp.Internal`).
- Cleaned up internals and code style: made `CostContextOverridePostProcessor` internal, modernized JSON converters with collection expressions and explicit types, and removed a redundant null guard.
- **Runtime Dependencies:** bumped `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Http`, and `Microsoft.Extensions.Options` from 10.0.9 to 10.0.11, then to 10.0.12.
- **Testing Dependencies:** bumped `TUnit` from 1.65.51 to 1.65.68, then to 1.67.0.
- **CI Dependencies:** bumped `actions/checkout` from 4 to 7 and `actions/setup-dotnet` from 4 to 6 in `test-build.yml`.

#### Fixed

- Fixed NuGet README packaging so `README.md` is packed as `Content` with the correct `PackagePath`.
- Added least-privilege `permissions: contents: read` to the `test-build` workflow.

## 0.1.0

Initial release of `ModelsDotDevSharp`, a C# client that wraps the public
[models.dev](https://models.dev) API (`/api.json`) for .NET consumers.

The library exposes three repository abstractions over the models.dev endpoints,
each available as an interface plus a concrete implementation and registered for
dependency injection via `AddModelsDotDev()`:

- **`IModelInfoRepository`** — retrieves AI model and provider information from the
  `/models.json` endpoint: look up a single model by provider/model id
  (`GetModelInfoByIdAsync`), look up a provider (`GetProviderInfoByIdAsync`),
  fetch all providers (`GetProviderInfosAsync`), and stream providers
  (`EnumerateProviderInfosAsync`).
- **`IModelMetadataRepository`** — retrieves detailed model metadata from the
  `/models.json` endpoint: fetch a single model's metadata by its
  `"{provider}/{model}"` composite id (`GetModelMetadataAsync`) or stream all
  model metadata (`EnumerateModelMetadataAsync`).
- **`ICatalogRepository`** — retrieves the combined AI catalog from the
  `/catalog.json` endpoint (`GetCatalogAsync`).

Supporting features:

- Strongly-typed models (`AIProviderInfo`, `AIModelInfo`, `AIModelMetadata`,
  `AICatalog`, and related types) bound to the models.dev JSON schema.
- `System.Text.Json` source-generated (trimming- and AOT-safe) deserialization.
- HTTP access via `IHttpClientFactory` constructor injection.
- Trimming- and Native AOT-compatible packaging.

[0.2.0]: https://github.com/alastairlundy/ModelsDotDevSharp/compare/0.1.0...0.2.0
