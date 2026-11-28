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

using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ModelsDotDevSharp.Converters;

namespace ModelsDotDevSharp.Tests;

/// <summary>
/// Test-only string enum converter that accepts a single named value but rejects numeric tokens
/// and comma-separated combinations such as "alpha, beta".
/// </summary>
internal sealed class StrictAIModelStatusConverter : JsonConverter<AIModelStatus>
{
    public override AIModelStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Unsupported AIModelStatus token {reader.TokenType}; expected a single wire name such as \"beta\".");
        }

        string? text = reader.GetString();
        if (text is not null && text.Contains(',', StringComparison.Ordinal))
        {
            throw new JsonException(
                $"Unsupported AIModelStatus value \"{text}\"; expected a single wire name such as \"beta\".");
        }

        return text switch
        {
            "alpha" => AIModelStatus.Alpha,
            "beta" => AIModelStatus.Beta,
            "deprecated" => AIModelStatus.Deprecated,
            _ => throw new JsonException(
                $"Unsupported AIModelStatus value \"{text}\"; expected \"alpha\", \"beta\" or \"deprecated\"."),
        };
    }

    public override void Write(Utf8JsonWriter writer, AIModelStatus value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            AIModelStatus.Alpha => "alpha",
            AIModelStatus.Beta => "beta",
            AIModelStatus.Deprecated => "deprecated",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        });
    }
}

/// <summary>
/// Test-only string enum converter that accepts named values but rejects numeric tokens.
/// </summary>
internal sealed class StrictAIModelReasoningOptionTypeConverter() : JsonStringEnumConverter<AIModelReasoningOptionType>(allowIntegerValues: false);

/// <summary>
/// Test-only string enum converter that accepts named values but rejects numeric tokens.
/// </summary>
internal sealed class StrictAIModelProviderShapeConverter() : JsonStringEnumConverter<AIModelProviderShape>(allowIntegerValues: false);

/// <summary>
/// Test-only string enum converter that accepts named values but rejects numeric tokens.
/// </summary>
internal sealed class StrictAIModelInterleavedFieldConverter() : JsonStringEnumConverter<AIModelInterleavedField>(allowIntegerValues: false);

/// <summary>
/// Strict test-only counterpart to the product's
/// <see cref="Converters.InterleavedBooleanOrObjectConverter"/>. The product converter
/// silently degrades a malformed object such as <c>{"field":123}</c> to the bare
/// boolean form (<c>Field = null</c>); this converter throws instead, preserving only
/// the valid boolean forms and the recognized field strings.
/// </summary>
internal sealed class StrictInterleavedBooleanOrObjectConverter : JsonConverter<AIModelInterleaved?>
{
    public override AIModelInterleaved? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True)
        {
            return new AIModelInterleaved { Field = null };
        }

        if (reader.TokenType == JsonTokenType.False || reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            string? fieldName = null;
            bool hasField = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString() ?? string.Empty;
                    reader.Read();
                    if (string.Equals(propertyName, "field", StringComparison.Ordinal))
                    {
                        hasField = true;
                        if (reader.TokenType != JsonTokenType.String)
                        {
                            throw new JsonException(
                                $"Unsupported interleaved field value of kind {reader.TokenType}; expected a string \"reasoning_content\" or \"reasoning_details\".");
                        }

                        fieldName = reader.GetString();
                    }
                    else
                    {
                        throw new JsonException(
                            $"Unsupported interleaved member \"{propertyName}\"; expected only \"field\".");
                    }
                }
            }

            if (!hasField)
            {
                return new AIModelInterleaved { Field = null };
            }

            return fieldName switch
            {
                "reasoning_content" => new AIModelInterleaved { Field = AIModelInterleavedField.ReasoningContent },
                "reasoning_details" => new AIModelInterleaved { Field = AIModelInterleavedField.ReasoningDetails },
                _ => throw new JsonException(
                    $"Unsupported interleaved field value \"{fieldName}\"; expected \"reasoning_content\" or \"reasoning_details\"."),
            };
        }

        throw new JsonException(
            $"Unexpected token type {reader.TokenType}. Expected boolean, null, or object for AIModelInterleaved?.");
    }

    public override void Write(Utf8JsonWriter writer, AIModelInterleaved? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteBooleanValue(false);
            return;
        }

        if (value.Field is null)
        {
            writer.WriteBooleanValue(true);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("field");
        writer.WriteStringValue(value.Field.Value switch
        {
            AIModelInterleavedField.ReasoningContent => "reasoning_content",
            AIModelInterleavedField.ReasoningDetails => "reasoning_details",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        });
        writer.WriteEndObject();
    }
}

