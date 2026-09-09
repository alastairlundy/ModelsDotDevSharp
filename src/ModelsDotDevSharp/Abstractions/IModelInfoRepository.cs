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

namespace ModelsDotDevSharp.Abstractions;

/// <summary>
/// Provides an abstraction for retrieving information about AI models and their providers.
/// </summary>
public interface IModelInfoRepository
{
    /// <summary>
    /// Gets a single model's information by its provider and model identifiers.
    /// </summary>
    /// <param name="providerId">The provider identifier, e.g. <c>"openai"</c>.</param>
    /// <param name="modelId">The model identifier, e.g. <c>"gpt-5.6-sol"</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching model information.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providerId"/> or <paramref name="modelId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No model with the given identifiers exists.</exception>
    Task<AIModelInfo> GetModelInfoByIdAsync(string providerId, string modelId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a single provider's information by its identifier.
    /// </summary>
    /// <param name="providerId">The provider identifier, e.g. <c>"openai"</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching provider information.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providerId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No provider with the given identifier exists.</exception>
    Task<AIProviderInfo> GetProviderInfoByIdAsync(string providerId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Returns all provider infos. Returns an empty array if the response body is empty or deserializes to null.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An array of provider infos.</returns>
    Task<AIProviderInfo[]> GetProviderInfosAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates all available provider information, one provider at a time.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async enumerable of provider information.</returns>
    IAsyncEnumerable<AIProviderInfo> EnumerateProviderInfosAsync(CancellationToken cancellationToken = default);
}