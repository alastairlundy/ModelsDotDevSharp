# Changelog

All notable changes to this project are documented in this file. The format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
