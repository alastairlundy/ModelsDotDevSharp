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
using System.Text.Json.Serialization.Metadata;

namespace ModelsDotDevSharp.Internal;

/// <summary>
/// Stateless HTTP implementation of <see cref="IModelsDevTransport"/> that creates an
/// <see cref="HttpClient"/> per call through <see cref="HttpClientHelper"/> and decodes collection
/// endpoints with source-generated type information.
/// </summary>
/// <remarks>
/// The type holds no per-call state — only the injected factory and options — so Singleton
/// registration stays safe and option reads stay fresh behind the seam.
/// </remarks>
internal sealed class ModelsDevTransport : IModelsDevTransport
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ModelsDevOptions> _options;

    /// <summary>
    /// Creates a new <see cref="ModelsDevTransport"/>.
    /// </summary>
    /// <param name="httpClientFactory">The factory used to create HTTP clients.</param>
    /// <param name="options">The configured <see cref="ModelsDevOptions"/>.</param>
    public ModelsDevTransport(IHttpClientFactory httpClientFactory, IOptions<ModelsDevOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<TElement[]> GetCollectionAsync<TElement>(
        string relativePath,
        JsonTypeInfo<TElement[]> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(typeInfo);

        HttpClient client = HttpClientHelper.CreateClient(_httpClientFactory, _options.Value);

        using HttpResponseMessage response = await client.GetAsync(relativePath, cancellationToken);
        response.EnsureSuccessStatusCode();

        TElement[]? elements = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken);

        return elements ?? Array.Empty<TElement>();
    }
}
