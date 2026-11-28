# ModelsDotDevSharp

[![NuGet Version](https://img.shields.io/nuget/v/ModelsDotDevSharp.svg)](https://www.nuget.org/packages/ModelsDotDevSharp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

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

### Model metadata

Resolve `IModelMetadataRepository` and fetch one model by its composite id:

```csharp
var metadataRepository = provider.GetRequiredService<IModelMetadataRepository>();

// Composite id has the form "provider/model"
AIModelMetadata metadata = await metadataRepository.GetModelMetadataAsync("openai/gpt-5.6-sol");
```

### Catalog

Resolve `ICatalogRepository` and fetch the full catalog:

```csharp
var catalogRepository = provider.GetRequiredService<ICatalogRepository>();

AICatalog catalog = await catalogRepository.GetCatalogAsync();
```

### Configuration

Override the API base address when you register the services:

```csharp
services.AddModelsDotDevSharp(opts =>
{
    opts.BaseAddress = "https://models.dev";
});