/// <summary>
/// The strict, test-only counterpart to the product's contexts. Every registered type
/// deserializes with <see cref="JsonUnmappedMemberHandling.Disallow"/>, so a payload key with no
/// mapped property throws instead of silently disappearing, and the four wire enums are read
/// through the generic string enum converters with integer values disabled (never the
/// non-generic converter, which is not native-AOT-safe). The product's flattening converters are deliberately absent: they
/// hardcode the product's contexts, so <see cref="SchemaWalk"/> peels the dynamic maps
/// manually instead. The tests project is never AOT-published, which is what makes a
/// test-only context acceptable here.
/// </summary>
/// <remarks>
/// The [JsonSerializable] registrations below are the single expected-shape source shared by
/// both drift mechanisms in this file: the generator emits one <c>JsonTypeInfo<T></c>
/// property per type it binds, and <see cref="SchemaWalk.MappedTypes"/> is derived from those
/// properties, so adding or removing a mapped type here moves the strict context and the
/// reflection walk together - they cannot drift apart.
/// </remarks>
[JsonSerializable(typeof(AICatalog))]
[JsonSerializable(typeof(AIProviderInfo))]
[JsonSerializable(typeof(AIModelInfo))]
[JsonSerializable(typeof(AIModelMetadata))]
[JsonSerializable(typeof(AIModelCostInfo))]
[JsonSerializable(typeof(AIModelCostTier))]
[JsonSerializable(typeof(AIModelTierInfo))]
[JsonSerializable(typeof(AIModelModalities))]
[JsonSerializable(typeof(AIModelLimit))]
[JsonSerializable(typeof(AIModelReasoningOption))]
[JsonSerializable(typeof(AIModelInterleaved))]
[JsonSerializable(typeof(AIModelExperimental))]
[JsonSerializable(typeof(AIModelExperimentalMode))]
[JsonSerializable(typeof(AIModelModeProvider))]
[JsonSerializable(typeof(AIModelProviderOverride))]
[JsonSerializable(typeof(AIModelWeightInfo))]
[JsonSerializable(typeof(AIModelBenchmark))]
[JsonSerializable(typeof(AIModelLinkInfo))]
[JsonSerializable(typeof(AIModelStatus))]
[JsonSerializable(typeof(AIModelReasoningOptionType))]
[JsonSerializable(typeof(AIModelProviderShape))]
[JsonSerializable(typeof(AIModelInterleavedField))]
[JsonSourceGenerationOptions(
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    Converters =
    [
        typeof(FlexibleDateOnlyConverter),
        typeof(StrictInterleavedBooleanOrObjectConverter),
        typeof(StrictAIModelStatusConverter),
        typeof(StrictAIModelReasoningOptionTypeConverter),
        typeof(StrictAIModelProviderShapeConverter),
        typeof(StrictAIModelInterleavedFieldConverter),
    ])]
public partial class SchemaWalkJsonContext : JsonSerializerContext
{
}

/// <summary>The two directions payload drift can take.</summary>
public enum SchemaDriftKind
{
    /// <summary>A payload JSON key that no mapped property binds.</summary>
    UnmappedKey,

    /// <summary>A mapped property whose expected JSON key is absent from the payload.</summary>
    StaleProperty,
}

/// <summary>
/// One drift finding. <see cref="Path"/> is the full JSON path of the offending (or missing)
/// member, so it already ends with <see cref="Key"/>; <see cref="Detail"/> carries extra
/// context when the key is swallowed by extension data rather than being wholly absent.
/// </summary>
public sealed record SchemaDriftFinding(SchemaDriftKind Kind, string Path, string Key, string? Detail = null, Type? DeclaringType = null)
{
    public override string ToString() =>
        Detail is null ? $"{Kind} at {Path}" : $"{Kind} at {Path} ({Detail})";
}

/// <summary>The outcome of one walk: every unmapped key and every stale property found.</summary>
public sealed class SchemaWalkReport
{
    internal SchemaWalkReport(IReadOnlyList<SchemaDriftFinding> findings) => Findings = findings;

    public IReadOnlyList<SchemaDriftFinding> Findings { get; }

    public bool HasDrift => Findings.Count > 0;

    public override string ToString() => Findings.Count == 0
        ? "No drift detected."
        : string.Join(Environment.NewLine, Findings.Select(finding => finding.ToString()));
}

/// <summary>
/// One deliberately unmapped payload key. <see cref="Reason"/> is mandatory: it is the written
/// justification for why a key the upstream payload sends is intentionally not mapped.
/// </summary>
public sealed record SchemaWalkAllowlistEntry(string Key, string Reason);

/// <summary>
/// The allowlist for deliberately unmapped payload keys. It is empty today because stale
/// mappings were deleted rather than allowlisted; the mechanism exists so a future
/// deliberately-skipped key can be silenced without weakening the walk. Every entry added here
/// must carry a written reason, which the record signature and the suite both enforce.
/// </summary>
public static class SchemaWalkAllowlist
{
    public static IReadOnlyList<SchemaWalkAllowlistEntry> Entries { get; } = [];
}

/// <summary>
/// Two-directional payload drift detection: a reflection walk that compares every mapped
/// object's expected JSON keys (derived from <c>[JsonPropertyName]</c>) against the keys a
/// payload actually carries, reporting unmapped keys and stale properties with their JSON
/// paths. It pairs with <see cref="SchemaWalkJsonContext"/>, whose
/// <c>JsonUnmappedMemberHandling.Disallow</c> supplies throw-safety for values; both mechanisms
/// take their type list from that context's registrations, so they stay in sync.
/// </summary>
/// <remarks>
/// The dynamic maps - the provider id map at the <c>/api.json</c> root, the model id map under
/// each provider's <c>models</c> (and the model id map at the <c>/models.json</c> root), and
/// the <c>experimental.modes</c> map - are walked manually here. The product's flattening
/// converters are never used: they deserialize through the product's contexts, and the wire
/// shape of those maps is owned by this walk instead.
/// </remarks>
public static class SchemaWalk
{
    /// <summary>
    /// The mapped object types to walk: every wire-keyed type bound by
    /// <see cref="SchemaWalkJsonContext"/>. Derived from the context's own generated type
    /// metadata (which the [JsonSerializable] registrations produce), which makes the strict
    /// context the single expected-shape source feeding both mechanisms.
    /// </summary>
    public static IReadOnlyList<Type> MappedTypes { get; } = DiscoverMappedTypes();

    private static readonly HashSet<Type> s_mappedTypes = [.. MappedTypes];

    private static readonly ConcurrentDictionary<Type, IReadOnlySet<string>> s_expectedKeys = new();

    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, PropertyInfo>> s_propertiesByWireKey = new();

    private static readonly ConcurrentDictionary<Type, bool> s_hasExtensionData = new();

