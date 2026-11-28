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

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelsDotDevSharp.Abstractions;
using ModelsDotDevSharp.Extensions;

namespace ModelsDotDevSharp.Tests;

/// <summary>
/// The live suite: fetches <c>/api.json</c>, <c>/models.json</c> and <c>/catalog.json</c> at run time
/// over real HTTP (no snapshot files are committed) and asserts value and pipeline behaviour on every
/// repository entry point wired through <see cref="ModelsDevServiceCollectionExtensions"/>, mirroring
/// how consumers run the library. On top of the entry points, exhaustive invariant loops visit every
/// real model with no sampling draw: no throw during deserialization, no extension-data residue on any
/// cost object, every JSON key mapping to a property through the SchemaWalk machinery, and generic
/// value assertions - dates parse, enums within closed sets, costs non-negative, reasoning option
/// min <= max where both present, and interleaved boolean-or-object consistency. Because the
/// repositories expose no raw bytes, the byte-level layer fetches each endpoint a second time per
/// run, deserializes those bytes with the strict test context, and hosts the two-directional drift
/// check with JSON paths.
/// </summary>
/// <remarks>
/// Network policy: the request timeout is configured through this suite's own service collection and
/// a small fixed retry loop in test code absorbs transient failures, so a red run separates a network
/// flake from a mapping error without any product change. The byte-level layer reuses that same
/// timeout and retry policy. Every failure reports the JSON path plus a
/// trimmed dump of the offending model or provider object, and each check that is expected red until
/// the concurrent mapping-fix session lands names the drift-comparison gap it guards.
/// </remarks>
public class LivePayloadTests
{
    private const string ApiJsonPath = "/api.json";
    private const string ModelsJsonPath = "/models.json";
    private const string CatalogJsonPath = "/catalog.json";

    /// <summary>The request timeout, wired through this suite's own service collection - never a product change.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The fixed delay between the retry loop's attempts.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private const int MaxAttempts = 3;
    private const int MaxReportedFailures = 10;
    private const int DumpLength = 240;
    private const string ContextOverridePrefix = "context_over_";

    private static readonly string[] CostWireKeys =
        ["input", "output", "input_audio", "output_audio", "cache_read", "cache_write", "reasoning"];

    private static readonly string[] TierWireKeys = ["input", "output", "cache_read", "cache_write", "reasoning"];

    /// <summary>
    /// The drift-comparison gap list. A test expected red until the mapping-fix session lands quotes
    /// the subset it guards from this list inside its failure message.
    /// </summary>
    private const string DriftComparisonGapList =
        "`limit` on AIModelInfo, `canonical_model_id`, model `type`, the experimental mode `{cost, provider}` shape, "
        + "cost `reasoning`, tier `cache_read`/`cache_write`/`reasoning`, metadata `description`/`license`/`links`/`type`, "
        + "weight `format`/`quantization`, and the retired stale properties";

    private const string ApiJsonGaps =
        "`limit` on AIModelInfo, `canonical_model_id`, model `type`, the experimental mode `{cost, provider}` shape, "
        + "cost `reasoning`, tier `cache_read`/`cache_write`/`reasoning`";

    private const string ModelsJsonGaps =
        "metadata `description`/`license`/`links`/`type`, weight `format`/`quantization`";

    private const string CatalogGaps =
        "the experimental mode `{cost, provider}` shape, cost `reasoning`, tier `cache_read`/`cache_write`/`reasoning`, "
        + "metadata `description`/`license`/`links`/`type`, weight `format`/`quantization`";

    /// <summary>The gap the stale direction of the drift comparison documents.</summary>
    private const string RetiredStalePropertiesGap = "the retired stale properties";

    /// <summary>
    /// Step 1 - the suite's own service collection wires the repositories with real HTTP and the
    /// request timeout, then smoke-fetches <c>/api.json</c>. A failure here is either a transient
    /// network flake (named by the retry loop) or a mapping error (a <see cref="JsonException"/> with
    /// its JSON path) - never an unclassified hang.
    /// </summary>
    [Test]
    public async Task LiveServiceCollection_TimesOutOnItsOwn_AndSmokeFetchesApiJson()
    {
        using ServiceProvider services = BuildLiveProvider();

        // The timeout is configured through this suite's own service collection; the client the
        // repositories create observes it because the factory applies the default-named options.
        using HttpClient client = services.GetRequiredService<IHttpClientFactory>().CreateClient();
        await Assert.That(client.Timeout).IsEqualTo(RequestTimeout);

        // The smoke path exercises the real HTTP request path through the repository, so the
        // replay-based comparisons below have a live baseline to trust.
        IModelInfoRepository repository = services.GetRequiredService<IModelInfoRepository>();
        JsonElement root = await RetryAsync(() => FetchRawJsonAsync(services, ApiJsonPath), ApiJsonPath);
        AIProviderInfo[] providers = await FetchFromRepositoryAsync(
            ApiJsonPath, () => repository.GetProviderInfosAsync(), ApiJsonGaps, root);

        await Assert.That(providers.Length).IsGreaterThan(0);
        await Assert.That(providers.Any(provider => provider.Models is { Length: > 0 })).IsTrue();
    }

    /// <summary>
    /// Step 2 - <see cref="IModelInfoRepository.GetProviderInfosAsync"/> against the live endpoint:
    /// every provider arrives with the pinned per-provider wire shape (id, name and a models array
    /// peeled from the provider's models map).
    /// </summary>
    [Test]
    public async Task GetProviderInfos_ReturnsEveryProvider_WithThePinnedPerProviderShapes()
    {
        using ServiceProvider services = BuildLiveProvider();

        LiveFetch<AIProviderInfo[]> live = await FetchLiveAsync(
            services,
            ApiJsonPath,
            static replay => replay.GetRequiredService<IModelInfoRepository>().GetProviderInfosAsync(),
            ApiJsonGaps);

        AIProviderInfo[] providers = live.Value;
        await Assert.That(providers.Length).IsGreaterThan(0);
        await Assert.That(providers.All(provider => !string.IsNullOrWhiteSpace(provider.Id))).IsTrue();
        await Assert.That(providers.All(provider => !string.IsNullOrWhiteSpace(provider.Name))).IsTrue();
        await Assert.That(providers.All(provider => provider.Models is not null && provider.Models.Length > 0)).IsTrue();
    }

    /// <summary>
    /// Step 2 - <see cref="IModelInfoRepository.GetProviderInfoByIdAsync"/> returns the very provider
    /// the collection endpoint served, proving the id that comes from the wire map key round-trips.
    /// </summary>
    [Test]
    public async Task GetProviderInfoById_ReturnsTheProvider_TheCollectionEndpointServed()
    {
        using ServiceProvider services = BuildLiveProvider();

        LiveFetch<AIProviderInfo[]> live = await FetchLiveAsync(
            services,
            ApiJsonPath,
            static replay => replay.GetRequiredService<IModelInfoRepository>().GetProviderInfosAsync(),
            ApiJsonGaps);

        AIProviderInfo first = live.Value[0];
        AIProviderInfo byId = await FetchFromReplayAsync(
            live,
            ApiJsonPath,
            replay => replay.GetRequiredService<IModelInfoRepository>().GetProviderInfoByIdAsync(first.Id),
            ApiJsonGaps);

        await Assert.That(byId.Id).IsEqualTo(first.Id);
        await Assert.That(byId.Name).IsEqualTo(first.Name);
        await Assert.That(byId.Models.Length).IsEqualTo(first.Models.Length);
    }

    /// <summary>
    /// Step 2 - <see cref="IModelInfoRepository.GetModelInfoByIdAsync"/> returns the very model the
    /// collection endpoint served, proving the per-provider model id round-trips through the pipeline.
    /// </summary>
    [Test]
    public async Task GetModelInfoById_ReturnsTheModel_TheCollectionEndpointServed()
    {
        using ServiceProvider services = BuildLiveProvider();

        LiveFetch<AIProviderInfo[]> live = await FetchLiveAsync(
            services,
            ApiJsonPath,
            static replay => replay.GetRequiredService<IModelInfoRepository>().GetProviderInfosAsync(),
            ApiJsonGaps);

        AIProviderInfo owner = live.Value.FirstOrDefault(provider => provider.Models is { Length: > 0 })
            ?? throw new InvalidOperationException(
                "The live /api.json payload carried no provider with models, so there is no model to look up.");

        AIModelInfo expected = owner.Models[0];
        AIModelInfo actual = await FetchFromReplayAsync(
            live,
            ApiJsonPath,
            replay => replay.GetRequiredService<IModelInfoRepository>().GetModelInfoByIdAsync(owner.Id, expected.Id),
            ApiJsonGaps);

        await Assert.That(actual.Id).IsEqualTo(expected.Id);
        await Assert.That(actual.Name).IsEqualTo(expected.Name);
    }

    /// <summary>
    /// Step 2 - <see cref="IModelInfoRepository.EnumerateProviderInfosAsync"/> yields exactly the
    /// providers the collection endpoint returned, one per wire map entry, in the same id set.
    /// </summary>
    [Test]
    public async Task EnumerateProviderInfos_YieldsTheSameProviders_AsTheCollectionEndpoint()
    {
        using ServiceProvider services = BuildLiveProvider();

        LiveFetch<AIProviderInfo[]> live = await FetchLiveAsync(
            services,
            ApiJsonPath,
            static replay => replay.GetRequiredService<IModelInfoRepository>().GetProviderInfosAsync(),
            ApiJsonGaps);

        AIProviderInfo[] enumerated = await FetchFromReplayAsync(
            live,
            ApiJsonPath,
            static async replay => await replay.GetRequiredService<IModelInfoRepository>()
                .EnumerateProviderInfosAsync().ToArrayAsync(),
            ApiJsonGaps);

        await Assert.That(enumerated.Length).IsEqualTo(live.Value.Length);
        await Assert.That(
            enumerated.Select(provider => provider.Id).ToHashSet()
                .SetEquals(live.Value.Select(provider => provider.Id))).IsTrue();
    }

