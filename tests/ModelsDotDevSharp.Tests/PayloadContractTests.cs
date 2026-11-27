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

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelsDotDevSharp.Abstractions;
using ModelsDotDevSharp.Contexts;
using ModelsDotDevSharp.Extensions;
using ModelsDotDevSharp.Internal;

namespace ModelsDotDevSharp.Tests;

/// <summary>
/// Contract tests pinned to the real models.dev wire shapes. Every regression here originated from
/// the library drifting away from the live schema without a test noticing, so each fixture mirrors
/// an actual <c>/api.json</c>, <c>/models.json</c> or <c>/catalog.json</c> fragment.
/// </summary>
public class PayloadContractTests
{
    /// <summary>Exercises every enum-valued field the live payload carries as a JSON string.</summary>
    [Test]
    public async Task ApiJson_DeserializesStringEnums_FromLiveShape()
    {
        const string body = """
            {"openai":{"name":"OpenAI","models":{
              "gpt-1":{"name":"GPT One","status":"beta",
                       "reasoning_options":[{"type":"effort","values":["low","medium","high"]}],
                       "interleaved":{"field":"reasoning_content"},
                       "provider":{"shape":"responses"}}}}}
            """;

        AIProviderInfo[] providers = JsonSerializer.Deserialize(body, ModelInfoJsonContext.Default.AIProviderInfoArray)!;

        AIModelInfo model = providers[0].Models[0];
        await Assert.That(model.Status).IsEqualTo(AIModelStatus.Beta);
        await Assert.That(model.ReasoningOptions![0].Type).IsEqualTo(AIModelReasoningOptionType.Effort);
        await Assert.That(model.Interleaved!.Field).IsEqualTo(AIModelInterleavedField.ReasoningContent);
        await Assert.That(model.ProviderOverride!.Shape).IsEqualTo(AIModelProviderShape.Responses);
    }

    /// <summary>
    /// <c>experimental.modes.{mode}</c> nests a <c>cost</c> object and a <c>provider</c> override;
    /// it is not itself a cost. Deserializing it as a cost silently yields all-zero prices.
    /// </summary>
    [Test]
    public async Task ExperimentalModes_DeserializeNestedCostAndProvider_FromLiveShape()
    {
        const string body = """
            {"openai":{"name":"OpenAI","models":{
              "gpt-1":{"name":"GPT One",
                       "experimental":{"modes":{
                         "fast":{"cost":{"input":0.2625,"output":0.875,"cache_read":0.0525},
                                 "provider":{"body":{"service_tier":"priority"},
                                             "headers":{"x-mode":"fast"}}}}}}}}}
            """;

        AIProviderInfo[] providers = JsonSerializer.Deserialize(body, ModelInfoJsonContext.Default.AIProviderInfoArray)!;
        AIModelExperimentalMode mode = providers[0].Models[0].Experimental!.Modes!["fast"];

        await Assert.That(mode.Cost!.InputCostPerMillionTokens).IsEqualTo(0.2625m);
        await Assert.That(mode.Cost.OutputCostPerMillionTokens).IsEqualTo(0.875m);
        await Assert.That(mode.Cost.CacheReadPerMillionTokens).IsEqualTo(0.0525m);
        await Assert.That(mode.Provider!.Body!["service_tier"].GetString()).IsEqualTo("priority");
        await Assert.That(mode.Provider.Headers!["x-mode"]).IsEqualTo("fast");
    }

    /// <summary>Fields that exist on the live payload but were previously unmapped.</summary>
    [Test]
    public async Task ApiJson_PopulatesFieldsThatPreviouslyHadNoProperty()
    {
        const string body = """
            {"openai":{"name":"OpenAI","models":{
              "gpt-1":{"name":"GPT One","type":"chat","canonical_model_id":"openai/gpt-1",
                       "limit":{"context":400000,"output":128000},
                       "cost":{"input":1.25,"output":10,"reasoning":4.4,
                               "tiers":[{"input":0.4,"output":1.8,"cache_read":0.04,
                                         "cache_write":0.6,"reasoning":0.8,
                                         "tier":{"type":"context","size":272000}}]}}}}}
            """;

        AIModelInfo model = JsonSerializer
            .Deserialize(body, ModelInfoJsonContext.Default.AIProviderInfoArray)![0].Models[0];

        await Assert.That(model.Type).IsEqualTo("chat");
        await Assert.That(model.CanonicalModelId).IsEqualTo("openai/gpt-1");
        await Assert.That(model.Limit!.ContextTokens).IsEqualTo(400000);
        await Assert.That(model.Limit.OutputTokens).IsEqualTo(128000);
        await Assert.That(model.Cost!.ReasoningCostPerMillionTokens).IsEqualTo(4.4m);

        AIModelCostTier tier = model.Cost.Tiers[0];
        await Assert.That(tier.CacheReadPerMillionTokens).IsEqualTo(0.04m);
        await Assert.That(tier.CacheWritePerMillionTokens).IsEqualTo(0.6m);
        await Assert.That(tier.ReasoningCostPerMillionTokens).IsEqualTo(0.8m);
    }