    // Wire shapes that a converter owns instead of the [JsonPropertyName] attributes. Each
    // entry exists because the registered converter, not the attribute, decides which keys the
    // payload carries for that type.
    private static readonly IReadOnlyDictionary<Type, WalkShape> s_converterShapes = new Dictionary<Type, WalkShape>
    {
        // InterleavedBooleanOrObjectConverter reads the "field" member of the interleaved
        // object (or a bare boolean), so the [JsonPropertyName("interleaved")] on
        // AIModelInterleaved.Field is never consulted on the wire; expect "field".
        [typeof(AIModelInterleaved)] = new WalkShape(WireKeys: new HashSet<string> { "field" }, DynamicKeyPrefix: null),

        // AIModelCostInfo carries [JsonExtensionData]: keys prefixed "context_over_" become
        // typed context overrides after post-processing (and stay cost-shaped), while any
        // other extra key would be silently swallowed instead of throwing.
        [typeof(AIModelCostInfo)] = new WalkShape(
            WireKeys: null,
            DynamicKeyPrefix: "context_over_",
            DynamicValueType: typeof(AIModelCostInfo)),
    };

    /// <summary>
    /// The expected JSON key set of <paramref name="type"/>, derived from its
    /// <c>[JsonPropertyName]</c> attributes via reflection, with converter-owned wire shapes
    /// (see <c>s_converterShapes</c>) taking precedence where a registered converter reads
    /// different keys than the attribute declares.
    /// </summary>
    public static IReadOnlySet<string> ExpectedKeys(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return s_expectedKeys.GetOrAdd(type, static candidate =>
        {
            if (s_converterShapes.TryGetValue(candidate, out WalkShape? shape) && shape.WireKeys is not null)
            {
                return shape.WireKeys;
            }

            HashSet<string> keys = [];
            foreach (PropertyInfo property in candidate.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<JsonPropertyNameAttribute>() is { Name: string name })
                {
                    keys.Add(name);
                }
            }

            return keys;
        });
    }

    /// <summary>
    /// Walks one JSON object against <paramref name="type"/>'s expected shape, reporting every
    /// unmapped key and every stale property with its JSON path below
    /// <paramref name="path"/>. Keys on <see cref="SchemaWalkAllowlist.Entries"/> (or on a
    /// caller-supplied allowlist) are silenced; each silencing entry carries a written reason.
    /// </summary>
    /// <param name="element">A JSON object to walk.</param>
    /// <param name="type">A type bound by <see cref="SchemaWalkJsonContext"/>.</param>
    /// <param name="path">The JSON path <paramref name="element"/> sits at, defaulting to the root.</param>
    /// <param name="allowlist">
    /// Allowlist to consult; when null, <see cref="SchemaWalkAllowlist.Entries"/> applies.
    /// </param>
    public static SchemaWalkReport Walk(
        JsonElement element,
        Type type,
        string path = "$",
        IReadOnlyList<SchemaWalkAllowlistEntry>? allowlist = null)
    {
        IReadOnlyList<SchemaWalkAllowlistEntry> effectiveAllowlist = allowlist ?? SchemaWalkAllowlist.Entries;

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"Expected a JSON object to walk for '{type?.Name}'.", nameof(element));
        }

        if (type is null || !s_mappedTypes.Contains(type))
        {
            throw new ArgumentException(
                $"'{type}' is not bound by {nameof(SchemaWalkJsonContext)}, so it has no expected shape to walk against.",
                nameof(type));
        }

        List<SchemaDriftFinding> findings = [];
        WalkObject(element, type, path, idFromMapKey: false, findings, effectiveAllowlist);
        return new SchemaWalkReport(findings);
    }

    /// <summary>
    /// Walks a full <c>/api.json</c> document. The root is the dynamic provider id map: each
    /// entry is walked as an <see cref="AIProviderInfo"/> whose id comes from its map key
    /// (the product's flattening converter does the same peeling; this walk does it manually).
    /// </summary>
    public static SchemaWalkReport WalkApiJson(
        JsonElement root,
        IReadOnlyList<SchemaWalkAllowlistEntry>? allowlist = null) =>
        WalkDynamicMap(root, typeof(AIProviderInfo), "provider id map", allowlist);

    /// <summary>
    /// Walks a full <c>/models.json</c> document. The root is the dynamic model id map: each
    /// entry is walked as an <see cref="AIModelMetadata"/> whose id comes from its map key.
    /// </summary>
    public static SchemaWalkReport WalkModelsJson(
        JsonElement root,
        IReadOnlyList<SchemaWalkAllowlistEntry>? allowlist = null) =>
        WalkDynamicMap(root, typeof(AIModelMetadata), "model id map", allowlist);

    /// <summary>
    /// Walks a full <c>/catalog.json</c> document as an <see cref="AICatalog"/>, descending
    /// manually through its <c>providers</c> and <c>models</c> id maps.
    /// </summary>
    public static SchemaWalkReport WalkCatalogJson(
        JsonElement root,
        IReadOnlyList<SchemaWalkAllowlistEntry>? allowlist = null) =>
        Walk(root, typeof(AICatalog), path: "$", allowlist);

    private static IReadOnlyList<Type> DiscoverMappedTypes() =>
        [.. typeof(SchemaWalkJsonContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.PropertyType)
            .Where(propertyType => propertyType.IsGenericType
                && propertyType.GetGenericTypeDefinition() == typeof(JsonTypeInfo<>))
            .Select(propertyType => propertyType.GetGenericArguments()[0])
            .Where(IsMappedShape)
            .Distinct()];

    private static bool IsMappedShape(Type type) =>
        !type.IsEnum
        && !type.IsArray
        && !type.IsPrimitive
        && type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(property => property.GetCustomAttribute<JsonPropertyNameAttribute>() is not null);

    private static SchemaWalkReport WalkDynamicMap(
        JsonElement root,
        Type valueType,
        string mapName,
        IReadOnlyList<SchemaWalkAllowlistEntry>? allowlist)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"The {mapName} must be a JSON object keyed by id.", nameof(root));
        }

        IReadOnlyList<SchemaWalkAllowlistEntry> effectiveAllowlist = allowlist ?? SchemaWalkAllowlist.Entries;
        List<SchemaDriftFinding> findings = [];
        foreach (JsonProperty entry in root.EnumerateObject())
        {
            WalkValue(entry.Value, valueType, Member("$", entry.Name), idFromMapKey: true, findings, effectiveAllowlist);
        }

        return new SchemaWalkReport(findings);
    }

    private static void WalkObject(
        JsonElement element,
        Type type,
        string path,
        bool idFromMapKey,
        List<SchemaDriftFinding> findings,
        IReadOnlyList<SchemaWalkAllowlistEntry> allowlist)
    {
        if (type == typeof(AIModelInterleaved))
        {
            ValidateInterleavedObject(element, path, findings);
        }

        IReadOnlySet<string> expectedKeys = ExpectedKeys(type);
        IReadOnlyDictionary<string, PropertyInfo> properties = PropertiesByWireKey(type);
        s_converterShapes.TryGetValue(type, out WalkShape? shape);
        bool extensionData = HasExtensionData(type);

        HashSet<string> presentKeys = new(StringComparer.Ordinal);
        foreach (JsonProperty member in element.EnumerateObject())
        {
            if (expectedKeys.Contains(member.Name))
            {
                presentKeys.Add(member.Name);
                if (properties.TryGetValue(member.Name, out PropertyInfo? property))
                {
                    WalkValue(member.Value, property.PropertyType, Member(path, member.Name),
                        idFromMapKey: false, findings, allowlist);
                }

                continue;
            }

            if (allowlist.Any(entry => string.Equals(entry.Key, member.Name, StringComparison.Ordinal)))
            {
                // Deliberately unmapped: the entry's written reason documents the skip.
                continue;
            }

            if (shape?.DynamicKeyPrefix is string prefix && member.Name.StartsWith(prefix, StringComparison.Ordinal))
            {
                // A dynamic extension-data key. When its value has a known shape (for
                // example, context_over_* holds a cost object), walk it so drift inside the
                // dynamic key is caught too. Null is allowed; anything non-object cannot be a
                // cost override and must not pass validation.
                if (shape.DynamicValueType is { } dynamicValueType)
                {
                    if (member.Value.ValueKind == JsonValueKind.Null)
                    {
                        continue;
                    }

                    if (member.Value.ValueKind != JsonValueKind.Object)
                    {
                        findings.Add(new SchemaDriftFinding(
                            SchemaDriftKind.UnmappedKey,
                            Member(path, member.Name),
                            member.Name,
                            $"unsupported {prefix}* override value of kind {member.Value.ValueKind}; expected a cost object or null"));
                        continue;
                    }

                    WalkValue(member.Value, dynamicValueType, Member(path, member.Name),
                        idFromMapKey: false, findings, allowlist);
                }

                continue;
            }

            findings.Add(new SchemaDriftFinding(
                SchemaDriftKind.UnmappedKey,
                Member(path, member.Name),
                member.Name,
                extensionData
                    ? "absorbed by the type's [JsonExtensionData] property instead of a mapped property"
                    : null));
        }

        foreach (string key in expectedKeys)
        {
            if (presentKeys.Contains(key))
            {
                continue;
            }

            if (idFromMapKey && string.Equals(key, "id", StringComparison.Ordinal))
            {
                // Inside a dynamic id map the id comes from the map key, which is not a
                // JSON member of the value object - its absence is not staleness.
                continue;
            }

            findings.Add(new SchemaDriftFinding(SchemaDriftKind.StaleProperty, Member(path, key), key, DeclaringType: type));
        }
    }

    private static void WalkValue(
        JsonElement value,
        Type declaredType,
        string path,
        bool idFromMapKey,
        List<SchemaDriftFinding> findings,
        IReadOnlyList<SchemaWalkAllowlistEntry> allowlist)
    {
        Type type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // A dictionary whose value type is mapped is a dynamic map (the experimental
            // mode map, the catalog's id maps): its keys are data, not drift. Free-form
            // dictionaries (headers, bodies, context sizes) carry no expected keys.
            Type valueType = type.GetGenericArguments()[1];
            if (!s_mappedTypes.Contains(valueType))
            {
                return;
            }

            foreach (JsonProperty member in value.EnumerateObject())
            {
                WalkValue(member.Value, valueType, Member(path, member.Name), idFromMapKey: true, findings, allowlist);
            }

            return;
        }

        Type? elementType = EnumerableElementType(type);
        if (elementType is not null)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                // A declared array arriving as an object is a dynamic id map (the model id
                // map under a provider's "models"): walk it manually instead of flattening.
                if (s_mappedTypes.Contains(elementType))
                {
                    foreach (JsonProperty member in value.EnumerateObject())
                    {
                        WalkValue(member.Value, elementType, Member(path, member.Name),
                            idFromMapKey: true, findings, allowlist);
                    }
                }

                return;
            }

            if (value.ValueKind == JsonValueKind.Array && s_mappedTypes.Contains(elementType))
            {
                int index = 0;
                foreach (JsonElement item in value.EnumerateArray())
                {
                    WalkValue(item, elementType,
                        path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]",
                        idFromMapKey: false, findings, allowlist);
                    index++;
                }
            }

            return;
        }

        if (value.ValueKind == JsonValueKind.Object && s_mappedTypes.Contains(type))
        {
            WalkObject(value, type, path, idFromMapKey, findings, allowlist);
            return;
        }

        if (type == typeof(AIModelInterleaved))
        {
            // The product converter accepts bare booleans/null; anything else must be an object.
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)
            {
                return;
            }

            findings.Add(new SchemaDriftFinding(
                SchemaDriftKind.UnmappedKey,
                path,
                "interleaved",
                $"unsupported interleaved value of kind {value.ValueKind}; expected a boolean or an object with a string \"field\""));
            return;
        }

        // Scalars, booleans standing in for AIModelInterleaved, and free-form objects are
        // leaves: they have no keys of their own to check.
    }

    private static void ValidateInterleavedObject(JsonElement value, string path, List<SchemaDriftFinding> findings)
    {
        if (!value.TryGetProperty("field", out JsonElement field))
        {
            return;
        }

        bool valid = field.ValueKind == JsonValueKind.String
            && field.GetString() is string name
            && (string.Equals(name, "reasoning_content", StringComparison.Ordinal)
                || string.Equals(name, "reasoning_details", StringComparison.Ordinal));

        if (!valid)
        {
            findings.Add(new SchemaDriftFinding(
                SchemaDriftKind.UnmappedKey,
                Member(path, "field"),
                "field",
                "unsupported interleaved field value; expected \"reasoning_content\" or \"reasoning_details\""));
        }
    }

    /// <summary>
    /// Strict cost extension-data validation. <see cref="AIModelCostInfo"/> carries
    /// <c>[JsonExtensionData]</c>, which captures unknown members even when the strict
    /// context sets <see cref="JsonUnmappedMemberHandling.Disallow"/> - so strict
    /// deserialization alone accepts <c>cost.mystery_key</c> and unchecked
    /// <c>cost.context_over_*.input</c> values. This validator rejects unknown keys and
    /// recursively strict-deserializes each non-null <c>context_over_*</c> value so valid
    /// overrides remain supported.
    /// </summary>
    public static IReadOnlyList<SchemaDriftFinding> ValidateCostTree(JsonElement costElement, string path)
    {
        List<SchemaDriftFinding> findings = [];
        ValidateCostTreeRecursive(costElement, path, findings);
        return findings;
    }

    /// <summary>
    /// Validates every cost object reachable from one raw model object: its base
    /// <c>cost</c> and each <c>experimental.modes.*.cost</c>.
    /// </summary>
    public static IReadOnlyList<SchemaDriftFinding> ValidateModelCosts(JsonElement rawModel, string path)
    {
        List<SchemaDriftFinding> findings = [];
        if (rawModel.ValueKind != JsonValueKind.Object)
        {
            return findings;
        }

        if (rawModel.TryGetProperty("cost", out JsonElement cost)
            && cost.ValueKind == JsonValueKind.Object)
        {
            ValidateCostTreeRecursive(cost, Member(path, "cost"), findings);
        }

        if (rawModel.TryGetProperty("experimental", out JsonElement experimental)
            && experimental.ValueKind == JsonValueKind.Object
            && experimental.TryGetProperty("modes", out JsonElement modes)
            && modes.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty mode in modes.EnumerateObject())
            {
                if (mode.Value.ValueKind == JsonValueKind.Object
                    && mode.Value.TryGetProperty("cost", out JsonElement modeCost)
                    && modeCost.ValueKind == JsonValueKind.Object)
                {
                    string modePath = Member(Member(Member(path, "experimental"), "modes"), mode.Name);
                    ValidateCostTreeRecursive(modeCost, Member(modePath, "cost"), findings);
                }
            }
        }

        return findings;
    }

    private static void ValidateCostTreeRecursive(
        JsonElement costElement,
        string path,
        List<SchemaDriftFinding> findings)
    {
        if (costElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        IReadOnlySet<string> expectedKeys = ExpectedKeys(typeof(AIModelCostInfo));
        foreach (JsonProperty member in costElement.EnumerateObject())
        {
            if (expectedKeys.Contains(member.Name))
            {
                continue;
            }

            if (member.Name.StartsWith("context_over_", StringComparison.Ordinal))
            {
                if (member.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    continue;
                }

                if (member.Value.ValueKind != JsonValueKind.Object)
                {
                    findings.Add(new SchemaDriftFinding(
                        SchemaDriftKind.UnmappedKey,
                        Member(path, member.Name),
                        member.Name,
                        $"unsupported context_over_* override value of kind {member.Value.ValueKind}; expected a cost object or null"));
                    continue;
                }

                try
                {
                    JsonSerializer.Deserialize(
                        member.Value.GetRawText(),
                        SchemaWalkJsonContext.Default.AIModelCostInfo);
                }
                catch (JsonException exception)
                {
                    findings.Add(new SchemaDriftFinding(
                        SchemaDriftKind.UnmappedKey,
                        Member(path, member.Name),
                        member.Name,
                        $"context_over_* override failed strict deserialization: {exception.Message}"));
                    continue;
                }

                ValidateCostTreeRecursive(member.Value, Member(path, member.Name), findings);
                continue;
            }

            findings.Add(new SchemaDriftFinding(
                SchemaDriftKind.UnmappedKey,
                Member(path, member.Name),
                member.Name,
                "absorbed by the type's [JsonExtensionData] property instead of a mapped property"));
        }
    }

    private static IReadOnlyDictionary<string, PropertyInfo> PropertiesByWireKey(Type type) =>
        s_propertiesByWireKey.GetOrAdd(type, static candidate =>
        {
            Dictionary<string, PropertyInfo> map = new(StringComparer.Ordinal);
            foreach (PropertyInfo property in candidate.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<JsonPropertyNameAttribute>() is { Name: string name })
                {
                    map[name] = property;
                }
            }

            return map;
        });

    private static bool HasExtensionData(Type type) => s_hasExtensionData.GetOrAdd(type, static candidate =>
        candidate.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(property => property.GetCustomAttribute<JsonExtensionDataAttribute>() is not null));

    private static Type? EnumerableElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type == typeof(string) || type == typeof(JsonElement))
        {
            return null;
        }

        return typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType
            ? type.GetGenericArguments()[0]
            : null;
    }

    private static string Member(string path, string name)
    {
        if (IsSimpleName(name))
        {
            return path + "." + name;
        }

        string escaped = name
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
        return path + "['" + escaped + "']";
    }

    private static bool IsSimpleName(string name)
    {
        if (name.Length == 0 || (!char.IsLetter(name[0]) && name[0] != '_'))
        {
            return false;
        }

        foreach (char character in name)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private sealed record WalkShape(IReadOnlySet<string>? WireKeys, string? DynamicKeyPrefix, Type? DynamicValueType = null);
}

