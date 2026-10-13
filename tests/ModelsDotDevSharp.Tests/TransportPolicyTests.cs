/*
    MIT License

    Copyright (c) 2026 Alastair Lundy

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
 */

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using ModelsDotDevSharp.Abstractions;
using ModelsDotDevSharp.Extensions;
using ModelsDotDevSharp.Internal;

namespace ModelsDotDevSharp.Tests;

/// <summary>
/// Proves the internal <see cref="IModelsDevTransport"/> seam is substitutable without live HTTP by
/// registering canned Dependency Injection doubles, then covering the unified empty-versus-throw policy
/// and the preserved per-endpoint paths.
/// </summary>
public class TransportPolicyTests
{
    private const string NullDecodeBody = "null";
    private const string EmptyCollectionBody = "{}";
    private const string PopulatedProvidersBody = """{"openai":{"name":"OpenAI"}}""";
    private const string UnifiedNullBodyMessage =
        "The models.dev API response body was empty or deserialized to null.";

    [Test]
    public async Task CollectionRepositories_YieldEmpty_WhenCannedTransportDecodesNull()
    {
        CannedTransport transport = new(NullDecodeBody);
        using ServiceProvider provider = BuildProvider(services => services.AddSingleton<IModelsDevTransport>(transport));

        IModelInfoRepository modelInfo = provider.GetRequiredService<IModelInfoRepository>();
        IModelMetadataRepository modelMetadata = provider.GetRequiredService<IModelMetadataRepository>();

        AIProviderInfo[] providers = await modelInfo.GetProviderInfosAsync();
        AIModelMetadata[] models = await modelMetadata.EnumerateModelMetadataAsync().ToArrayAsync();

        await Assert.That(providers).IsEmpty();
        await Assert.That(models).IsEmpty();

        // Both collection endpoints kept their own paths behind one substituted seam.
        await Assert.That(transport.RequestedPaths.Length).IsEqualTo(2);
        await Assert.That(transport.RequestedPaths[0]).IsEqualTo("/api.json");
        await Assert.That(transport.RequestedPaths[1]).IsEqualTo("/models.json");
    }

    [Test]
    public async Task CollectionRepositories_YieldEmpty_WhenCannedTransportReturnsEmptyArray()
    {
        CannedTransport transport = new(EmptyCollectionBody);
        using ServiceProvider provider = BuildProvider(services => services.AddSingleton<IModelsDevTransport>(transport));

        IModelInfoRepository modelInfo = provider.GetRequiredService<IModelInfoRepository>();
        IModelMetadataRepository modelMetadata = provider.GetRequiredService<IModelMetadataRepository>();

        AIProviderInfo[] providers = await modelInfo.GetProviderInfosAsync();
        IAsyncEnumerable<AIModelMetadata> models = modelMetadata.EnumerateModelMetadataAsync();

        await Assert.That(providers).IsEmpty();
        await Assert.That(models).IsEmpty();
    }

    [Test]
    public async Task ModelInfoRepository_SurfacesCannedProviders_AndRequestsApiJson()
    {
        CannedTransport transport = new(PopulatedProvidersBody);
        using ServiceProvider provider = BuildProvider(services => services.AddSingleton<IModelsDevTransport>(transport));

        IModelInfoRepository modelInfo = provider.GetRequiredService<IModelInfoRepository>();

        AIProviderInfo[] providers = await modelInfo.GetProviderInfosAsync();

        await Assert.That(providers.Length).IsEqualTo(1);
        await Assert.That(providers[0].Id).IsEqualTo("openai");

        await Assert.That(transport.RequestedPaths.Length).IsEqualTo(1);
        await Assert.That(transport.RequestedPaths[0]).IsEqualTo("/api.json");
    }

    [Test]
    public async Task Transport_MapsNullDecodeBody_ToEmptyArray()
    {
        CannedHttpMessageHandler handler = new(NullDecodeBody);
        using ServiceProvider provider = BuildProvider(
            services => services.AddSingleton<IHttpClientFactory>(new CannedHttpClientFactory(handler)));

        // The real transport stays wired here; only its HTTP dependency is canned.
        IModelInfoRepository modelInfo = provider.GetRequiredService<IModelInfoRepository>();

        AIProviderInfo[] providers = await modelInfo.GetProviderInfosAsync();

        await Assert.That(providers).IsEmpty();
        await Assert.That(handler.RequestedPaths.Length).IsEqualTo(1);
        await Assert.That(handler.RequestedPaths[0]).IsEqualTo("/api.json");
    }

    [Test]
    public async Task CatalogRepository_ThrowsInvalidDataException_OnNullBody_AndRequestsCatalogJson()
    {
        CannedHttpMessageHandler handler = new(NullDecodeBody);
        using ServiceProvider provider = BuildProvider(
            services => services.AddSingleton<IHttpClientFactory>(new CannedHttpClientFactory(handler)));

        ICatalogRepository catalog = provider.GetRequiredService<ICatalogRepository>();

        await Assert.ThrowsAsync<InvalidDataException>(() => catalog.GetCatalogAsync())
            .WithMessage(UnifiedNullBodyMessage);

        await Assert.That(handler.RequestedPaths.Length).IsEqualTo(1);
        await Assert.That(handler.RequestedPaths[0]).IsEqualTo("/catalog.json");
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> substitute)
    {
        ServiceCollection services = new();
        services.AddModelsDotDevSharp();

        // Registered after AddModelsDotDevSharp so the substitution wins resolution.
        substitute(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Canned <see cref="IModelsDevTransport"/> that decodes a fixed JSON body with the source-generated
    /// type info supplied by the repository, exactly as the real transport does minus HTTP, and that
    /// records every requested path.
    /// </summary>
    private sealed class CannedTransport(string body) : IModelsDevTransport
    {
        private readonly List<string> _requestedPaths = [];

        public string[] RequestedPaths => [.. _requestedPaths];

        public Task<TElement[]> GetCollectionAsync<TElement>(
            string relativePath,
            JsonTypeInfo<TElement[]> typeInfo,
            CancellationToken cancellationToken = default)
        {
            _requestedPaths.Add(relativePath);

            TElement[]? decoded = JsonSerializer.Deserialize(body, typeInfo);
            return Task.FromResult(decoded ?? []);
        }
    }

    /// <summary>
    /// Canned <see cref="IHttpClientFactory"/> handing out clients backed by a fixed-response handler so
    /// no test touches the network.
    /// </summary>
    private sealed class CannedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>
    /// Records the absolute path of every request and answers with a canned JSON body.
    /// </summary>
    private sealed class CannedHttpMessageHandler(string body) : HttpMessageHandler
    {
        private readonly List<string> _requestedPaths = [];

        public string[] RequestedPaths => [.. _requestedPaths];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requestedPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }
}
