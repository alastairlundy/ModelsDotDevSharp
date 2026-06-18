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

public record AIModelInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("family")]
    public string Family { get; set; } = string.Empty;
    
    [JsonPropertyName("attachment")]
    public bool SupportsFileAttachments { get; set; }
    
    [JsonPropertyName("temperature")]
    public bool SupportsTemperature { get; set; }
    
    [JsonPropertyName("reasoning")]
    public bool SupportsReasoning { get; set; }
    
    [JsonPropertyName("structured_output")]
    public bool SupportsStructuredOutput { get; set; }
    
    [JsonPropertyName("tool_call")]
    public bool SupportsToolCalling { get; set; }
    
    [JsonPropertyName("knowledge")]
    public DateOnly? Knowledge { get; set; }
    
    [JsonPropertyName("release_date")]
    public DateOnly? ReleaseDate { get; set; }
    
    [JsonPropertyName("last_updated")]
    public DateOnly? LastUpdatedDate { get; set; }
    
    [JsonPropertyName("open_weights")]
    public bool IsOpenWeight { get; set; }
    
    [JsonPropertyName("modalities")]
    public AIModelModalities? Modalities { get; set; } = null;
    
    [JsonPropertyName("cost")]
    public AIModelCostInfo? Cost { get; set; } = null;

    [JsonPropertyName("status")]
    public AIModelStatus? Status { get; set; }

    [JsonPropertyName("reasoning_options")]
    public Dictionary<string, AIModelReasoningOption>? ReasoningOptions { get; set; }

    [JsonPropertyName("interleaved")]
    public AIModelInterleaved? Interleaved { get; set; }

    [JsonPropertyName("modes")]
    public Dictionary<string, AIModelCostInfo>? Modes { get; set; }

    [JsonPropertyName("experimental")]
    public AIModelExperimental? Experimental { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    [JsonPropertyName("body")]
    public Dictionary<string, System.Text.Json.JsonElement>? Body { get; set; }

    [JsonPropertyName("provider")]
    public AIModelProviderOverride? ProviderOverride { get; set; }
}