/// <summary>
/// Verifies the drift-detection machinery itself: the strict context's registration and
/// converters, the reflection-derived expected keys, the walk's JSON paths in both drift
/// directions, dynamic-map peeling, and the allowlist mechanism.
/// </summary>
public class SchemaWalkTests
{
    [Test]
    public async Task MappedTypes_AreDerivedFromTheStrictContextRegistration()
    {
        await Assert.That(SchemaWalk.MappedTypes.Any(type => type == typeof(AIModelInfo))).IsTrue();
        await Assert.That(SchemaWalk.MappedTypes.Any(type => type == typeof(AIModelMetadata))).IsTrue();
        await Assert.That(SchemaWalk.MappedTypes.Any(type => type == typeof(AICatalog))).IsTrue();

        // Enums, arrays and primitives are leaves or containers - never walk roots - even if
        // they are registered so the strict context can bind them.
        await Assert.That(SchemaWalk.MappedTypes.Any(type => type.IsEnum || type.IsArray || type.IsPrimitive)).IsFalse();

        IJsonTypeInfoResolver resolver = SchemaWalkJsonContext.Default;
        foreach (Type type in SchemaWalk.MappedTypes)
        {
            await Assert.That(resolver.GetTypeInfo(type, SchemaWalkJsonContext.Default.Options)).IsNotNull();
        }
    }

