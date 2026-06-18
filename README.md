# ModelsDotDevSharp
Easily access [models.dev](https://models.dev)'s AI model, pricing, and provider information from .NET.

## Requirements

- .NET 10

## Installation

```bash
dotnet add package ModelsDotDevSharp
```

Or with a `PackageReference`:

```xml
<ItemGroup>
  <PackageReference Include="ModelsDotDevSharp" Version="X.Y.Z" />
</ItemGroup>
```

Use the latest version available on NuGet.

## Quick start

`ModelInfoProvider` depends on `IHttpClientFactory`. Register it with `AddHttpClient`, which also wires up the factory:

```csharp
using ModelsDotDevSharp;
using ModelsDotDevSharp.Abstractions;

builder.Services.AddHttpClient<IModelInfoProvider, ModelInfoProvider>();
```

Resolve and use it:

```csharp
IModelInfoProvider provider = serviceProvider.GetRequiredService<IModelInfoProvider>();

AIProviderInfo[] providers = await provider.GetProviderInfosAsync();
AIProviderInfo oneProvider = await provider.GetProviderInfoByIdAsync("provider-id");
AIModelInfo oneModel = await provider.GetModelInfoByIdAsync("provider-id", "model-id");

await foreach (AIProviderInfo p in provider.EnumerateProviderInfosAsync())
{
    // stream as it arrives
}
```

All four methods accept an optional `CancellationToken` as their last parameter.

## Public model types

In the `ModelsDotDevSharp` namespace, as `record` types bound to models.dev's JSON via `[JsonPropertyName]`:

- `AIProviderInfo` — a provider: id, name, npm package id, documentation/API URLs, environment variables, and the models it exposes.
- `AIModelInfo` — a single model: id, name, family, capabilities, release/knowledge/last-updated dates, modalities, and cost.
- `AIModelCostInfo` — per-million-token pricing for input, output, audio in/out, cache reads, cache writes, and tiered pricing.
- `AIModelCostTier` — a single tiered-price entry.
- `AIModelTierInfo` — metadata for a cost tier.
- `AIModelLimit` — context, input, and output token limits.
- `AIModelModalities` — input and output modality strings.

The public abstraction is `IModelInfoProvider` (`ModelsDotDevSharp.Abstractions`); the implementation is `ModelInfoProvider`.

## AOT and trimming

The library is AOT- and trim-safe. The project sets `IsTrimmable`, `PublishTrimmed`, and `EnableAoTAnalyzer`, and JSON deserialization runs through `System.Text.Json` source generators rather than reflection. It can be consumed from NativeAOT applications or trimmed hosts with no extra configuration.

## Upstream notes

The models.dev base address (`https://models.dev`) and the `/api.json` path are hardcoded constants in `ModelInfoProvider`; the base address is not currently configurable.

JSON field names are kept byte-for-byte identical to the models.dev schema (e.g. `input_audio`, `output_audio`, `cache_read`, `cache_write`, `structured_output`, `tool_call`, `open_weights`, `last_updated`, `knowledge`). When models.dev's schema changes, those bindings are updated here to match.

## License

MIT — see [LICENSE](LICENSE).
