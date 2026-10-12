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

using System.Runtime.CompilerServices;

namespace ModelsDotDevSharp;

/// <summary>
/// HTTP-backed implementation of <see cref="IModelMetadataRepository"/>.
/// </summary>
public class ModelMetadataRepository : IModelMetadataRepository
{
    private readonly IModelsDevTransport _transport;

    /// <summary>
    /// Creates a new <see cref="ModelMetadataRepository"/>.
    /// </summary>
    /// <param name="transport">The transport used to fetch the model metadata collection.</param>
    internal ModelMetadataRepository(IModelsDevTransport transport)
    {
        _transport = transport;
    }

    /// <summary>
    /// Enumerates all available model metadata, one model at a time.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async enumerable of model metadata.</returns>
    public async IAsyncEnumerable<AIModelMetadata> EnumerateModelMetadataAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        AIModelMetadata[] models = await _transport.GetCollectionAsync(
            "/models.json", ModelMetadataJsonContext.Default.AIModelMetadataArray, cancellationToken);

        foreach (AIModelMetadata model in models)
        {
            yield return model;
        }
    }

    /// <summary>
    /// Gets a single model's metadata by its composite <paramref name="id"/> in the form <c>"{provider}/{model}"</c>.
    /// </summary>
    /// <param name="id">The composite model identifier, e.g. <c>"openai/gpt-5.6-sol"</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching model metadata.</returns>
    /// <exception cref="ArgumentException">No model metadata with the given <paramref name="id"/> exists.</exception>
    public async Task<AIModelMetadata> GetModelMetadataAsync(string id, CancellationToken cancellationToken = default)
    {
        AIModelMetadata? result = await EnumerateModelMetadataAsync(cancellationToken)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        if (result is null)
            throw new ArgumentException($"Model metadata with Id of {id} not found.");

        return result;
    }
}