    [Test]
    public async Task ExpectedKeys_ComeFromJsonPropertyNameAttributes()
    {
        IReadOnlySet<string> limitKeys = SchemaWalk.ExpectedKeys(typeof(AIModelLimit));
        await Assert.That(limitKeys.SetEquals(["context", "input", "output"])).IsTrue();

        IReadOnlySet<string> providerKeys = SchemaWalk.ExpectedKeys(typeof(AIProviderInfo));
        await Assert.That(providerKeys.SetEquals(["id", "npm", "name", "doc", "api", "env", "models"])).IsTrue();

        // The interleaved wire shape is converter-owned: the payload carries "field", not
        // the attribute's "interleaved".
        IReadOnlySet<string> interleavedKeys = SchemaWalk.ExpectedKeys(typeof(AIModelInterleaved));
        await Assert.That(interleavedKeys.SetEquals(["field"])).IsTrue();
    }

    [Test]
    public async Task StrictContext_RegistersTheNamedConverters()
    {
        JsonConverter[] converters = [.. SchemaWalkJsonContext.Default.Options.Converters];

        await Assert.That(converters.Any(converter => converter is FlexibleDateOnlyConverter)).IsTrue();
        await Assert.That(converters.Any(converter => converter is StrictInterleavedBooleanOrObjectConverter)).IsTrue();
        await Assert.That(converters.Any(converter => converter is StrictAIModelStatusConverter)).IsTrue();
        await Assert.That(converters.Any(converter => converter is JsonStringEnumConverter<AIModelReasoningOptionType>)).IsTrue();
        await Assert.That(converters.Any(converter => converter is JsonStringEnumConverter<AIModelProviderShape>)).IsTrue();
        await Assert.That(converters.Any(converter => converter is JsonStringEnumConverter<AIModelInterleavedField>)).IsTrue();
    }

