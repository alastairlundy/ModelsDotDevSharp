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

namespace ModelsDotDevSharp.Converters;

using System.Text.Json;
using System.Text.Json.Serialization;
using Contexts;

/// <summary>
/// Flattens the root <c>/api.json</c> object map (keyed by provider id) into an
/// <see cref="AIProviderInfo"/> array, setting each provider's <c>Id</c> from its map key.
/// </summary>
public sealed class AIProviderInfoArrayFlatteningConverter : JsonConverter<AIProviderInfo[]>
{
    public override AIProviderInfo[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject token. Got {reader.TokenType}.");
        }

        List<AIProviderInfo> providers = [];

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected PropertyName token. Got {reader.TokenType}.");
            }

            string providerId = reader.GetString() ?? string.Empty;
            reader.Read();

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Expected StartObject for provider data. Got {reader.TokenType}.");
            }

            // Use the source-gen context for AOT safety
            AIProviderInfo? provider = JsonSerializer.Deserialize(ref reader, ModelInfoJsonContext.Default.AIProviderInfo);
            if (provider != null)
            {
                provider.Id = providerId;
                providers.Add(provider);
            }
        }

        return [.. providers];
    }

    public override void Write(Utf8JsonWriter writer, AIProviderInfo[] value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (AIProviderInfo provider in value)
        {
            writer.WritePropertyName(provider.Id ?? string.Empty);
            JsonSerializer.Serialize(writer, provider, ModelInfoJsonContext.Default.AIProviderInfo);
        }
        writer.WriteEndObject();
    }
}