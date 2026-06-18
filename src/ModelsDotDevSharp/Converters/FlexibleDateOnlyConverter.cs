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

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class FlexibleDateOnlyConverter : JsonConverter<DateOnly?>
{
    private const string FullFormat = "yyyy-MM-dd";
    private const string ShortFormat = "yyyy-MM";

    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Unexpected token type {reader.TokenType}. Expected string for DateOnly?.");
        }

        string? dateString = reader.GetString();
        if (string.IsNullOrWhiteSpace(dateString))
        {
            return null;
        }

        if (DateOnly.TryParseExact(dateString, FullFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        if (DateOnly.TryParseExact(dateString, ShortFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var shortDate))
        {
            return shortDate;
        }

        throw new JsonException($"Unable to parse date '{dateString}'. Expected format {FullFormat} or {ShortFormat}.");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString(FullFormat, CultureInfo.InvariantCulture));
    }
}