    [Test]
    public async Task StrictInterleavedConverter_RejectsMalformedFieldValues_PreservesValidForms()
    {
        // Malformed: non-string field values must throw instead of degrading to Field = null.
        string[] malformed = ["{\"field\":123}", "{\"field\":true}", "{\"field\":null}", "{\"field\":\"bogus\"}", "{\"rogue\":true}"];
        foreach (string body in malformed)
        {
            bool threw = false;
            try
            {
                JsonSerializer.Deserialize(body, SchemaWalkJsonContext.Default.AIModelInterleaved);
            }
            catch (JsonException)
            {
                threw = true;
            }

            await Assert.That(threw).IsTrue();
        }

        // Valid forms still bind: bare booleans/null and the recognized field strings.
        AIModelInterleaved? bare = JsonSerializer.Deserialize("true", SchemaWalkJsonContext.Default.AIModelInterleaved);
        await Assert.That(bare).IsNotNull();
        await Assert.That(bare!.Field).IsNull();

        await Assert.That(JsonSerializer.Deserialize("false", SchemaWalkJsonContext.Default.AIModelInterleaved)).IsNull();
        await Assert.That(JsonSerializer.Deserialize("null", SchemaWalkJsonContext.Default.AIModelInterleaved)).IsNull();

        AIModelInterleaved? content = JsonSerializer.Deserialize(
            """{"field":"reasoning_content"}""", SchemaWalkJsonContext.Default.AIModelInterleaved);
        await Assert.That(content!.Field).IsEqualTo(AIModelInterleavedField.ReasoningContent);

        AIModelInterleaved? details = JsonSerializer.Deserialize(
            """{"field":"reasoning_details"}""", SchemaWalkJsonContext.Default.AIModelInterleaved);
        await Assert.That(details!.Field).IsEqualTo(AIModelInterleavedField.ReasoningDetails);
    }

