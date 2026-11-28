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

namespace ModelsDotDevSharp.Internal;

using System.Text.Json.Serialization.Metadata;

/// <summary>
/// Resolves <c>context_over_*</c> extension-data keys into the typed
/// <see cref="AIModelCostInfo.ContextOverrides"/> tree for every cost reachable from a model.
/// Shared by <c>ModelInfoRepository</c> (<c>/api.json</c>) and <c>CatalogRepository</c>
/// (<c>/catalog.json</c>) so both endpoints honour the same empty-versus-resolved policy.
/// </summary>
internal static class CostProcessing
{
    /// <summary>
    /// Resolves context overrides across a model's base cost and every experimental mode cost.
    /// </summary>
    /// <param name="model">The model whose costs should be processed.</param>
    /// <param name="costTypeInfo">Source-generated type info used to decode nested override costs.</param>
    public static void ProcessModel(AIModelInfo model, JsonTypeInfo<AIModelCostInfo> costTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(costTypeInfo);

        if (model.Cost is not null)
        {
            ProcessAll(model.Cost, costTypeInfo);
        }

        if (model.Experimental?.Modes is null)
        {
            return;
        }

        foreach (AIModelExperimentalMode mode in model.Experimental.Modes.Values)
        {
            if (mode?.Cost is not null)
            {
                ProcessAll(mode.Cost, costTypeInfo);
            }
        }
    }

    /// <summary>
    /// Iterative pre-order DFS over a cost tree, avoiding unbounded recursion /
    /// <see cref="StackOverflowException"/> on deeply nested <see cref="AIModelCostInfo.ContextOverrides"/>.
    /// </summary>
    /// <param name="cost">The root cost to process.</param>
    /// <param name="costTypeInfo">Source-generated type info used to decode nested override costs.</param>
    public static void ProcessAll(AIModelCostInfo cost, JsonTypeInfo<AIModelCostInfo> costTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(costTypeInfo);

        Stack<AIModelCostInfo> stack = new Stack<AIModelCostInfo>();
        stack.Push(cost);

        while (stack.Count > 0)
        {
            AIModelCostInfo current = stack.Pop();
            CostContextOverridePostProcessor.Process(current, costTypeInfo);

            foreach (AIModelCostInfo contextOverride in current.ContextOverrides.Values)
            {
                if (contextOverride is not null)
                {
                    stack.Push(contextOverride);
                }
            }
        }
    }
}
