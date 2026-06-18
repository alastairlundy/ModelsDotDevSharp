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

namespace ModelsDotDevSharp;

public record AIModelMetadata
{
    /// <summary>
    /// The model ID. Note: this is null after deserialization from the 
    /// /models.json endpoint because the flattening converter populates it 
    /// from the dictionary key.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("family")]
    public string Family { get; set; } = string.Empty;

    [JsonPropertyName("attachment")]
    public bool Attachment { get; set; }

    [JsonPropertyName("reasoning")]
    public bool Reasoning { get; set; }

    [JsonPropertyName("tool_call")]
    public bool ToolCall { get; set; }

    [JsonPropertyName("structured_output")]
    public bool StructuredOutput { get; set; }

    [JsonPropertyName("temperature")]
    public bool Temperature { get; set; }

    [JsonPropertyName("knowledge")]
    public DateOnly? Knowledge { get; set; }

    [JsonPropertyName("release_date")]
    public DateOnly? ReleaseDate { get; set; }

    [JsonPropertyName("last_updated")]
    public DateOnly? LastUpdatedDate { get; set; }

    [JsonPropertyName("open_weights")]
    public bool OpenWeights { get; set; }

    [JsonPropertyName("modalities")]
    public AIModelModalities Modalities { get; set; } = null!;

    [JsonPropertyName("limit")]
    public AIModelLimit Limit { get; set; } = null!;

    [JsonPropertyName("weights")]
    public IReadOnlyList<AIModelWeightInfo>? Weights { get; set; }

    [JsonPropertyName("benchmarks")]
    public IReadOnlyList<AIModelBenchmark>? Benchmarks { get; set; }
}