    /// <summary>Fields on <c>/models.json</c> that were previously unmapped.</summary>
    [Test]
    public async Task ModelsJson_PopulatesFieldsThatPreviouslyHadNoProperty()
    {
        const string body = """
            {"oai/gpt-1":{"name":"GPT One","description":"A helpful model","type":"chat",
                          "license":"Apache-2.0",
                          "links":[{"label":"Model docs","url":"https://example.com/docs","type":"docs"}],
                          "weights":[{"label":"Base","url":"https://example.com/w","format":"safetensors","quantization":"fp8"}]}}
            """;

        AIModelMetadata metadata = JsonSerializer
            .Deserialize(body, ModelMetadataJsonContext.Default.AIModelMetadataArray)![0];

        await Assert.That(metadata.Id).IsEqualTo("oai/gpt-1");
        await Assert.That(metadata.Description).IsEqualTo("A helpful model");
        await Assert.That(metadata.Type).IsEqualTo("chat");
        await Assert.That(metadata.License).IsEqualTo("Apache-2.0");

        AIModelLinkInfo link = metadata.Links![0];
        await Assert.That(link.Label).IsEqualTo("Model docs");
        await Assert.That(link.Url).IsEqualTo("https://example.com/docs");
        await Assert.That(link.LinkType).IsEqualTo("docs");

        AIModelWeightInfo weight = metadata.Weights![0];
        await Assert.That(weight.Format).IsEqualTo("safetensors");
        await Assert.That(weight.Quantization).IsEqualTo("fp8");
    }

    /// <summary>
    /// <c>context_over_*</c> keys arrive as extension data and only become typed
    /// <see cref="AIModelCostInfo.ContextOverrides"/> once post-processed. The <c>/api.json</c>
    /// path must do that too, not just <c>/catalog.json</c>.
    /// </summary>
    [Test]
    public async Task ApiJson_ResolvesContextOverrides_OnEveryEntryPoint()
    {
        const string body = """
            {"openai":{"name":"OpenAI","models":{
              "gpt-1":{"name":"GPT One","cost":{"input":1.0,"output":2.0,
                                                "context_over_200k":{"input":0.4,"output":1.8}}}}}}
            """;

        CannedTransport transport = new(body);
        using ServiceProvider provider = BuildProvider(
            services => services.AddSingleton<IModelsDevTransport>(transport));

        IModelInfoRepository modelInfo = provider.GetRequiredService<IModelInfoRepository>();

        AIProviderInfo[] providers = await modelInfo.GetProviderInfosAsync();
        AIModelInfo byId = await modelInfo.GetModelInfoByIdAsync("openai", "gpt-1");
        IAsyncEnumerable<AIProviderInfo> enumerated = modelInfo.EnumerateProviderInfosAsync();

        List<AIModelInfo> observed = [providers[0].Models[0], byId];
        await foreach (AIProviderInfo p in enumerated)
        {
            observed.AddRange(p.Models);
        }

        await Assert.That(observed.Count).IsEqualTo(3);

        foreach (AIModelInfo model in observed)
        {
            await Assert.That(model.Cost!.ContextOverridesRaw).IsNull();
            await Assert.That(model.Cost.ContextOverrides.Keys).Contains("200k");
            await Assert.That(model.Cost.ContextOverrides["200k"].InputCostPerMillionTokens).IsEqualTo(0.4m);
        }
    }

    /// <summary>
    /// Properties that never appear in the live payload must not exist, otherwise they read as
    /// misleading defaults (empty string / false) that look like real upstream data.
    /// </summary>
    [Test]
    public async Task StaleProperties_AreAbsentFromThePublicSurface()
    {
        await Assert.That(typeof(AIProviderInfo).GetProperty("Description")).IsNull();
        await Assert.That(typeof(AIModelMetadata).GetProperty("StructuredOutput")).IsNull();
        await Assert.That(typeof(AIModelInfo).GetProperty("Modes")).IsNull();
        await Assert.That(typeof(AIModelInfo).GetProperty("Headers")).IsNull();
        await Assert.That(typeof(AIModelInfo).GetProperty("Body")).IsNull();
        await Assert.That(typeof(AIModelProviderOverride).GetProperty("Body")).IsNull();
        await Assert.That(typeof(AIModelProviderOverride).GetProperty("Headers")).IsNull();
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> substitute)
    {
        ServiceCollection services = new();
        services.AddModelsDotDevSharp();
        substitute(services);
        return services.BuildServiceProvider();
    }

    private sealed class CannedTransport(string body) : IModelsDevTransport
    {
        public Task<TElement[]> GetCollectionAsync<TElement>(
            string relativePath,
            System.Text.Json.Serialization.Metadata.JsonTypeInfo<TElement[]> typeInfo,
            CancellationToken cancellationToken = default)
        {
            TElement[]? decoded = JsonSerializer.Deserialize(body, typeInfo);
            return Task.FromResult(decoded ?? []);
        }
    }
}
