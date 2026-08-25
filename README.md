# ModelsDotDevSharp

A .NET wrapper around the public [models.dev](https://models.dev) API, providing strongly-typed access to AI provider and model metadata.

## Install

```sh
dotnet add package ModelsDotDevSharp
```

## Usage

Register the services, build an `IServiceProvider`, resolve `IModelInfoRepository`, and fetch data:

```csharp
using Microsoft.Extensions.DependencyInjection;
using ModelsDotDevSharp;
using ModelsDotDevSharp.Abstractions;

var services = new ServiceCollection();
services.AddModelsDotDevSharp();

using var provider = services.BuildServiceProvider();
var repository = provider.GetRequiredService<IModelInfoRepository>();

// All providers (Ids populated from the API's root map)
AIProviderInfo[] providers = await repository.GetProviderInfosAsync();

// A single model by provider and model id
AIModelInfo model = await repository.GetModelInfoByIdAsync("anthropic", "claude-3-5-sonnet");
```
