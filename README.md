# ModelsDotDevSharp
Easily access [models.dev](https://models.dev)'s AI model, pricing, and provider information from .NET.

## Requirements

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


## AOT and trimming

The library is AOT- and trim-safe. The project sets `IsTrimmable`, `PublishTrimmed`, and `EnableAoTAnalyzer`, and JSON deserialization runs through `System.Text.Json` source generators rather than reflection. It can be consumed from NativeAOT applications or trimmed hosts with no extra configuration.

## Upstream notes

The models.dev base address (`https://models.dev`) and the `/api.json` path are hardcoded constants in `ModelInfoProvider`; the base address is not currently configurable.

JSON field names are kept byte-for-byte identical to the models.dev schema (e.g. `input_audio`, `output_audio`, `cache_read`, `cache_write`, `structured_output`, `tool_call`, `open_weights`, `last_updated`, `knowledge`). When models.dev's schema changes, those bindings are updated here to match.

## License

MIT — see [LICENSE](LICENSE).
