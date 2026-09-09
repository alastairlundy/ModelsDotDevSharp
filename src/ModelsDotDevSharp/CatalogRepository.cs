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

namespace ModelsDotDevSharp;

/// <summary>
/// HTTP-backed implementation of <see cref="ICatalogRepository"/>.
/// </summary>
public class CatalogRepository : ICatalogRepository
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ModelsDevOptions> _options;

    /// <summary>
    /// Creates a new <see cref="CatalogRepository"/>.
    /// </summary>
    /// <param name="httpClientFactory">The factory used to create HTTP clients.</param>
    /// <param name="options">The configured <see cref="ModelsDevOptions"/>.</param>
    public CatalogRepository(IHttpClientFactory httpClientFactory, IOptions<ModelsDevOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    /// <summary>
    /// Gets the full catalog of models and providers, with cost context overrides resolved.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The AI catalog.</returns>
    /// <exception cref="Exception">The catalog could not be retrieved or deserialized.</exception>
    public async Task<AICatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        HttpClient client = HttpClientHelper.CreateClient(_httpClientFactory, _options.Value);

        using HttpResponseMessage response = await client.GetAsync("/catalog.json", cancellationToken);
        response.EnsureSuccessStatusCode();

        AICatalog? catalog = await response.Content.ReadFromJsonAsync(
            CatalogJsonContext.Default.AICatalog, cancellationToken);

        if (catalog is null)
            throw new Exception("Could not connect to the ModelDotDev API");

        foreach (AIProviderInfo provider in catalog.Providers?.Values ?? Enumerable.Empty<AIProviderInfo>())
        {
            foreach (AIModelInfo model in provider.Models)
            {
                if (model.Cost is not null)
                    ProcessAllCosts(model.Cost);

                if (model.Modes is not null)
                {
                    foreach (AIModelCostInfo modeCost in model.Modes.Values)
                    {
                        ProcessAllCosts(modeCost);
                    }
                }

                if (model.Experimental?.Modes is not null)
                {
                    foreach (AIModelCostInfo experimentalModeCost in model.Experimental.Modes.Values)
                    {
                        ProcessAllCosts(experimentalModeCost);
                    }
                }
            }
        }

        return catalog;
    }

    private static void ProcessAllCosts(AIModelCostInfo cost)
    {
        // Iterative pre-order DFS to avoid unbounded recursion / StackOverflowException
        // on deeply nested ContextOverrides trees.
        Stack<AIModelCostInfo> stack = new Stack<AIModelCostInfo>();
        stack.Push(cost);

        while (stack.Count > 0)
        {
            AIModelCostInfo current = stack.Pop();
            CostContextOverridePostProcessor.Process(current, CatalogJsonContext.Default.Options);

            foreach (AIModelCostInfo contextOverride in current.ContextOverrides.Values)
            {
                stack.Push(contextOverride);
            }
        }
    }
}