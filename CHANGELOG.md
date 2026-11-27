# Changelog

All notable changes to this project are documented in this file. The format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.3.0] - Unreleased

### Added

- Internal collection transport seam `IModelsDevTransport` / `ModelsDevTransport` (registered as a Singleton) that centralizes HTTP client creation, GET, status-code validation, source-generated JSON decoding, and null-to-empty mapping for the `/api.json` and `/models.json` collection endpoints.
- `InternalsVisibleTo("ModelsDotDevSharp.Tests")` so the transport seam can be substituted in tests.
- NuGet publish workflow (`.github/workflows/publish.yml`) using OIDC trusted publishing.
- Documented the unified empty-versus-throw policy across all repository abstractions.
- AOT-safe `JsonStringEnumConverter<TEnum>` registrations on `ModelInfoJsonContext` and `CatalogJsonContext`, so `AIModelStatus`, `AIModelReasoningOptionType`, `AIModelProviderShape`, and `AIModelInterleavedField` deserialize from the strings the API sends.
- `AIModelInfo.Limit`, `AIModelInfo.CanonicalModelId`, and `AIModelInfo.Type` (the `limit`, `canonical_model_id` and `type` fields carried by `/api.json`).
- `AIModelMetadata.Description`, `AIModelMetadata.License`, `AIModelMetadata.Links`, and `AIModelMetadata.Type`.
- `AIModelLinkInfo` representing a `/models.json` `links` entry (`label`, `url`, `type`).
- `AIModelCostInfo.ReasoningCostPerMillionTokens`, plus `CacheReadPerMillionTokens`, `CacheWritePerMillionTokens`, and `ReasoningCostPerMillionTokens` on `AIModelCostTier`.
- `AIModelWeightInfo.Format` and `AIModelWeightInfo.Quantization`.
- `AIModelExperimentalMode` and `AIModelModeProvider` modelling the `{cost, provider}` shape of `experimental.modes.{mode}`.
- Internal `CostProcessing` helper shared by `ModelInfoRepository` and `CatalogRepository`.
- `PayloadContractTests` pinning the wire shapes of all three endpoints.

### Changed

- `ModelInfoRepository` and `ModelMetadataRepository` now consume `IModelsDevTransport` instead of `IHttpClientFactory` + `IOptions<ModelsDevOptions>`; their constructors are now `internal` (repositories are intended to be resolved via dependency injection).
- `GetModelInfoByIdAsync`, `GetProviderInfoByIdAsync`, and `GetModelMetadataAsync` now scan fetched arrays directly instead of using LINQ `First`/`FirstAsync`, removing `NullReferenceException`-based control flow.
- `GetCatalogAsync` now throws `InvalidDataException` with the message "The models.dev API response body was empty or deserialized to null." instead of a bare `Exception`.
- `CostContextOverridePostProcessor` uses ordinal string comparison when matching context override keys.
- Unified collection endpoints (`GetProviderInfosAsync`, `EnumerateProviderInfosAsync`, `EnumerateModelMetadataAsync`) yield empty results on absent/null bodies through the shared transport.
- **Breaking:** `AIModelExperimental.Modes` is now `Dictionary<string, AIModelExperimentalMode>` instead of `Dictionary<string, AIModelCostInfo>`. It previously deserialized the nested `{cost, provider}` object as a cost, yielding all-zero prices.
- **Breaking:** `AIModelInfo.Modes`, `AIModelInfo.Headers`, `AIModelInfo.Body`, `AIProviderInfo.Description`, `AIModelMetadata.StructuredOutput`, and `AIModelProviderOverride.Body`/`Headers` were removed. None of these fields appear in the live payload, so they read as empty-string or `false` defaults that look like real data.
- `GetProviderInfosAsync` (and therefore `GetProviderInfoByIdAsync` and `EnumerateProviderInfosAsync`) now resolves `context_over_*` cost overrides, matching `GetCatalogAsync`.
- **Runtime Dependencies:** bumped `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Http`, and `Microsoft.Extensions.Options` from 10.0.12 to 11.0.0.
- **Testing Dependencies:** bumped `TUnit` from 1.67.0 to 1.83.0.
- Dependabot configuration: weekly Monday schedule, 3-day cooldown, grouped minor/patch updates, and multiple manifest directories.

### Fixed

- Centralized null/empty response handling for collection endpoints so the empty-versus-throw policy is applied consistently.
- `/api.json` and `/catalog.json` no longer throw `JsonException` when deserializing the live payload. Both contexts were missing an enum converter, so any model carrying `status`, `reasoning_options`, `interleaved`, or a `provider.shape` failed the entire read.
- `context_over_*` cost overrides on the `/api.json` path stayed as raw extension data, so `ContextOverrides` came back empty for 794 costs in a fetched payload. The path now resolves them into typed override costs.

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

[0.3.0]: https://github.com/alastairlundy/ModelsDotDevSharp/compare/0.2.0...HEAD
[Unreleased]: https://github.com/alastairlundy/ModelsDotDevSharp/compare/0.2.0...HEAD
[0.2.0]: https://github.com/alastairlundy/ModelsDotDevSharp/compare/0.1.0...0.2.0