    /// <summary>
    /// Step 2 - <see cref="IModelMetadataRepository"/> over <c>/models.json</c>: every record carries
    /// its composite <c>{provider}/{model}</c> id populated from the wire map key, and
    /// <see cref="IModelMetadataRepository.GetModelMetadataAsync"/> resolves that id back to the same
    /// record.
    /// </summary>
    [Test]
    public async Task EnumerateModelMetadata_YieldsEveryCompositeId_AndGetModelMetadataResolvesIt()
    {
        using ServiceProvider services = BuildLiveProvider();

        LiveFetch<AIModelMetadata[]> live = await FetchLiveAsync(
            services,
            ModelsJsonPath,
            static async replay => await replay.GetRequiredService<IModelMetadataRepository>()
                .EnumerateModelMetadataAsync().ToArrayAsync(),
            ModelsJsonGaps);

        await Assert.That(live.Value.Length).IsGreaterThan(0);
        await Assert.That(live.Value.All(
            metadata => !string.IsNullOrWhiteSpace(metadata.Id) && metadata.Id.Contains('/'))).IsTrue();

        AIModelMetadata first = live.Value[0];
        AIModelMetadata resolved = await FetchFromReplayAsync(
            live,
            ModelsJsonPath,
            replay => replay.GetRequiredService<IModelMetadataRepository>().GetModelMetadataAsync(first.Id!),
            ModelsJsonGaps);

        await Assert.That(resolved.Id).IsEqualTo(first.Id);
        await Assert.That(resolved.Name).IsEqualTo(first.Name);
    }

    /// <summary>
    /// Step 2 - <see cref="ICatalogRepository.GetCatalogAsync"/> returns both pinned views at once:
    /// the per-provider view (every provider's models map peeled into an array) and the canonical
    /// view (composite model keys), with cost context overrides already resolved by the pipeline.
    /// </summary>
    [Test]
    public async Task GetCatalog_ReturnsBothThePerProviderView_AndTheCanonicalView()
    {
        using ServiceProvider services = BuildLiveProvider();

        LiveFetch<AICatalog> live = await FetchLiveAsync(
            services,
            CatalogJsonPath,
            static replay => replay.GetRequiredService<ICatalogRepository>().GetCatalogAsync(),
            CatalogGaps);

        AICatalog catalog = live.Value;
        await Assert.That(catalog.Providers.Count).IsGreaterThan(0);
        await Assert.That(catalog.Models.Count).IsGreaterThan(0);
        await Assert.That(catalog.Providers.Values.All(
            provider => provider.Models is not null && provider.Models.Length > 0)).IsTrue();
        await Assert.That(catalog.Models.Keys.All(key => key.Contains('/'))).IsTrue();
        await Assert.That(catalog.Models.Values.All(
            metadata => metadata is not null && !string.IsNullOrWhiteSpace(metadata.Name))).IsTrue();
    }

    /// <summary>
    /// Step 3 - exhaustive invariant loop over every real model on <c>/api.json</c> (no sampling
    /// draw): identity, dates parse, enums within closed sets, reasoning option min <= max,
    /// interleaved boolean-or-object consistency, the limit block, the experimental mode
    /// <c>{cost, provider}</c> shape, non-negative costs, no extension-data residue on any reachable
    /// cost object, and a wire echo for every cost and drift-listed scalar. Each failure carries the
    /// JSON path plus a trimmed dump of the offending model or provider object.
    /// </summary>
    [Test]
    public async Task EveryRealApiJsonModel_HoldsTheValueAndResidueInvariants()
    {
        using ServiceProvider services = BuildLiveProvider();

        LiveFetch<AIProviderInfo[]> live = await FetchLiveAsync(
            services,
            ApiJsonPath,
            static replay => replay.GetRequiredService<IModelInfoRepository>().GetProviderInfosAsync(),
            ApiJsonGaps);

        Dictionary<string, AIProviderInfo> providersById = new(StringComparer.Ordinal);
        foreach (AIProviderInfo provider in live.Value)
        {
            providersById.TryAdd(provider.Id, provider);
        }

        List<InvariantFailure> failures = [];
        int checkedModels = 0;

        foreach (JsonProperty providerEntry in live.Root.EnumerateObject())
        {
            if (!providersById.TryGetValue(providerEntry.Name, out AIProviderInfo? provider))
            {
                Fail(failures, $"$.{providerEntry.Name}",
                    $"provider '{providerEntry.Name}' from the wire is missing from GetProviderInfosAsync",
                    providerEntry.Value);
                continue;
            }

            if (!providerEntry.Value.TryGetProperty("models", out JsonElement rawModels)
                || rawModels.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            Dictionary<string, AIModelInfo> modelsById = new(StringComparer.Ordinal);
            foreach (AIModelInfo model in provider.Models ?? [])
            {
                modelsById.TryAdd(model.Id, model);
            }

            foreach (JsonProperty modelEntry in rawModels.EnumerateObject())
            {
                string path = $"$.{providerEntry.Name}.models['{modelEntry.Name}']";
                if (!modelsById.TryGetValue(modelEntry.Name, out AIModelInfo? mapped))
                {
                    Fail(failures, path,
                        $"model '{modelEntry.Name}' from the wire is missing from GetProviderInfosAsync",
                        providerEntry.Value);
                    continue;
                }

                failures.AddRange(CheckApiJsonModel(modelEntry.Value, mapped, path));
                checkedModels++;
            }
        }

        if (failures.Count > 0)
        {
            throw AggregateFailure(ApiJsonPath, ApiJsonGaps, failures, checkedModels);
        }

        await Assert.That(checkedModels).IsGreaterThan(0);
    }

    /// <summary>
    /// Step 3 - exhaustive invariant loop over every real record in <c>/models.json</c> (no sampling
    /// draw): the map key must populate the id, dates parse, and the drift-listed metadata fields
    /// (<c>type</c>, <c>description</c>, <c>license</c>, <c>links</c>, weight
    /// <c>format</c>/<c>quantization</c>) echo the wire bytes.
    /// </summary>
    [Test]
    public async Task EveryRealModelsJsonRecord_HoldsTheValueInvariants()
    {
        using ServiceProvider services = BuildLiveProvider();

        LiveFetch<AIModelMetadata[]> live = await FetchLiveAsync(
            services,
            ModelsJsonPath,
            static async replay => await replay.GetRequiredService<IModelMetadataRepository>()
                .EnumerateModelMetadataAsync().ToArrayAsync(),
            ModelsJsonGaps);

        Dictionary<string, AIModelMetadata> byId = new(StringComparer.Ordinal);
        foreach (AIModelMetadata metadata in live.Value)
        {
            if (!string.IsNullOrWhiteSpace(metadata.Id))
            {
                byId.TryAdd(metadata.Id, metadata);
            }
        }

        List<InvariantFailure> failures = [];
        int checkedRecords = 0;

        foreach (JsonProperty modelEntry in live.Root.EnumerateObject())
        {
            string path = $"$.['{modelEntry.Name}']";
            if (!byId.TryGetValue(modelEntry.Name, out AIModelMetadata? mapped))
            {
                Fail(failures, path,
                    $"model '{modelEntry.Name}' from the wire is missing from EnumerateModelMetadataAsync",
                    modelEntry.Value);
                continue;
            }

            failures.AddRange(CheckMetadata(modelEntry.Value, mapped, path, requireMapKeyId: true));
            checkedRecords++;
        }

        if (failures.Count > 0)
        {
            throw AggregateFailure(ModelsJsonPath, ModelsJsonGaps, failures, checkedRecords);
        }

        await Assert.That(checkedRecords).IsGreaterThan(0);
    }

    /// <summary>
    /// Step 3 - exhaustive invariant loop over every real object <c>/catalog.json</c> carries (no
    /// sampling draw): the per-provider view's models run the <c>/api.json</c> model invariants and
    /// the canonical view's records run the metadata invariants, with dictionary keys as identity.
    /// </summary>
    [Test]
    public async Task EveryRealCatalogObject_HoldsTheValueAndResidueInvariants()
    {
        using ServiceProvider services = BuildLiveProvider();

        LiveFetch<AICatalog> live = await FetchLiveAsync(
            services,
            CatalogJsonPath,
            static replay => replay.GetRequiredService<ICatalogRepository>().GetCatalogAsync(),
            CatalogGaps);

        AICatalog catalog = live.Value;
        List<InvariantFailure> failures = [];
        int checkedObjects = 0;

        if (live.Root.TryGetProperty("providers", out JsonElement rawProviders)
            && rawProviders.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty providerEntry in rawProviders.EnumerateObject())
            {
                if (!catalog.Providers.TryGetValue(providerEntry.Name, out AIProviderInfo? provider))
                {
                    Fail(failures, $"$.providers['{providerEntry.Name}']",
                        $"provider '{providerEntry.Name}' from the wire is missing from GetCatalogAsync",
                        providerEntry.Value);
                    continue;
                }

                if (!providerEntry.Value.TryGetProperty("models", out JsonElement rawModels)
                    || rawModels.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                Dictionary<string, AIModelInfo> modelsById = new(StringComparer.Ordinal);
                foreach (AIModelInfo model in provider.Models ?? [])
                {
                    modelsById.TryAdd(model.Id, model);
                }

                foreach (JsonProperty modelEntry in rawModels.EnumerateObject())
                {
                    string path = $"$.providers['{providerEntry.Name}'].models['{modelEntry.Name}']";
                    if (!modelsById.TryGetValue(modelEntry.Name, out AIModelInfo? mapped))
                    {
                        Fail(failures, path,
                            $"model '{modelEntry.Name}' from the wire is missing from the provider's models view",
                            providerEntry.Value);
                        continue;
                    }

                    failures.AddRange(CheckApiJsonModel(modelEntry.Value, mapped, path));
                    checkedObjects++;
                }
            }
        }

        if (live.Root.TryGetProperty("models", out JsonElement rawCatalogModels)
            && rawCatalogModels.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty modelEntry in rawCatalogModels.EnumerateObject())
            {
                string path = $"$.models['{modelEntry.Name}']";
                if (!catalog.Models.TryGetValue(modelEntry.Name, out AIModelMetadata? mapped) || mapped is null)
                {
                    Fail(failures, path,
                        $"model '{modelEntry.Name}' from the wire is missing from the catalog's canonical view",
                        modelEntry.Value);
                    continue;
                }

                failures.AddRange(CheckMetadata(modelEntry.Value, mapped, path, requireMapKeyId: false));
                checkedObjects++;
            }
        }

        if (failures.Count > 0)
        {
            throw AggregateFailure(CatalogJsonPath, CatalogGaps, failures, checkedObjects);
        }

        await Assert.That(checkedObjects).IsGreaterThan(0);
    }

