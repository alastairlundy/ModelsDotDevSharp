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

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Contexts;

public sealed class ModelsJsonFlatteningConverter : JsonConverter<AIModelMetadata[]>
{
    public override AIModelMetadata[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject token. Got {reader.TokenType}.");
        }

        var models = new List<AIModelMetadata>();

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

            string modelId = reader.GetString() ?? string.Empty;
            reader.Read();

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Expected StartObject for model data. Got {reader.TokenType}.");
            }

            // Use the source-gen context for AOT safety
            AIModelMetadata? metadata = JsonSerializer.Deserialize(ref reader, ModelMetadataJsonContext.Default.AIModelMetadata);
            if (metadata != null)
            {
                metadata.Id = modelId;
                models.Add(metadata);
            }
        }

        return models.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, AIModelMetadata[] value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var model in value)
        {
            writer.WritePropertyName(model.Id ?? string.Empty);
            JsonSerializer.Serialize(writer, model, ModelMetadataJsonContext.Default.AIModelMetadata);
        }
        writer.WriteEndObject();
    }
}
