// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace Azure.Mcp.Tools.Monitor.Sandbox;

/// <summary>
/// Reduces the shapes a model actually emits — fenced blocks, a bare statement body, an arrow function,
/// an <c>export default</c> — to the single invoked-async-function form the engine evaluates.
/// </summary>
internal static partial class HealthModelScriptNormalizer
{
    [GeneratedRegex(@"^\s*```[a-zA-Z]*\s*\n(.*?)\n?\s*```\s*$", RegexOptions.Singleline)]
    private static partial Regex FencedBlock();

    [GeneratedRegex(@"^(async\s+)?function\b", RegexOptions.Singleline)]
    private static partial Regex FunctionDeclaration();

    [GeneratedRegex(@"^(async\s*)?\([^)]*\)\s*=>", RegexOptions.Singleline)]
    private static partial Regex ArrowFunction();

    public static string Normalize(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var source = code.Trim();

        var fenced = FencedBlock().Match(source);
        if (fenced.Success)
        {
            source = fenced.Groups[1].Value.Trim();
        }

        if (source.StartsWith("export default", StringComparison.Ordinal))
        {
            source = source["export default".Length..].Trim();
        }

        if (source.EndsWith(';'))
        {
            var withoutTrailer = source[..^1].TrimEnd();
            if (ArrowFunction().IsMatch(withoutTrailer) || FunctionDeclaration().IsMatch(withoutTrailer))
            {
                source = withoutTrailer;
            }
        }

        if (ArrowFunction().IsMatch(source) || FunctionDeclaration().IsMatch(source))
        {
            return $"({source})()";
        }

        return $"(async () => {{\n{source}\n}})()";
    }
}