    /// <summary>
    /// Step 3 - the key-mapping invariant: every JSON key in every real payload maps to a mapped
    /// property, via the SchemaWalk machinery from the strict-context helper. Unmapped keys fail with
    /// the JSON path, a trimmed dump of the offending model or provider object, and the gap the key
    /// belongs to on the drift-comparison list (or an explicit "new upstream drift" verdict).
    /// </summary>
    [Test]
    public async Task EveryRealPayloadKey_MapsToAMappedProperty()
    {
        using ServiceProvider services = BuildLiveProvider();

        JsonElement apiJson = await RetryAsync(() => FetchRawJsonAsync(services, ApiJsonPath), ApiJsonPath);
        JsonElement modelsJson = await RetryAsync(() => FetchRawJsonAsync(services, ModelsJsonPath), ModelsJsonPath);
        JsonElement catalogJson = await RetryAsync(() => FetchRawJsonAsync(services, CatalogJsonPath), CatalogJsonPath);

        List<string> failures = [];
        CollectUnmappedKeys(failures, ApiJsonPath, SchemaWalk.WalkApiJson(apiJson), apiJson);
        CollectUnmappedKeys(failures, ModelsJsonPath, SchemaWalk.WalkModelsJson(modelsJson), modelsJson);
        CollectUnmappedKeys(failures, CatalogJsonPath, SchemaWalk.WalkCatalogJson(catalogJson), catalogJson);

        if (failures.Count > 0)
        {
            StringBuilder message = new();
            message.AppendLine("Every payload JSON key must map to a mapped property (SchemaWalk found unmapped keys):");
            foreach (string failure in failures)
            {
                message.AppendLine(failure);
            }

            throw new InvalidOperationException(message.ToString());
        }
    }

