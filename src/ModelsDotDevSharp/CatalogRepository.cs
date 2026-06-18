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

public class CatalogRepository : ICatalogRepository
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ModelsDevOptions> _options;

    public CatalogRepository(IHttpClientFactory httpClientFactory, IOptions<ModelsDevOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<AICatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_options.Value.BaseAddress);

        HttpResponseMessage response = await client.GetAsync("/catalog.json", cancellationToken);

        AICatalog? catalog = await response.Content.ReadFromJsonAsync(
            CatalogJsonContext.Default.AICatalog, cancellationToken);

        if (catalog is null)
            throw new Exception("Could not connect to the ModelDotDev API");

        foreach (AIProviderInfo provider in catalog.Providers.Values)
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
        CostContextOverridePostProcessor.Process(cost, CatalogJsonContext.Default.Options);

        foreach (AIModelCostInfo contextOverride in cost.ContextOverrides.Values)
        {
            ProcessAllCosts(contextOverride);
        }
    }
}
