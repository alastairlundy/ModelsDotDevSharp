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

using System.Text.Json.Serialization.Metadata;

namespace ModelsDotDevSharp.Internal;

/// <summary>
/// Collection-only fetch seam shared by the repositories behind Dependency Injection, keeping one null and
/// error policy for every collection endpoint.
/// </summary>
/// <remarks>
/// This interface deliberately exposes no document-fetch member; document fetches stay with their owning
/// repository.
/// </remarks>
internal interface IModelsDevTransport
{
    /// <summary>
    /// GETs <paramref name="relativePath"/> against the configured base address and decodes the response
    /// body as a JSON array using <paramref name="typeInfo"/> for Ahead-of-Time safe deserialization.
    /// </summary>
    /// <typeparam name="TElement">
    /// The element type of the collection to decode.
    /// </typeparam>
    /// <param name="relativePath">The endpoint path relative to the configured base address.</param>
    /// <param name="typeInfo">
    /// Source-generated type information for the element array, threaded per call site so decoding requires
    /// no reflection.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// The decoded array. An empty body or a body that decodes to <see langword="null"/> yields an empty
    /// array; this method never returns <see langword="null"/>.
    /// </returns>
    Task<TElement[]> GetCollectionAsync<TElement>(
        string relativePath,
        JsonTypeInfo<TElement[]> typeInfo,
        CancellationToken cancellationToken = default);
}
