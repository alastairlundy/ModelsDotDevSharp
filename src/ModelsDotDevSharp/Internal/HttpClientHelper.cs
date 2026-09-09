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

namespace ModelsDotDevSharp.Internal;

/// <summary>
/// Builds an <see cref="HttpClient"/> from the configured <see cref="ModelsDevOptions"/>, validating the
/// base address so it can only target absolute <c>http</c>/<c>https</c> endpoints.
/// </summary>
internal static class HttpClientHelper
{
    /// <summary>
    /// Creates an <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is the configured
    /// <see cref="ModelsDevOptions.BaseAddress"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="options"/> has no <see cref="ModelsDevOptions.BaseAddress"/>, or when it is
    /// not an absolute <c>http</c>/<c>https</c> URI. This guards against SSRF via non-network schemes such as
    /// <c>file://</c> or UNC paths, and against internal/cloud-metadata endpoints.
    /// </exception>
    internal static HttpClient CreateClient(IHttpClientFactory httpClientFactory, ModelsDevOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.BaseAddress))
        {
            throw new InvalidOperationException(
                "ModelsDevOptions.BaseAddress must be configured to an absolute http or https URI.");
        }

        if (!Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out Uri? baseAddress)
            || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"ModelsDevOptions.BaseAddress '{options.BaseAddress}' must be an absolute http or https URI.");
        }

        HttpClient client = httpClientFactory.CreateClient();
        client.BaseAddress = baseAddress;
        return client;
    }
}