    // ------------------------------------------------------------------------------------------
    // Byte-level layer: the endpoint bytes fetched a second time per run (the repositories expose
    // no raw bytes), strictly deserialized with the test context, then walked in both drift
    // directions. Every failure carries a JSON path, a trimmed dump, and its named gap.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Byte-level layer, step 1 - fetches each endpoint's bytes a second time per run, separate from
    /// the repository-layer fetches, through the same timeout and retry policy. No repository entry
    /// point participates, so a failure here is only ever a transient network flake (named by the
    /// retry loop once it exhausts its attempts), never a mapping error.
    /// </summary>
    [Test]
    public async Task ByteLevelFetch_SucceedsIndependentlyOfTheRepositoryLayer()
    {
        using ServiceProvider services = BuildLiveProvider();

        (string Endpoint, JsonElement Root)[] payloads = await FetchByteLevelPayloadsAsync(services);

        foreach ((string endpoint, JsonElement root) in payloads)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Any())
            {
                throw new InvalidOperationException(
                    $"The byte-level pass could not fetch a usable {endpoint} payload: its second fetch "
                    + "of this endpoint returned no JSON object. A failure here is a network flake, never "
                    + "a mapping error.");
            }
        }
    }

    /// <summary>
    /// Byte-level layer, step 2 - deserializes every bound object the endpoints' bytes carry with
    /// the strict test context from SchemaWalk (unmapped members disallowed plus the named
    /// converters), so an unknown member or a wrong value shape throws instead of being skipped. The
    /// dynamic id maps are peeled manually here, the way the walk does: each provider's models map is
    /// rewritten into the array the product's flattening converter would build, because those
    /// converters hardcode the product's contexts. Every rejection reports the JSON path, a trimmed
    /// dump, and the gap it documents.
    /// </summary>
    [Test]
    public async Task ByteLevelStrictDeserialization_RejectsUnknownMembersWithJsonPaths()
    {
        using ServiceProvider services = BuildLiveProvider();
        (string Endpoint, JsonElement Root)[] payloads = await FetchByteLevelPayloadsAsync(services);

        List<string> failures = [];
        int deserialized = 0;
        foreach ((string endpoint, JsonElement root) in payloads)
        {
            deserialized += StrictDeserializeEndpoint(endpoint, root, failures);
        }

        if (failures.Count > 0)
        {
            throw AggregateByteLevelFailure(
                "The strict test context rejected a real payload object - an unknown member or a wrong value "
                + "shape reached the strict deserializer:",
                failures);
        }

        await Assert.That(deserialized).IsGreaterThan(0);
    }

    /// <summary>
    /// Byte-level layer, steps 3 and 4 - runs the reflection-built schema walk against the fetched
    /// bytes and reports both drift directions with JSON paths: every unmapped key, and every mapped
    /// property no fetched payload object anywhere carries. The allowlist for deliberately unmapped
    /// keys is consulted on every walk; it is empty today and any future entry must carry a written
    /// reason. Each failure carries a trimmed dump of the offending object and names the gap it
    /// documents - the retired stale properties for the stale direction, the mapped key's gap (or an
    /// explicit new-upstream-drift verdict) for the unmapped direction.
    /// </summary>
    [Test]
    public async Task ByteLevelSchemaWalk_ReportsBothDriftDirectionsWithJsonPathsAndNamedGaps()
    {
        using ServiceProvider services = BuildLiveProvider();
        (string Endpoint, JsonElement Root)[] payloads = await FetchByteLevelPayloadsAsync(services);

        IReadOnlyList<SchemaWalkAllowlistEntry> allowlist = SchemaWalkAllowlist.Entries;
        await Assert.That(allowlist.All(entry => !string.IsNullOrWhiteSpace(entry.Reason))).IsTrue();

        Dictionary<Type, HashSet<string>> presentByType = CollectPresentKeysByType(payloads);

        List<string> failures = [];
        foreach ((string endpoint, JsonElement root) in payloads)
        {
            SchemaWalkReport report = endpoint switch
            {
                ApiJsonPath => SchemaWalk.WalkApiJson(root, allowlist),
                ModelsJsonPath => SchemaWalk.WalkModelsJson(root, allowlist),
                CatalogJsonPath => SchemaWalk.WalkCatalogJson(root, allowlist),
                _ => throw new InvalidOperationException($"The byte-level pass has no walk shape for '{endpoint}'."),
            };

            CollectDriftFindings(failures, endpoint, report, root, presentByType);        }

        if (failures.Count > 0)
        {
            throw AggregateByteLevelFailure(
                "The reflection-built schema walk found drift between the fetched bytes and the mapped shapes:",
                failures);
        }
    }

    // ------------------------------------------------------------------------------------------
    // Wiring: the suite's own service collection, the timeout, and the fixed retry loop.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the suite's own service collection exactly the way a consumer wires the library -
    /// <c>AddModelsDotDevSharp</c> with real HTTP - and configures the request timeout there, on the
    /// default-named client the repositories create. No product option or package is involved.
    /// </summary>
    private static ServiceProvider BuildLiveProvider()
    {
        ServiceCollection services = new();
        services.AddModelsDotDevSharp();

        // The timeout lives in the test's own service collection: the factory applies these options
        // to every default-named client, which is the client HttpClientHelper hands to the transport.
        services.AddHttpClient(Options.DefaultName, client => client.Timeout = RequestTimeout);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A small fixed retry loop around one fetch. Transient transport failures (timeouts, refused
    /// connections, error status codes) are retried up to <see cref="MaxAttempts"/> times; a
    /// <see cref="JsonException"/> is a mapping error and propagates immediately so the failure can
    /// never be misread as a network flake.
    /// </summary>
    private static async Task<T> RetryAsync<T>(Func<Task<T>> operation, string endpoint)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception exception) when (IsTransientNetworkFailure(exception))
            {
                if (attempt >= MaxAttempts)
                {
                    throw new InvalidOperationException(
                        $"{endpoint} still failed after {MaxAttempts} attempts - transient network flake, "
                        + $"not a mapping error. Failure: {exception.Message}",
                        exception);
                }

                await Task.Delay(RetryDelay);
            }
        }
    }

    private static bool IsTransientNetworkFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or IOException;

    /// <summary>Fetches the raw body of one endpoint through the suite's own configured client.</summary>
    private static async Task<string> FetchRawBodyAsync(ServiceProvider services, string relativePath)
    {
        IHttpClientFactory factory = services.GetRequiredService<IHttpClientFactory>();
        ModelsDevOptions options = services.GetRequiredService<IOptions<ModelsDevOptions>>().Value;

        using HttpClient client = factory.CreateClient();
        client.BaseAddress = new Uri(options.BaseAddress, UriKind.Absolute);

        using HttpResponseMessage response = await client.GetAsync(relativePath);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Fetches the raw bytes of one endpoint through the suite's own configured client.</summary>
    private static async Task<JsonElement> FetchRawJsonAsync(ServiceProvider services, string relativePath)
    {
        string body = await FetchRawBodyAsync(services, relativePath);
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Captures one live response body per endpoint, then replays that same body through the real
    /// repository entry points, so the wire view and the mapped view reflect the same payload. Only
    /// the initial body capture touches the network. A <see cref="JsonException"/> from the replayed
    /// repository path is the "no throw during deserialization" invariant failing, reported with its
    /// JSON path, a trimmed dump of the offending object, and the guarded gap.
    /// </summary>
    private static async Task<LiveFetch<T>> FetchLiveAsync<T>(
        ServiceProvider services,
        string relativePath,
        Func<IServiceProvider, Task<T>> fetch,
        string gap)
    {
        string body = await RetryAsync(() => FetchRawBodyAsync(services, relativePath), relativePath);
        JsonElement root;
        using (JsonDocument document = JsonDocument.Parse(body))
        {
            root = document.RootElement.Clone();
        }

        using ServiceProvider replay = BuildReplayProvider(body);
        T value = await FetchFromRepositoryAsync(relativePath, () => fetch(replay), gap, root);
        return new LiveFetch<T>(value, root, body);
    }

    /// <summary>
    /// Replays the captured body behind <paramref name="live"/> through another real repository entry
    /// point, so lookup and enumeration comparisons observe the same payload as the collection fetch.
    /// </summary>
    private static async Task<T> FetchFromReplayAsync<T, TLive>(
        LiveFetch<TLive> live,
        string relativePath,
        Func<IServiceProvider, Task<T>> fetch,
        string gap)
    {
        using ServiceProvider replay = BuildReplayProvider(live.Body);
        return await FetchFromRepositoryAsync(relativePath, () => fetch(replay), gap, live.Root);
    }

    /// <summary>
    /// Builds a consumer-wired provider whose HTTP layer replays <paramref name="body"/> instead of
    /// touching the network, keeping every real repository entry point in the decoding path.
    /// </summary>
    private static ServiceProvider BuildReplayProvider(string body)
    {
        ServiceCollection services = new();
        services.AddModelsDotDevSharp();
        services.AddSingleton<IHttpClientFactory>(new ReplayHttpClientFactory(new ReplayHttpMessageHandler(body)));
        return services.BuildServiceProvider();
    }

    /// <summary>Runs one repository entry point under the retry loop, mapping deserialization failures.</summary>
    private static async Task<T> FetchFromRepositoryAsync<T>(
        string relativePath,
        Func<Task<T>> fetch,
        string gap,
        JsonElement dumpRoot)
    {
        try
        {
            return await RetryAsync(fetch, relativePath);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Live payload invariant failed: no throw during deserialization - the repository pipeline "
                + $"rejected {relativePath}.\n"
                + $"JSON path: {exception.Path ?? "(the serializer reported no path)"}\n"
                + $"Gap (expected-red until the mapping-fix session lands): {gap}\n"
                + $"Offending object (trimmed): "
                + $"{DumpAt(dumpRoot, exception.Path) ?? "(the path could not be resolved in the fetched payload)"}\n"
                + $"Failure: {exception.Message}",
                exception);
        }
    }

    // ------------------------------------------------------------------------------------------
    // Exhaustive per-model invariants.
    // ------------------------------------------------------------------------------------------

    /// <summary>One invariant violation: where it broke, what broke, and the trimmed offending object.</summary>
    private sealed record InvariantFailure(string JsonPath, string Invariant, string? Dump);

    /// <summary>The repository view of an endpoint next to the suite's raw byte view of the same fetch.</summary>
    private sealed record LiveFetch<T>(T Value, JsonElement Root, string Body);

    /// <summary>Answers every request with the captured live body, so replays never touch the network.</summary>
    private sealed class ReplayHttpMessageHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }

    /// <summary>Hands out clients backed by the replay handler.</summary>
    private sealed class ReplayHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static List<InvariantFailure> CheckApiJsonModel(JsonElement rawModel, AIModelInfo model, string path)
    {
        List<InvariantFailure> failures = [];

        if (rawModel.ValueKind != JsonValueKind.Object)
        {
            Fail(failures, path, $"the model arrived as {rawModel.ValueKind}; an object was expected", rawModel);
            return failures;
        }

        // Identity: the id carried by the provider's models map key must land on the model.
        if (string.IsNullOrWhiteSpace(model.Id))
        {
            Fail(failures, path, "the model id from the provider's models map key must populate AIModelInfo.Id", rawModel);
        }

        // Drift-listed scalars echo the wire bytes.
        CheckWireString(failures, rawModel, "name", model.Name, path, rawModel);
        CheckWireString(failures, rawModel, "type", model.Type, path, rawModel);
        CheckWireString(failures, rawModel, "canonical_model_id", model.CanonicalModelId, path, rawModel);

        // Dates parse: a non-empty wire date must land as a parsed DateOnly in a sane range.
        CheckDate(failures, rawModel, "knowledge", model.Knowledge, path, rawModel);
        CheckDate(failures, rawModel, "release_date", model.ReleaseDate, path, rawModel);
        CheckDate(failures, rawModel, "last_updated", model.LastUpdatedDate, path, rawModel);

        // Enums within closed sets.
        if (model.Status is { } status)
        {
            CheckEnum(failures, path, "status", status, rawModel);
        }

        if (model.ReasoningOptions is { } options)
        {
            for (int index = 0; index < options.Length; index++)
            {
                if (options[index] is not null)
                {
                    CheckEnum(failures, $"{path}.reasoning_options[{index}]", "type", options[index].Type, rawModel);
                }
            }
        }

        if (model.Interleaved?.Field is { } field)
        {
            CheckEnum(failures, path, "interleaved.field", field, rawModel);
        }

        if (model.ProviderOverride?.Shape is { } shape)
        {
            CheckEnum(failures, path, "provider.shape", shape, rawModel);
        }

        // The wire must actually feed the closed-set status enum - a silently unset mapped value
        // would otherwise hide a mapping gap behind the enum's default.
        if (TryWireString(rawModel, "status", out string wireStatus)
            && !string.IsNullOrWhiteSpace(wireStatus)
            && model.Status is null)
        {
            Fail(failures, path, $"wire `status` = '{wireStatus}' did not map to AIModelStatus", rawModel);
        }

        // Interleaved boolean-or-object consistency against the wire union.
        CheckInterleaved(failures, rawModel, model, path);

        // Reasoning options: min <= max where both are present, plus the wire echo.
        CheckReasoningOptions(failures, rawModel, model, path);

        // The per-provider view's pinned limit block.
        CheckLimit(failures, rawModel, model, path);

        // The model-level provider override arrives as an object on the wire.
        if (rawModel.TryGetProperty("provider", out JsonElement rawProvider)
            && rawProvider.ValueKind == JsonValueKind.Object
            && model.ProviderOverride is null)
        {
            Fail(failures, $"{path}.provider", "wire `provider` did not map to AIModelProviderOverride", rawModel);
        }

        // The experimental mode {cost, provider} shape, entry by entry.
        CheckExperimentalModes(failures, rawModel, model, path);

        // Costs: extension-data residue and non-negative values across the whole reachable tree.
        failures.AddRange(SweepMappedCosts(model, path, rawModel));

        // Costs: the wire echo of every numeric field, tier, and context override.
        if (rawModel.TryGetProperty("cost", out JsonElement rawCost)
            && rawCost.ValueKind is not JsonValueKind.Null)
        {
            if (rawCost.ValueKind != JsonValueKind.Object)
            {
                Fail(failures, $"{path}.cost", $"`cost` arrived as {rawCost.ValueKind}; an object was expected", rawModel);
            }
            else if (model.Cost is null)
            {
                Fail(failures, $"{path}.cost", "wire `cost` did not map to AIModelCostInfo", rawModel);
            }
            else
            {
                CheckRawCost(failures, rawCost, model.Cost, $"{path}.cost", rawModel);
            }
        }

        return failures;
    }

    private static List<InvariantFailure> CheckMetadata(
        JsonElement rawMetadata,
        AIModelMetadata metadata,
        string path,
        bool requireMapKeyId)
    {
        List<InvariantFailure> failures = [];

        if (rawMetadata.ValueKind != JsonValueKind.Object)
        {
            Fail(failures, path, $"the metadata record arrived as {rawMetadata.ValueKind}; an object was expected", rawMetadata);
            return failures;
        }

        if (requireMapKeyId && string.IsNullOrWhiteSpace(metadata.Id))
        {
            Fail(failures, path, "the model id from the models.json map key must populate AIModelMetadata.Id", rawMetadata);
        }

        CheckWireString(failures, rawMetadata, "name", metadata.Name, path, rawMetadata);
        CheckWireString(failures, rawMetadata, "type", metadata.Type, path, rawMetadata);
        CheckWireString(failures, rawMetadata, "description", metadata.Description, path, rawMetadata);
        CheckWireString(failures, rawMetadata, "license", metadata.License, path, rawMetadata);

        CheckDate(failures, rawMetadata, "knowledge", metadata.Knowledge, path, rawMetadata);
        CheckDate(failures, rawMetadata, "release_date", metadata.ReleaseDate, path, rawMetadata);
        CheckDate(failures, rawMetadata, "last_updated", metadata.LastUpdatedDate, path, rawMetadata);

        CheckWireArray(failures, rawMetadata, "links", metadata.Links?.Count ?? -1, $"{path}.links", rawMetadata);
        CheckWireArray(failures, rawMetadata, "weights", metadata.Weights?.Count ?? -1, $"{path}.weights", rawMetadata);
        CheckWireArray(failures, rawMetadata, "benchmarks", metadata.Benchmarks?.Count ?? -1, $"{path}.benchmarks", rawMetadata);

        // The weight `format` / `quantization` entries from the drift comparison.
        if (rawMetadata.TryGetProperty("weights", out JsonElement rawWeights)
            && rawWeights.ValueKind == JsonValueKind.Array
            && metadata.Weights is { } weights
            && weights.Count == rawWeights.GetArrayLength())
        {
            int index = 0;
            foreach (JsonElement rawWeight in rawWeights.EnumerateArray())
            {
                if (rawWeight.ValueKind == JsonValueKind.Object && weights[index] is { } weight)
                {
                    string weightPath = $"{path}.weights[{index}]";
                    CheckWireString(failures, rawWeight, "format", weight.Format, weightPath, rawMetadata);
                    CheckWireString(failures, rawWeight, "quantization", weight.Quantization, weightPath, rawMetadata);
                }

                index++;
            }
        }

        // Blocks the canonical view needs must not silently deserialise to null when the wire carries them.
        if (rawMetadata.TryGetProperty("modalities", out JsonElement rawModalities)
            && rawModalities.ValueKind == JsonValueKind.Object
            && metadata.Modalities is null)
        {
            Fail(failures, $"{path}.modalities", "wire `modalities` did not map to AIModelModalities", rawMetadata);
        }

        if (rawMetadata.TryGetProperty("limit", out JsonElement rawLimit)
            && rawLimit.ValueKind == JsonValueKind.Object
            && metadata.Limit is null)
        {
            Fail(failures, $"{path}.limit", "wire `limit` did not map to AIModelLimit", rawMetadata);
        }

        return failures;
    }

    /// <summary>
    /// Sweeps every cost reachable from a model - base cost, experimental mode costs, and every
    /// nested context override - asserting no extension-data residue survives post-processing and no
    /// cost field is negative. The sweep is driven by the mapped tree, so it covers every cost object
    /// whether or not the wire echo above looked at it.
    /// </summary>
    private static List<InvariantFailure> SweepMappedCosts(AIModelInfo model, string path, JsonElement dump)
    {
        List<InvariantFailure> failures = [];
        Stack<(AIModelCostInfo Cost, string CostPath)> pending = new();

        if (model.Cost is { } baseCost)
        {
            pending.Push((baseCost, $"{path}.cost"));
        }

        if (model.Experimental?.Modes is { } modes)
        {
            foreach (KeyValuePair<string, AIModelExperimentalMode> mode in modes)
            {
                if (mode.Value?.Cost is { } modeCost)
                {
                    pending.Push((modeCost, $"{path}.experimental.modes.{mode.Key}.cost"));
                }
            }
        }

        while (pending.Count > 0)
        {
            (AIModelCostInfo cost, string costPath) = pending.Pop();

            if (cost.ContextOverridesRaw is not null)
            {
                Fail(failures, costPath,
                    "extension-data residue: `context_over_*` keys remain in ContextOverridesRaw instead of "
                    + "resolving into the typed ContextOverrides tree",
                    dump);
            }

            foreach (string key in CostWireKeys)
            {
                if (CostValue(cost, key) is { } value && value < 0)
                {
                    Fail(failures, $"{costPath}.{key}", $"cost `{key}` = {value} is negative; costs are non-negative", dump);
                }
            }

            AIModelCostTier[] tiers = cost.Tiers ?? [];
            for (int index = 0; index < tiers.Length; index++)
            {
                foreach (string key in TierWireKeys)
                {
                    if (TierValue(tiers[index], key) is { } value && value < 0)
                    {
                        Fail(failures, $"{costPath}.tiers[{index}].{key}",
                            $"tier cost `{key}` = {value} is negative; costs are non-negative", dump);
                    }
                }
            }

            foreach (KeyValuePair<string, AIModelCostInfo> resolved in cost.ContextOverrides)
            {
                pending.Push((resolved.Value, $"{costPath}.{ContextOverridePrefix}{resolved.Key}"));
            }
        }

        return failures;
    }

    private static void CheckRawCost(
        List<InvariantFailure> failures,
        JsonElement rawCost,
        AIModelCostInfo mapped,
        string path,
        JsonElement dump)
    {
        if (rawCost.ValueKind != JsonValueKind.Object)
        {
            Fail(failures, path, $"`cost` arrived as {rawCost.ValueKind}; an object was expected", dump);
            return;
        }

        foreach (string key in CostWireKeys)
        {
            CheckCostField(failures, rawCost, key, CostValue(mapped, key), $"{path}.{key}", dump);
        }

        if (rawCost.TryGetProperty("tiers", out JsonElement rawTiers) && rawTiers.ValueKind is not JsonValueKind.Null)
        {
            AIModelCostTier[] mappedTiers = mapped.Tiers ?? [];
            if (rawTiers.ValueKind != JsonValueKind.Array)
            {
                Fail(failures, $"{path}.tiers", $"`tiers` arrived as {rawTiers.ValueKind}", dump);
            }
            else if (mappedTiers.Length != rawTiers.GetArrayLength())
            {
                Fail(failures, $"{path}.tiers",
                    $"wire `tiers` carries {rawTiers.GetArrayLength()} entries but the mapped cost holds "
                    + CountText(mappedTiers.Length),
                    dump);
            }
            else
            {
                int index = 0;
                foreach (JsonElement rawTier in rawTiers.EnumerateArray())
                {
                    string tierPath = $"{path}.tiers[{index}]";
                    AIModelCostTier tier = mappedTiers[index];

                    if (rawTier.ValueKind != JsonValueKind.Object)
                    {
                        Fail(failures, tierPath, $"a tier arrived as {rawTier.ValueKind}", dump);
                    }
                    else if (tier is null)
                    {
                        Fail(failures, tierPath, "a tier deserialized to null", dump);
                    }
                    else
                    {
                        foreach (string key in TierWireKeys)
                        {
                            CheckCostField(failures, rawTier, key, TierValue(tier, key), $"{tierPath}.{key}", dump);
                        }

                        if (rawTier.TryGetProperty("tier", out JsonElement rawTierInfo)
                            && rawTierInfo.ValueKind == JsonValueKind.Object
                            && tier.TierInfo is null)
                        {
                            Fail(failures, $"{tierPath}.tier", "wire tier info did not map to AIModelTierInfo", dump);
                        }
                    }

                    index++;
                }
            }
        }

        // Every context_over_* wire key must resolve into the typed ContextOverrides tree.
        foreach (JsonProperty member in rawCost.EnumerateObject())
        {
            if (!member.Name.StartsWith(ContextOverridePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string suffix = member.Name[ContextOverridePrefix.Length..];
            if (!mapped.ContextOverrides.TryGetValue(suffix, out AIModelCostInfo? resolved) || resolved is null)
            {
                Fail(failures, $"{path}.{member.Name}",
                    $"wire key `{member.Name}` did not resolve into ContextOverrides[\"{suffix}\"]", dump);
                continue;
            }

            if (member.Value.ValueKind == JsonValueKind.Object)
            {
                CheckRawCost(failures, member.Value, resolved, $"{path}.{member.Name}", dump);
            }
        }
    }

    private static void CheckCostField(
        List<InvariantFailure> failures,
        JsonElement rawOwner,
        string wireKey,
        decimal? mappedValue,
        string path,
        JsonElement dump)
    {
        if (!rawOwner.TryGetProperty(wireKey, out JsonElement raw)
            || raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (raw.ValueKind != JsonValueKind.Number)
        {
            Fail(failures, path, $"cost `{wireKey}` arrived as {raw.ValueKind}; costs are JSON numbers", dump);
            return;
        }

        if (!raw.TryGetDecimal(out decimal wireValue))
        {
            Fail(failures, path, $"cost `{wireKey}` = {Trim(raw.GetRawText())} is not a representable decimal cost", dump);
            return;
        }

        if (mappedValue is null)
        {
            Fail(failures, path, $"wire cost `{wireKey}` = {wireValue} did not map to a cost property", dump);
            return;
        }

        if (mappedValue.Value != wireValue)
        {
            Fail(failures, path,
                $"mapped cost `{wireKey}` = {mappedValue.Value} disagrees with the wire value {wireValue}", dump);
        }
    }

    private static void CheckDate(
        List<InvariantFailure> failures,
        JsonElement rawOwner,
        string field,
        DateOnly? mapped,
        string path,
        JsonElement dump)
    {
        if (!rawOwner.TryGetProperty(field, out JsonElement raw)
            || raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (raw.ValueKind != JsonValueKind.String)
        {
            Fail(failures, path, $"`{field}` arrived as {raw.ValueKind}; dates are strings", dump);
            return;
        }

        string? text = raw.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return; // An empty wire date legitimately maps to a null DateOnly.
        }

        if (mapped is null)
        {
            Fail(failures, path, $"wire date `{field}` = '{text}' did not parse into a DateOnly", dump);
            return;
        }

        if (mapped.Value < new DateOnly(1970, 1, 1))
        {
            Fail(failures, path, $"parsed date `{field}` = {mapped:yyyy-MM-dd} precedes the 1970 epoch sentinel", dump);
            return;
        }

        DateOnly upperBound = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(730));
        if (mapped.Value > upperBound)
        {
            Fail(failures, path, $"parsed date `{field}` = {mapped:yyyy-MM-dd} lies more than two years in the future", dump);
        }
    }

    private static void CheckEnum<TEnum>(List<InvariantFailure> failures, string path, string field, TEnum value, JsonElement dump)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(typeof(TEnum), value))
        {
            Fail(failures, path,
                $"`{field}` = '{value}' lies outside the closed set of {typeof(TEnum).Name} values", dump);
        }
    }

    private static void CheckInterleaved(List<InvariantFailure> failures, JsonElement rawModel, AIModelInfo model, string path)
    {
        if (!rawModel.TryGetProperty("interleaved", out JsonElement raw)
            || raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            if (model.Interleaved is not null)
            {
                Fail(failures, path, "no `interleaved` member on the wire must map to a null AIModelInterleaved", rawModel);
            }

            return;
        }

        switch (raw.ValueKind)
        {
            case JsonValueKind.False:
                if (model.Interleaved is not null)
                {
                    Fail(failures, path,
                        "`interleaved:false` on the wire must map to a null AIModelInterleaved (the false form of the union)",
                        rawModel);
                }

                break;

            case JsonValueKind.True:
                if (model.Interleaved is null || model.Interleaved.Field is not null)
                {
                    Fail(failures, path,
                        "`interleaved:true` on the wire must map to a bare AIModelInterleaved with no field",
                        rawModel);
                }

                break;

            case JsonValueKind.Object:
                if (model.Interleaved is null)
                {
                    Fail(failures, path,
                        "the object form of `interleaved` must map to a non-null AIModelInterleaved",
                        rawModel);
                }
                else if (TryWireString(raw, "field", out string fieldName)
                    && !string.IsNullOrWhiteSpace(fieldName)
                    && model.Interleaved.Field is null)
                {
                    Fail(failures, path,
                        $"`interleaved.field` = '{fieldName}' did not map to AIModelInterleavedField; an unrecognised "
                        + "wire value silently degrades the object form to the bare boolean form",
                        rawModel);
                }

                break;

            default:
                Fail(failures, path,
                    $"`interleaved` arrived as {raw.ValueKind}; the union expects boolean, null, or object",
                    rawModel);
                break;
        }
    }

    private static void CheckReasoningOptions(List<InvariantFailure> failures, JsonElement rawModel, AIModelInfo model, string path)
    {
        AIModelReasoningOption[]? options = model.ReasoningOptions;

        // min <= max where both are present.
        if (options is not null)
        {
            for (int index = 0; index < options.Length; index++)
            {
                AIModelReasoningOption? option = options[index];
                if (option is null)
                {
                    Fail(failures, $"{path}.reasoning_options[{index}]", "a reasoning option deserialized to null", rawModel);
                    continue;
                }

                if (option.Min is { } min && option.Max is { } max && min > max)
                {
                    Fail(failures, $"{path}.reasoning_options[{index}]",
                        $"reasoning option min {min} exceeds max {max}", rawModel);
                }
            }
        }

        if (!rawModel.TryGetProperty("reasoning_options", out JsonElement rawOptions)
            || rawOptions.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (rawOptions.ValueKind != JsonValueKind.Array)
        {
            Fail(failures, path, $"`reasoning_options` arrived as {rawOptions.ValueKind}", rawModel);
            return;
        }

        int wireCount = rawOptions.GetArrayLength();
        if (options is null || options.Length != wireCount)
        {
            Fail(failures, path,
                $"wire `reasoning_options` carries {wireCount} entries but the mapped model holds "
                + CountText(options?.Length ?? -1),
                rawModel);
            return;
        }

        int wireIndex = 0;
        foreach (JsonElement rawOption in rawOptions.EnumerateArray())
        {
            string optionPath = $"{path}.reasoning_options[{wireIndex}]";
            if (rawOption.ValueKind != JsonValueKind.Object)
            {
                Fail(failures, optionPath, $"a reasoning option arrived as {rawOption.ValueKind}", rawModel);
            }
            else if (options[wireIndex] is { } option)
            {
                CheckInt32(failures, rawOption, "min", option.Min, optionPath, rawModel);
                CheckInt32(failures, rawOption, "max", option.Max, optionPath, rawModel);
            }

            wireIndex++;
        }
    }

    private static void CheckLimit(List<InvariantFailure> failures, JsonElement rawModel, AIModelInfo model, string path)
    {
        if (!rawModel.TryGetProperty("limit", out JsonElement rawLimit)
            || rawLimit.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        string limitPath = $"{path}.limit";
        if (rawLimit.ValueKind != JsonValueKind.Object)
        {
            Fail(failures, limitPath, $"`limit` arrived as {rawLimit.ValueKind}", rawModel);
            return;
        }

        if (model.Limit is null)
        {
            Fail(failures, limitPath, "wire `limit` did not map to an AIModelLimit", rawModel);
            return;
        }

        CheckInt32(failures, rawLimit, "context", model.Limit.ContextTokens, limitPath, rawModel);
        CheckInt32(failures, rawLimit, "input", model.Limit.InputTokens, limitPath, rawModel);
        CheckInt32(failures, rawLimit, "output", model.Limit.OutputTokens, limitPath, rawModel);
    }

    private static void CheckExperimentalModes(
        List<InvariantFailure> failures,
        JsonElement rawModel,
        AIModelInfo model,
        string path)
    {
        if (!rawModel.TryGetProperty("experimental", out JsonElement rawExperimental)
            || rawExperimental.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        string experimentalPath = $"{path}.experimental";
        if (rawExperimental.ValueKind != JsonValueKind.Object)
        {
            Fail(failures, experimentalPath, $"`experimental` arrived as {rawExperimental.ValueKind}", rawModel);
            return;
        }

        if (model.Experimental is null)
        {
            Fail(failures, experimentalPath, "wire `experimental` did not map to AIModelExperimental", rawModel);
            return;
        }

        if (!rawExperimental.TryGetProperty("modes", out JsonElement rawModes)
            || rawModes.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        string modesPath = $"{experimentalPath}.modes";
        if (rawModes.ValueKind != JsonValueKind.Object)
        {
            Fail(failures, modesPath, $"`modes` arrived as {rawModes.ValueKind}", rawModel);
            return;
        }

        Dictionary<string, AIModelExperimentalMode>? modes = model.Experimental.Modes;
        foreach (JsonProperty rawMode in rawModes.EnumerateObject())
        {
            string modePath = $"{modesPath}.{rawMode.Name}";

            if (modes is null
                || !modes.TryGetValue(rawMode.Name, out AIModelExperimentalMode? mode)
                || mode is null)
            {
                Fail(failures, modePath,
                    $"experimental mode '{rawMode.Name}' from the wire did not survive the pipeline", rawModel);
                continue;
            }

            if (rawMode.Value.ValueKind != JsonValueKind.Object)
            {
                Fail(failures, modePath, $"an experimental mode arrived as {rawMode.Value.ValueKind}", rawModel);
                continue;
            }

            if (rawMode.Value.TryGetProperty("cost", out JsonElement rawModeCost)
                && rawModeCost.ValueKind is not JsonValueKind.Null)
            {
                if (rawModeCost.ValueKind != JsonValueKind.Object)
                {
                    Fail(failures, $"{modePath}.cost", $"a mode `cost` arrived as {rawModeCost.ValueKind}", rawModel);
                }
                else if (mode.Cost is null)
                {
                    Fail(failures, $"{modePath}.cost",
                        "wire mode `cost` did not map to AIModelCostInfo - the experimental mode `{cost, provider}` shape",
                        rawModel);
                }
                else
                {
                    CheckRawCost(failures, rawModeCost, mode.Cost, $"{modePath}.cost", rawModel);
                }
            }

            if (rawMode.Value.TryGetProperty("provider", out JsonElement rawModeProvider)
                && rawModeProvider.ValueKind == JsonValueKind.Object)
            {
                if (mode.Provider is null)
                {
                    Fail(failures, $"{modePath}.provider",
                        "wire mode `provider` did not map to AIModelModeProvider - the experimental mode "
                        + "`{cost, provider}` shape",
                        rawModel);
                }
                else
                {
                    if (rawModeProvider.TryGetProperty("body", out JsonElement rawBody)
                        && rawBody.ValueKind != JsonValueKind.Null
                        && mode.Provider.Body is null)
                    {
                        Fail(failures, $"{modePath}.provider.body", "wire mode provider `body` did not map", rawModel);
                    }

                    if (rawModeProvider.TryGetProperty("headers", out JsonElement rawHeaders)
                        && rawHeaders.ValueKind != JsonValueKind.Null
                        && mode.Provider.Headers is null)
                    {
                        Fail(failures, $"{modePath}.provider.headers", "wire mode provider `headers` did not map", rawModel);
                    }
                }
            }
        }
    }

    private static void CheckWireString(
        List<InvariantFailure> failures,
        JsonElement rawOwner,
        string field,
        string? mapped,
        string path,
        JsonElement dump)
    {
        if (!rawOwner.TryGetProperty(field, out JsonElement raw)
            || raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (raw.ValueKind != JsonValueKind.String)
        {
            Fail(failures, path, $"`{field}` arrived as {raw.ValueKind}; a string was expected", dump);
            return;
        }

        string wire = raw.GetString() ?? string.Empty;
        if (mapped is null)
        {
            Fail(failures, path, $"wire `{field}` = \"{Trim(wire)}\" did not map to a property", dump);
            return;
        }

        if (!string.Equals(mapped, wire, StringComparison.Ordinal))
        {
            Fail(failures, path,
                $"mapped `{field}` disagrees with the wire value (wire \"{Trim(wire)}\", mapped \"{Trim(mapped)}\")",
                dump);
        }
    }

    private static void CheckWireArray(
        List<InvariantFailure> failures,
        JsonElement rawOwner,
        string field,
        int mappedCount,
        string path,
        JsonElement dump)
    {
        if (!rawOwner.TryGetProperty(field, out JsonElement raw)
            || raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (raw.ValueKind != JsonValueKind.Array)
        {
            Fail(failures, path, $"`{field}` arrived as {raw.ValueKind}; an array was expected", dump);
            return;
        }

        int wireCount = raw.GetArrayLength();
        if (mappedCount != wireCount)
        {
            Fail(failures, path,
                $"wire `{field}` carries {wireCount} entries but the mapped view holds {CountText(mappedCount)}", dump);
        }
    }

    private static void CheckInt32(
        List<InvariantFailure> failures,
        JsonElement rawOwner,
        string field,
        int? mapped,
        string path,
        JsonElement dump)
    {
        if (!rawOwner.TryGetProperty(field, out JsonElement raw)
            || raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (raw.ValueKind != JsonValueKind.Number || !raw.TryGetInt32(out int wireValue))
        {
            Fail(failures, path, $"`{field}` = {Trim(raw.GetRawText())} is not a 32-bit integer on the wire", dump);
            return;
        }

        if (mapped is null)
        {
            Fail(failures, path, $"wire `{field}` = {wireValue} did not map to a property", dump);
            return;
        }

        if (mapped.Value != wireValue)
        {
            Fail(failures, path, $"mapped `{field}` = {mapped.Value} disagrees with the wire value {wireValue}", dump);
        }
    }

    private static bool TryWireString(JsonElement owner, string field, out string value)
    {
        value = string.Empty;
        if (owner.ValueKind != JsonValueKind.Object
            || !owner.TryGetProperty(field, out JsonElement raw)
            || raw.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = raw.GetString() ?? string.Empty;
        return true;
    }

    private static decimal? CostValue(AIModelCostInfo cost, string wireKey) => wireKey switch
    {
        "input" => cost.InputCostPerMillionTokens,
        "output" => cost.OutputCostPerMillionTokens,
        "input_audio" => cost.AudioInputCostPerMillionTokens,
        "output_audio" => cost.AudioOutputCostPerMillionTokens,
        "cache_read" => cost.CacheReadPerMillionTokens,
        "cache_write" => cost.CacheWritePerMillionTokens,
        "reasoning" => cost.ReasoningCostPerMillionTokens,
        _ => null,
    };

    private static decimal? TierValue(AIModelCostTier tier, string wireKey) => wireKey switch
    {
        "input" => tier.InputCostPerMillionTokens,
        "output" => tier.OutputCostPerMillionTokens,
        "cache_read" => tier.CacheReadPerMillionTokens,
        "cache_write" => tier.CacheWritePerMillionTokens,
        "reasoning" => tier.ReasoningCostPerMillionTokens,
        _ => null,
    };

    // ------------------------------------------------------------------------------------------
    // Failure output: JSON path + trimmed dump + the guarded gap.
    // ------------------------------------------------------------------------------------------

    private static void Fail(List<InvariantFailure> failures, string path, string invariant, JsonElement dump) =>
        failures.Add(new InvariantFailure(path, invariant, Trimmed(dump)));

    private static InvalidOperationException AggregateFailure(
        string endpoint,
        string gap,
        List<InvariantFailure> failures,
        int checkedCount)
    {
        StringBuilder message = new();
        message.Append("Live payload invariant failures: ")
            .Append(failures.Count)
            .Append(" violation(s) across ")
            .Append(checkedCount.ToString(CultureInfo.InvariantCulture))
            .Append(" real object(s) fetched from ")
            .Append(endpoint)
            .AppendLine(".");
        message.Append("Gap (expected-red until the mapping-fix session lands): ")
            .AppendLine(gap)
            .AppendLine();

        foreach (InvariantFailure failure in failures.Take(MaxReportedFailures))
        {
            message.Append("JSON path: ").AppendLine(failure.JsonPath);
            message.Append("  invariant: ").AppendLine(failure.Invariant);
            message.Append("  object (trimmed): ").AppendLine(failure.Dump ?? "(unavailable)");
        }

        if (failures.Count > MaxReportedFailures)
        {
            message.Append("... and ")
                .Append((failures.Count - MaxReportedFailures).ToString(CultureInfo.InvariantCulture))
                .AppendLine(" more failure(s) trimmed from this report.");
        }

        return new InvalidOperationException(message.ToString());
    }

    // ------------------------------------------------------------------------------------------
    // SchemaWalk key-mapping pass: unmapped keys with JSON paths, dumps, and per-key gaps.
    // ------------------------------------------------------------------------------------------

    private static void CollectUnmappedKeys(
        List<string> failures,
        string endpoint,
        SchemaWalkReport report,
        JsonElement root)
    {
        List<SchemaDriftFinding> unmapped =
            [.. report.Findings.Where(finding => finding.Kind == SchemaDriftKind.UnmappedKey)];

        if (unmapped.Count == 0)
        {
            return;
        }

        failures.Add($"{unmapped.Count} payload key(s) on {endpoint} map to no mapped property. "
            + $"Gap list (expected-red until the mapping-fix session lands): {DriftComparisonGapList}.");

        foreach (SchemaDriftFinding finding in unmapped.Take(MaxReportedFailures))
        {
            string dump = DumpAt(root, finding.Path)
                ?? "(the offending object could not be resolved in the fetched payload)";
            failures.Add($"JSON path: {finding.Path} | gap: {GapFor(endpoint, finding.Path, finding.Key)} | unmapped key: "
                + $"`{finding.Key}` | object (trimmed): {Trim(dump)}"
                + (finding.Detail is null ? string.Empty : $" | detail: {finding.Detail}"));
        }

        if (unmapped.Count > MaxReportedFailures)
        {
            failures.Add($"... and {unmapped.Count - MaxReportedFailures} more unmapped payload key(s) trimmed "
                + "from this report.");
        }
    }

    /// <summary>
    /// Maps one finding to the drift-comparison gap it belongs to, or explicitly reports that its key
    /// is new upstream drift rather than a gap the list already names.
    /// </summary>
    private static string GapFor(string endpoint, string path, string key)
    {
        if (path.Contains(".experimental.modes", StringComparison.Ordinal) && key is "cost" or "provider")
        {
            return "the experimental mode `{cost, provider}` shape";
        }

        if (key is "cache_read" or "cache_write")
        {
            return "tier `cache_read`/`cache_write`/`reasoning`";
        }

        if (key == "reasoning")
        {
            return path.Contains(".cost", StringComparison.Ordinal) || path.Contains(".tiers[", StringComparison.Ordinal)
                ? "cost `reasoning`, tier `cache_read`/`cache_write`/`reasoning`"
                : "cost `reasoning`";
        }

        if (key == "limit" && endpoint == ApiJsonPath)
        {
            return "`limit` on AIModelInfo";
        }

        if (key == "canonical_model_id")
        {
            return "`canonical_model_id`";
        }

        if (key == "type")
        {
            bool modelShaped = endpoint switch
            {
                ApiJsonPath => path.Contains(".models[", StringComparison.Ordinal),
                CatalogJsonPath => path.Contains(".providers", StringComparison.Ordinal),
                _ => false,
            };
            return modelShaped ? "model `type`" : "metadata `type`";
        }

        if (key is "description" or "license" or "links")
        {
            return $"metadata `{key}`";
        }

        if (key is "format" or "quantization")
        {
            return $"weight `{key}`";
        }

        return "not on the drift-comparison list - a new upstream key the mappings have never seen";
    }

    // ------------------------------------------------------------------------------------------
    // Byte-level layer: the second fetch, strict-context deserialization, and both drift
    // directions.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Fetches each endpoint's bytes a second time per run - separate from any repository-layer
    /// fetch, through the same timeout and retry policy - so the byte-level pass owns its own bytes,
    /// since the repositories expose no raw bytes.
    /// </summary>
    private static async Task<(string Endpoint, JsonElement Root)[]> FetchByteLevelPayloadsAsync(
        ServiceProvider services) =>
    [
        (ApiJsonPath, await RetryAsync(() => FetchRawJsonAsync(services, ApiJsonPath), ApiJsonPath)),
        (ModelsJsonPath, await RetryAsync(() => FetchRawJsonAsync(services, ModelsJsonPath), ModelsJsonPath)),
        (CatalogJsonPath, await RetryAsync(() => FetchRawJsonAsync(services, CatalogJsonPath), CatalogJsonPath)),
    ];

    /// <summary>
    /// Strictly deserializes every bound object one endpoint's bytes carry with
    /// <see cref="SchemaWalkJsonContext"/> - unmapped members disallowed, named converters applied -
    /// peeling the dynamic id maps manually the way the walk does. Returns the number of objects
    /// submitted; every rejection lands in <paramref name="failures"/> with its JSON path, a trimmed
    /// dump, and the gap it documents.
    /// </summary>
    private static int StrictDeserializeEndpoint(string endpoint, JsonElement root, List<string> failures)
    {
        int deserialized = 0;
        switch (endpoint)
        {
            case ApiJsonPath:
                // The provider id map: each provider (models map flattened into the array the
                // product's converter would build) deserializes strictly, models included.
                foreach (JsonProperty provider in root.EnumerateObject())
                {
                    deserialized++;
                    DeserializeStrict(failures, endpoint, $"$.['{provider.Name}']",
                        FlattenProviderModels(provider.Value), SchemaWalkJsonContext.Default.AIProviderInfo, root);
                }

                break;

            case ModelsJsonPath:
                // The model id map: the key becomes the id, so each record object deserializes as-is.
                foreach (JsonProperty record in root.EnumerateObject())
                {
                    deserialized++;
                    DeserializeStrict(failures, endpoint, $"$.['{record.Name}']", record.Value.GetRawText(),
                        SchemaWalkJsonContext.Default.AIModelMetadata, root);
                }

                break;

            case CatalogJsonPath:
                if (root.TryGetProperty("providers", out JsonElement providers)
                    && providers.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty provider in providers.EnumerateObject())
                    {
                        deserialized++;
                        DeserializeStrict(failures, endpoint, $"$.providers['{provider.Name}']",
                            FlattenProviderModels(provider.Value), SchemaWalkJsonContext.Default.AIProviderInfo, root);
                    }
                }
                else
                {
                    string actual = root.TryGetProperty("providers", out JsonElement actualProviders)
                        ? actualProviders.ValueKind.ToString()
                        : "missing";
                    string dump = DumpAt(root, "$.providers") ?? Trim(root.GetRawText());
                    failures.Add($"endpoint: {endpoint} | JSON path: $.providers | object (trimmed): {dump} "
                        + $"| failure: Expected JSON object for \"providers\" but found {actual}.");
                }

                if (root.TryGetProperty("models", out JsonElement models)
                    && models.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty record in models.EnumerateObject())
                    {
                        deserialized++;
                        DeserializeStrict(failures, endpoint, $"$.models['{record.Name}']", record.Value.GetRawText(),
                            SchemaWalkJsonContext.Default.AIModelMetadata, root);
                    }
                }
                else
                {
                    string actual = root.TryGetProperty("models", out JsonElement actualModels)
                        ? actualModels.ValueKind.ToString()
                        : "missing";
                    string dump = DumpAt(root, "$.models") ?? Trim(root.GetRawText());
                    failures.Add($"endpoint: {endpoint} | JSON path: $.models | object (trimmed): {dump} "
                        + $"| failure: Expected JSON object for \"models\" but found {actual}.");
                }

                break;

            default:
                throw new InvalidOperationException(
                    $"The byte-level pass has no strict deserialization shape for '{endpoint}'.");
        }

        return deserialized;
    }

    /// <summary>
    /// Deserializes one fragment with its strict <see cref="JsonTypeInfo{T}"/>, mapping a
    /// <see cref="JsonException"/> to a path-precise failure: the composed JSON path, the gap the
    /// offending key documents, and a trimmed dump of the offending object.
    /// </summary>
    private static void DeserializeStrict<T>(
        List<string> failures,
        string endpoint,
        string path,
        string json,
        JsonTypeInfo<T> typeInfo,
        JsonElement dumpRoot)
    {
        try
        {
            JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException exception)
        {
            string fullPath = ComposeJsonPath(path, exception.Path);
            string dump = DumpAt(dumpRoot, fullPath)
                ?? "(the offending object could not be resolved in the fetched payload)";
            failures.Add($"endpoint: {endpoint} | JSON path: {fullPath} "
                + $"| gap: {GapFor(endpoint, fullPath, LastPathSegment(fullPath))} | object (trimmed): {Trim(dump)} "
                + $"| failure: {exception.Message}");
        }
    }

    /// <summary>
    /// Rewrites one provider object's <c>models</c> map into the array the product's flattening
    /// converter would build, so the strict context can deserialize a whole provider - models
    /// included - without the product's converter, which hardcodes the product's contexts.
    /// </summary>
    private static string FlattenProviderModels(JsonElement provider)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            foreach (JsonProperty member in provider.EnumerateObject())
            {
                if (member.NameEquals("models") && member.Value.ValueKind == JsonValueKind.Object)
                {
                    writer.WritePropertyName("models");
                    writer.WriteStartArray();
                    foreach (JsonProperty model in member.Value.EnumerateObject())
                    {
                        model.Value.WriteTo(writer);
                    }

                    writer.WriteEndArray();
                    continue;
                }

                member.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>
    /// Collects both drift directions from one endpoint's walk report: every unmapped key (through
    /// the key-mapping collector above), plus every mapped property no fetched object mapped to
    /// its declaring type carries. Presence is tracked per mapped type and wire name, so an
    /// optional field absent from some objects of its own type is not stale, but a key carried
    /// only by other types or as a dictionary key does not mask genuine retirement.
    /// </summary>
    private static void CollectDriftFindings(
        List<string> failures,
        string endpoint,
        SchemaWalkReport report,
        JsonElement root,
        IReadOnlyDictionary<Type, HashSet<string>> presentByType)
    {
        CollectUnmappedKeys(failures, endpoint, report, root);

        foreach (IGrouping<(Type? DeclaringType, string Key), SchemaDriftFinding> group in report.Findings
            .Where(finding => finding.Kind == SchemaDriftKind.StaleProperty)
            .GroupBy(finding => (finding.DeclaringType, finding.Key)))
        {
            if (group.Key.DeclaringType is { } declaringType
                && presentByType.TryGetValue(declaringType, out HashSet<string>? present)
                && present.Contains(group.Key.Key))
            {
                continue;
            }

            if (group.Key.DeclaringType is null
                && presentByType.Values.Any(keys => keys.Contains(group.Key.Key)))
            {
                continue;
            }

            SchemaDriftFinding first = group.First();
            string dump = DumpAt(root, first.Path)
                ?? "(the offending object could not be resolved in the fetched payload)";
            failures.Add($"{group.Count()} object(s) on {endpoint} carry no `{group.Key.Key}` member, and no fetched "
                + $"object mapped to {(group.Key.DeclaringType?.Name ?? "an unknown type")} carries it either. Gap list (expected-red until the mapping-fix "
                + $"session lands): {RetiredStalePropertiesGap}. JSON path: {first.Path} "
                + $"| gap: {RetiredStalePropertiesGap} | object (trimmed): {Trim(dump)}");
        }
    }

    /// <summary>
    /// Every wire name carried by JSON members on objects mapped to each type, across all
    /// endpoints that map to that type. Only members on objects walked as that mapped type count;
    /// dictionary keys (provider/model/mode ids) and members on other types do not.
    /// </summary>
    private static Dictionary<Type, HashSet<string>> CollectPresentKeysByType((string Endpoint, JsonElement Root)[] payloads)
    {
        Dictionary<Type, HashSet<string>> present = new();

        foreach ((string endpoint, JsonElement root) in payloads)
        {
            switch (endpoint)
            {
                case ApiJsonPath:
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty provider in root.EnumerateObject())
                        {
                            AccumulatePresentKeys(provider.Value, typeof(AIProviderInfo), present, idFromMapKey: true);
                        }
                    }

                    break;

                case ModelsJsonPath:
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty record in root.EnumerateObject())
                        {
                            AccumulatePresentKeys(record.Value, typeof(AIModelMetadata), present, idFromMapKey: true);
                        }
                    }

                    break;

                case CatalogJsonPath:
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("providers", out JsonElement providers)
                            && providers.ValueKind == JsonValueKind.Object)
                        {
                            foreach (JsonProperty provider in providers.EnumerateObject())
                            {
                                AccumulatePresentKeys(provider.Value, typeof(AIProviderInfo), present, idFromMapKey: true);
                            }
                        }

                        if (root.TryGetProperty("models", out JsonElement models)
                            && models.ValueKind == JsonValueKind.Object)
                        {
                            foreach (JsonProperty record in models.EnumerateObject())
                            {
                                AccumulatePresentKeys(record.Value, typeof(AIModelMetadata), present, idFromMapKey: true);
                            }
                        }
                    }

                    break;
            }
        }

        return present;
    }

    private static void AccumulatePresentKeys(
        JsonElement value,
        Type declaredType,
        Dictionary<Type, HashSet<string>> present,
        bool idFromMapKey)
    {
        Type type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            Type valueType = type.GetGenericArguments()[1];
            if (!SchemaWalk.MappedTypes.Contains(valueType))
            {
                return;
            }

            foreach (JsonProperty member in value.EnumerateObject())
            {
                AccumulatePresentKeys(member.Value, valueType, present, idFromMapKey: true);
            }

            return;
        }

        if (type.IsArray)
        {
            Type? elementType = type.GetElementType();
            if (elementType is null)
            {
                return;
            }

            if (value.ValueKind == JsonValueKind.Object && SchemaWalk.MappedTypes.Contains(elementType))
            {
                foreach (JsonProperty member in value.EnumerateObject())
                {
                    AccumulatePresentKeys(member.Value, elementType, present, idFromMapKey: true);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array && SchemaWalk.MappedTypes.Contains(elementType))
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    AccumulatePresentKeys(item, elementType, present, idFromMapKey: false);
                }
            }

            return;
        }

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
        {
            Type elementType = type.GetGenericArguments()[0];
            if (value.ValueKind == JsonValueKind.Array && SchemaWalk.MappedTypes.Contains(elementType))
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    AccumulatePresentKeys(item, elementType, present, idFromMapKey: false);
                }
            }

            return;
        }

        if (value.ValueKind == JsonValueKind.Object && SchemaWalk.MappedTypes.Contains(type))
        {
            AccumulatePresentKeysFromObject(value, type, present, idFromMapKey);
        }
    }

    private static void AccumulatePresentKeysFromObject(
        JsonElement element,
        Type type,
        Dictionary<Type, HashSet<string>> present,
        bool idFromMapKey)
    {
        IReadOnlySet<string> expectedKeys = SchemaWalk.ExpectedKeys(type);
        if (!present.TryGetValue(type, out HashSet<string>? keys))
        {
            keys = new HashSet<string>(StringComparer.Ordinal);
            present[type] = keys;
        }

        if (idFromMapKey && expectedKeys.Contains("id"))
        {
            keys.Add("id");
        }

        foreach (JsonProperty member in element.EnumerateObject())
        {
            if (expectedKeys.Contains(member.Name))
            {
                keys.Add(member.Name);

                PropertyInfo? property = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(candidate =>
                        candidate.GetCustomAttribute<JsonPropertyNameAttribute>() is { Name: string name }
                        && string.Equals(name, member.Name, StringComparison.Ordinal));
                if (property is not null)
                {
                    AccumulatePresentKeys(member.Value, property.PropertyType, present, idFromMapKey: false);
                }

                continue;
            }

            if (type == typeof(AIModelCostInfo)
                && member.Name.StartsWith(ContextOverridePrefix, StringComparison.Ordinal)
                && member.Value.ValueKind == JsonValueKind.Object)
            {
                AccumulatePresentKeys(member.Value, typeof(AIModelCostInfo), present, idFromMapKey: false);
            }
        }
    }

    /// <summary>
    /// Composes a fragment's path with the serializer's own exception path, so dumps and gap keys
    /// resolve no matter whether the serializer reported nothing, a root-relative path, or a member.
    /// </summary>
    private static string ComposeJsonPath(string fragmentPath, string? serializerPath)
    {
        if (string.IsNullOrWhiteSpace(serializerPath))
        {
            return fragmentPath;
        }

        string suffix = serializerPath[0] == '$' ? serializerPath[1..] : serializerPath;
        if (suffix.Length == 0)
        {
            return fragmentPath;
        }

        return suffix[0] is '.' or '[' ? fragmentPath + suffix : fragmentPath + "." + suffix;
    }

    private static string LastPathSegment(string path)
    {
        List<string> segments = SplitPath(path);
        return segments.Count > 0 ? segments[^1] : path;
    }

    private static InvalidOperationException AggregateByteLevelFailure(string headline, List<string> failures)
    {
        StringBuilder message = new();
        message.Append("Byte-level drift pass failures: ")
            .Append(failures.Count.ToString(CultureInfo.InvariantCulture))
            .AppendLine(".");
        message.AppendLine(headline);

        foreach (string failure in failures.Take(MaxReportedFailures))
        {
            message.AppendLine(failure);
        }

        if (failures.Count > MaxReportedFailures)
        {
            message.Append("... and ")
                .Append((failures.Count - MaxReportedFailures).ToString(CultureInfo.InvariantCulture))
                .AppendLine(" more failure(s) trimmed from this report.");
        }

        return new InvalidOperationException(message.ToString());
    }

    // ------------------------------------------------------------------------------------------
    // JSON path resolution for trimmed dumps.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves a JSON path (SchemaWalk style, <c>$.a.models['b'].cost</c>, or serializer style,
    /// <c>.a.models.b.cost</c> or <c>[0].name</c>) against a payload and returns a trimmed dump of
    /// the deepest object or array it names - the offending model or provider object.
    /// </summary>
    private static string? DumpAt(JsonElement root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        List<string> segments = SplitPath(path);
        JsonElement current = root;
        JsonElement container = root;

        foreach (string segment in segments)
        {
            container = current;
            if (!TryStep(current, segment, out JsonElement next))
            {
                break;
            }

            current = next;
        }

        JsonElement target = current.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? current : container;
        return target.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? Trimmed(target) : null;
    }

    private static bool TryStep(JsonElement current, string segment, out JsonElement next)
    {
        next = current;
        if (current.ValueKind == JsonValueKind.Object)
        {
            return current.TryGetProperty(segment, out next);
        }

        if (current.ValueKind == JsonValueKind.Array
            && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            && index >= 0
            && index < current.GetArrayLength())
        {
            next = current[index];
            return true;
        }

        return false;
    }

    private static List<string> SplitPath(string path)
    {
        List<string> segments = [];
        int index = path.Length > 0 && path[0] == '$' ? 1 : 0;

        while (index < path.Length)
        {
            char character = path[index];
            if (character == '.')
            {
                index++;
                int start = index;
                while (index < path.Length && path[index] != '.' && path[index] != '[')
                {
                    index++;
                }

                if (index > start)
                {
                    segments.Add(path[start..index]);
                }
            }
            else if (character == '[')
            {
                index++;
                if (index < path.Length && path[index] == '\'')
                {
                    index++;
                    StringBuilder key = new();
                    while (index < path.Length)
                    {
                        if (path[index] == '\\' && index + 1 < path.Length)
                        {
                            key.Append(path[index + 1]);
                            index += 2;
                            continue;
                        }

                        if (path[index] == '\'')
                        {
                            break;
                        }

                        key.Append(path[index]);
                        index++;
                    }

                    index++; // closing quote
                    if (index < path.Length && path[index] == ']')
                    {
                        index++;
                    }

                    segments.Add(key.ToString());
                }
                else
                {
                    int start = index;
                    while (index < path.Length && path[index] != ']')
                    {
                        index++;
                    }

                    segments.Add(path[start..index]);
                    if (index < path.Length)
                    {
                        index++; // closing bracket
                    }
                }
            }
            else
            {
                int start = index;
                while (index < path.Length && path[index] != '.' && path[index] != '[')
                {
                    index++;
                }

                if (index > start)
                {
                    segments.Add(path[start..index]);
                }
            }
        }

        return segments;
    }

    private static string Trimmed(JsonElement element) => Trim(element.GetRawText());

    private static string Trim(string text) =>
        text.Length <= DumpLength ? text : $"{text[..DumpLength]}... (trimmed, {text.Length} chars total)";

    private static string CountText(int count) =>
        count < 0 ? "none" : count.ToString(CultureInfo.InvariantCulture);
}