    [Test]
    public async Task StrictAIModelStatusConverter_RejectsCommaSeparatedNames()
    {
        AIModelStatus beta = JsonSerializer.Deserialize(
            "\"beta\"", SchemaWalkJsonContext.Default.AIModelStatus);
        await Assert.That(beta).IsEqualTo(AIModelStatus.Beta);

        bool threw = false;
        try
        {
            JsonSerializer.Deserialize(
                "\"alpha, beta\"", SchemaWalkJsonContext.Default.AIModelStatus);
        }
        catch (JsonException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task StrictContext_RejectsPayloadKeysWithNoMappedProperty()
    {
        const string body = """{"name":"GPT One","definitely_unmapped":true}""";

        bool threw = false;
        try
        {
            JsonSerializer.Deserialize(body, SchemaWalkJsonContext.Default.AIModelInfo);
        }
        catch (JsonException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task ValidateCostTree_RejectsExtensionDataSwallowedKeys_AndBadOverrideValues()
    {
        // [JsonExtensionData] captures unknown cost members even with Disallow, so the
        // strict deserializer alone accepts both fragments below. The explicit cost
        // validator must reject them.
        const string mysteryBody = """{"input":1.0,"mystery_key":9}""";
        using (JsonDocument mystery = JsonDocument.Parse(mysteryBody))
        {
            AIModelCostInfo? strict = JsonSerializer.Deserialize(
                mysteryBody, SchemaWalkJsonContext.Default.AIModelCostInfo);
            await Assert.That(strict).IsNotNull();

            IReadOnlyList<SchemaDriftFinding> findings =
                SchemaWalk.ValidateCostTree(mystery.RootElement, "$.cost");
            await Assert.That(findings.Any(finding => finding.Key == "mystery_key")).IsTrue();
        }

        const string badOverrideBody = """{"input":1.0,"context_over_200k":{"input":"cheap"}}""";
        using (JsonDocument badOverride = JsonDocument.Parse(badOverrideBody))
        {
            AIModelCostInfo? strict = JsonSerializer.Deserialize(
                badOverrideBody, SchemaWalkJsonContext.Default.AIModelCostInfo);
            await Assert.That(strict).IsNotNull();

            IReadOnlyList<SchemaDriftFinding> findings =
                SchemaWalk.ValidateCostTree(badOverride.RootElement, "$.cost");
            await Assert.That(findings.Any(finding => finding.Path.Contains("context_over_200k", StringComparison.Ordinal))).IsTrue();
        }

        // A valid override (numeric fields, null allowed) stays supported.
        const string validBody = """{"input":1.0,"context_over_200k":{"input":0.4,"output":1.8}}""";
        using (JsonDocument valid = JsonDocument.Parse(validBody))
        {
            IReadOnlyList<SchemaDriftFinding> findings =
                SchemaWalk.ValidateCostTree(valid.RootElement, "$.cost");
            await Assert.That(findings.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task StrictContext_DeserializesASampleFragmentOfEachEndpointShape()
    {
        // /api.json model value: enums, flexible dates, interleaved, provider override,
        // nested cost with tiers and a context_over_* extension key, experimental modes.
        const string modelBody = """
            {"name":"GPT One","type":"chat","canonical_model_id":"openai/gpt-1","status":"beta",
             "knowledge":"2024-05","release_date":"2024-01-15","last_updated":"2024-06-01",
             "attachment":true,"temperature":true,"reasoning":true,"structured_output":true,
             "tool_call":true,"open_weights":false,
             "modalities":{"input":["text"],"output":["text"]},
             "limit":{"context":400000,"input":400000,"output":128000},
             "cost":{"input":1.25,"output":10,"reasoning":4.4,"cache_read":0.5,"cache_write":2,
                     "tiers":[{"input":0.4,"output":1.8,"cache_read":0.04,"cache_write":0.6,
                               "reasoning":0.8,"tier":{"type":"context","size":272000}}],
                     "context_over_200k":{"input":0.4,"output":1.8}},
             "reasoning_options":[{"type":"effort","min":1,"max":3,"values":["low","medium","high"]}],
             "interleaved":{"field":"reasoning_content"},
             "provider":{"shape":"responses","npm":"openai","api":"https://api.openai.com"},
             "experimental":{"modes":{"fast":{"cost":{"input":0.2625,"output":0.875,"cache_read":0.0525},
                                          "provider":{"body":{"service_tier":"priority"},
                                                      "headers":{"x-mode":"fast"}}}}}}
            """;

        // /api.json provider value, with the model id map peeled off (the walk owns that map).
        const string providerBody = """
            {"id":"openai","name":"OpenAI","npm":"openai","env":["OPENAI_API_KEY"],
             "doc":"https://docs.openai.com","api":"https://api.openai.com"}
            """;

        // /models.json model metadata value.
        const string metadataBody = """
            {"name":"GPT One","description":"A helpful model","type":"chat","license":"Apache-2.0",
             "links":[{"label":"Model docs","url":"https://example.com/docs","type":"docs"}],
             "weights":[{"label":"Base","url":"https://example.com/w","format":"safetensors",
                         "quantization":"fp8"}]}
            """;

        // /catalog.json document.
        const string catalogBody = """
            {"models":{"oai/gpt-1":{"name":"GPT One","description":"A helpful model","license":"Apache-2.0"}},
             "providers":{"openai":{"name":"OpenAI"}}}
            """;

        AIModelInfo? model = JsonSerializer.Deserialize(modelBody, SchemaWalkJsonContext.Default.AIModelInfo);
        await Assert.That(model).IsNotNull();
        await Assert.That(model!.Status).IsEqualTo(AIModelStatus.Beta);
        await Assert.That(model.Knowledge).IsEqualTo(new DateOnly(2024, 5, 1));
        await Assert.That(model.ReasoningOptions![0].Type).IsEqualTo(AIModelReasoningOptionType.Effort);
        await Assert.That(model.Interleaved!.Field).IsEqualTo(AIModelInterleavedField.ReasoningContent);
        await Assert.That(model.ProviderOverride!.Shape).IsEqualTo(AIModelProviderShape.Responses);
        await Assert.That(model.Cost!.ContextOverridesRaw).IsNotNull();
        await Assert.That(model.Cost.ContextOverridesRaw!.ContainsKey("context_over_200k")).IsTrue();

        AIProviderInfo? provider = JsonSerializer.Deserialize(providerBody, SchemaWalkJsonContext.Default.AIProviderInfo);
        await Assert.That(provider).IsNotNull();
        await Assert.That(provider!.Name).IsEqualTo("OpenAI");

        AIModelMetadata? metadata = JsonSerializer.Deserialize(metadataBody, SchemaWalkJsonContext.Default.AIModelMetadata);
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.License).IsEqualTo("Apache-2.0");

        AICatalog? catalog = JsonSerializer.Deserialize(catalogBody, SchemaWalkJsonContext.Default.AICatalog);
        await Assert.That(catalog).IsNotNull();
        await Assert.That(catalog!.Models.ContainsKey("oai/gpt-1")).IsTrue();
        await Assert.That(catalog.Providers.ContainsKey("openai")).IsTrue();
    }

    [Test]
    public async Task Walk_ReportsUnmappedKeysAndStaleProperties_WithJsonPaths()
    {
        const string body = """
            {"openai":{"models":{
              "gpt-1":{"name":"GPT One","brand_new":true,
                       "cost":{"input":1.0,"mystery_key":9,"context_over_200k":{"input":0.4}},
                       "experimental":{"modes":{
                         "fast":{"cost":{"input":0.2625},"rogue":true}}}}}}}
            """;

        using JsonDocument document = JsonDocument.Parse(body);
        SchemaWalkReport report = SchemaWalk.WalkApiJson(document.RootElement);

        // Unmapped keys, each with the JSON path of the key itself - including one inside
        // the manually walked model id map and one inside the experimental mode map.
        await Assert.That(Has(report, SchemaDriftKind.UnmappedKey, "$.openai.models['gpt-1'].brand_new")).IsTrue();
        await Assert.That(Has(report, SchemaDriftKind.UnmappedKey, "$.openai.models['gpt-1'].cost.mystery_key")).IsTrue();
        await Assert.That(Has(report, SchemaDriftKind.UnmappedKey, "$.openai.models['gpt-1'].experimental.modes.fast.rogue")).IsTrue();

        // The cost object's context_over_* key is dynamic extension data, not drift.
        await Assert.That(report.Findings.Any(finding =>
            finding.Kind == SchemaDriftKind.UnmappedKey && finding.Path.Contains("context_over_200k", StringComparison.Ordinal))).IsFalse();

        // Stale mapped properties, including inside the walked maps.
        await Assert.That(Has(report, SchemaDriftKind.StaleProperty, "$.openai.name")).IsTrue();
        await Assert.That(Has(report, SchemaDriftKind.StaleProperty, "$.openai.models['gpt-1'].description")).IsTrue();
        await Assert.That(Has(report, SchemaDriftKind.StaleProperty, "$.openai.models['gpt-1'].experimental.modes.fast.provider")).IsTrue();

        // The id of a map value comes from its map key, so it is never stale there.
        await Assert.That(Has(report, SchemaDriftKind.StaleProperty, "$.openai.id")).IsFalse();
        await Assert.That(Has(report, SchemaDriftKind.StaleProperty, "$.openai.models['gpt-1'].id")).IsFalse();
    }

    [Test]
    public async Task Walk_SilencesOnlyAllowlistedKeys_AndEveryEntryCarriesAReason()
    {
        const string body = """{"name":"GPT One","brand_new":true,"deliberately_unmapped":true}""";
        using JsonDocument document = JsonDocument.Parse(body);

        // With an explicit empty allowlist, every unmapped key is reported - the same
        // hand-built fragment reports the stale direction alongside it.
        SchemaWalkReport withEmptyAllowlist = SchemaWalk.Walk(document.RootElement, typeof(AIModelInfo), allowlist: []);
        await Assert.That(UnmappedKeys(withEmptyAllowlist).Contains("brand_new")).IsTrue();
        await Assert.That(UnmappedKeys(withEmptyAllowlist).Contains("deliberately_unmapped")).IsTrue();
        await Assert.That(Has(withEmptyAllowlist, SchemaDriftKind.StaleProperty, "$.description")).IsTrue();

        // An allowlist entry silences its key; other unmapped keys still report.
        SchemaWalkAllowlistEntry[] allowlist = [new("deliberately_unmapped", "Speculative upstream field the library will map later.")];
        SchemaWalkReport withAllowlist = SchemaWalk.Walk(document.RootElement, typeof(AIModelInfo), allowlist: allowlist);
        await Assert.That(UnmappedKeys(withAllowlist).Contains("brand_new")).IsTrue();
        await Assert.That(UnmappedKeys(withAllowlist).Contains("deliberately_unmapped")).IsFalse();

        // Every allowlist entry must carry a written reason.
        await Assert.That(SchemaWalkAllowlist.Entries.All(entry => !string.IsNullOrWhiteSpace(entry.Reason))).IsTrue();
    }

    private static bool Has(SchemaWalkReport report, SchemaDriftKind kind, string path) =>
        report.Findings.Any(finding => finding.Kind == kind && finding.Path == path);

    private static HashSet<string> UnmappedKeys(SchemaWalkReport report) =>
        [.. report.Findings.Where(finding => finding.Kind == SchemaDriftKind.UnmappedKey).Select(finding => finding.Key)];
}
