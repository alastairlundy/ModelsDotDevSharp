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
/// Provides an abstraction for retrieving AI model metadata from the /models.json endpoint.
/// </summary>
public interface IModelMetadataRepository
{
    /// <summary>
    /// Gets a single model's metadata by its composite <paramref name="id"/> in the form <c>"{provider}/{model}"</c>.
    /// </summary>
    /// <param name="id">The composite model identifier, e.g. <c>"openai/gpt-4o"</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching model metadata.</returns>
    Task<AIModelMetadata> GetModelMetadataAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates all available model metadata.
    /// The underlying wire format is a dictionary, so the data is materialized as an array before streaming.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async enumerable of model metadata.</returns>
    IAsyncEnumerable<AIModelMetadata> EnumerateModelMetadataAsync(CancellationToken cancellationToken = default);
}
