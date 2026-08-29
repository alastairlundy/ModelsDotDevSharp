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

using System.Net.Http.Json;
using System.Runtime.CompilerServices;

namespace ModelsDotDevSharp;

/// <summary>
/// HTTP-backed implementation of <see cref="IModelInfoRepository"/>.
/// </summary>
public class ModelInfoRepository : IModelInfoRepository
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ModelsDevOptions> _options;

    /// <summary>
    /// Creates a new <see cref="ModelInfoRepository"/>.
    /// </summary>
    /// <param name="httpClientFactory">The factory used to create HTTP clients.</param>
    /// <param name="options">The configured <see cref="ModelsDevOptions"/>.</param>
    public ModelInfoRepository(IHttpClientFactory httpClientFactory, IOptions<ModelsDevOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    /// <summary>
    /// Gets a single model's information by its provider and model identifiers.
    /// </summary>
    /// <param name="providerId">The provider identifier, e.g. <c>"openai"</c>.</param>
    /// <param name="modelId">The model identifier, e.g. <c>"gpt-5.6-sol"</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching model information.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providerId"/> or <paramref name="modelId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No model with the given identifiers exists.</exception>
    public async Task<AIModelInfo> GetModelInfoByIdAsync(string providerId, string modelId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        ArgumentNullException.ThrowIfNull(providerId);

        AIProviderInfo provider = await GetProviderInfoByIdAsync(providerId, cancellationToken);

        try
        {
            return provider.Models.First(m => m.Id == modelId);
        }
        catch (NullReferenceException)
        {
            throw new ArgumentException($"Model with with Id of {modelId} not found.");
        }
    }

    /// <summary>
    /// Gets a single provider's information by its identifier.
    /// </summary>
    /// <param name="providerId">The provider identifier, e.g. <c>"openai"</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching provider information.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providerId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No provider with the given identifier exists.</exception>
    public async Task<AIProviderInfo> GetProviderInfoByIdAsync(string providerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerId);

        try
        {
            return await EnumerateProviderInfosAsync(cancellationToken).FirstAsync(p => p.Id == providerId, cancellationToken);
        }
        catch (NullReferenceException)
        {
            throw new ArgumentException($"Provider with with Id of {providerId} not found.");
        }
    }

    /// <summary>
    /// Enumerates all available provider information, one provider at a time.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async enumerable of provider information.</returns>
    public async IAsyncEnumerable<AIProviderInfo> EnumerateProviderInfosAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        HttpClient client = HttpClientHelper.CreateClient(_httpClientFactory, _options.Value);

        using HttpResponseMessage response = await client.GetAsync("/api.json", cancellationToken);
        response.EnsureSuccessStatusCode();

        AIProviderInfo[]? providers = await response.Content.ReadFromJsonAsync(
            ModelInfoJsonContext.Default.AIProviderInfoArray, cancellationToken);

        if (providers is not null)
        {
            foreach (AIProviderInfo provider in providers)
            {
                if (provider is not null)
                    yield return provider;
            }
        }
    }

    /// <summary>
    /// Returns all provider infos. Returns an empty array if the response body is empty or deserializes to null.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An array of provider infos.</returns>
    public async Task<AIProviderInfo[]> GetProviderInfosAsync(CancellationToken cancellationToken = default) => 
        await EnumerateProviderInfosAsync(cancellationToken)
            .ToArrayAsync(cancellationToken);
}
