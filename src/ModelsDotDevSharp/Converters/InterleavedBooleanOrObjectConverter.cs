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
using ModelsDotDevSharp;

public sealed class InterleavedBooleanOrObjectConverter : JsonConverter<AIModelInterleaved?>
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
            // Deserializing the record itself. Since this converter is registered for AIModelInterleaved?, 
            // the JsonSerializer.Deserialize call here might cause recursion if not handled.
            // However, the spec says "object with a field string". 
            // We can handle the object manually to be safe and avoid recursion.
            
            string? fieldName = null;
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
                    if (propertyName == "field")
                    {
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            fieldName = reader.GetString();
                        }
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
            }

            return new AIModelInterleaved 
            { 
                Field = MapField(fieldName) 
            };
        }

        throw new JsonException($"Unexpected token type {reader.TokenType}. Expected boolean, null, or object for AIModelInterleaved?.");
    }

    public override void Write(Utf8JsonWriter writer, AIModelInterleaved? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteBooleanValue(false);
            return;
        }

        if (value.Field == null)
        {
            writer.WriteBooleanValue(true);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("field");
        writer.WriteStringValue(GetFieldString(value.Field!.Value));
        writer.WriteEndObject();
    }

    private static AIModelInterleavedField? MapField(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value switch
        {
            "reasoning_content" => AIModelInterleavedField.ReasoningContent,
            "reasoning_details" => AIModelInterleavedField.ReasoningDetails,
            _ => null
        };
    }

    private static string GetFieldString(AIModelInterleavedField field)
    {
        return field switch
        {
            AIModelInterleavedField.ReasoningContent => "reasoning_content",
            AIModelInterleavedField.ReasoningDetails => "reasoning_details",
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }
}
