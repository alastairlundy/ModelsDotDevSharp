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
using Microsoft.Extensions.Options;

namespace ModelsDotDevSharp;

/// <summary>
/// 
/// </summary>
public class ModelInfoRepository : IModelInfoRepository
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ModelsDevOptions> _options;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="httpClientFactory"></param>
    /// <param name="options"></param>
    public ModelInfoRepository(IHttpClientFactory httpClientFactory, IOptions<ModelsDevOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="providerId"></param>
    /// <param name="modelId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
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
    /// 
    /// </summary>
    /// <param name="providerId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
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
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<AIProviderInfo> EnumerateProviderInfosAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_options.Value.BaseAddress);

        HttpResponseMessage response = await client.GetAsync("/api.json", cancellationToken);

        IAsyncEnumerable<AIProviderInfo?> providers = response.Content.ReadFromJsonAsAsyncEnumerable(
            ModelInfoJsonContext.Default.AIProviderInfo, cancellationToken);

        await foreach (AIProviderInfo? provider in providers)
        {
            if (provider is not null)
                yield return provider;
        }
    }

    /// <summary>
    /// Returns all provider infos. Returns an empty array if the response body is empty or deserializes to null.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<AIProviderInfo[]> GetProviderInfosAsync(CancellationToken cancellationToken = default)
    {
        return await EnumerateProviderInfosAsync(cancellationToken).ToArrayAsync(cancellationToken);
    }
}
