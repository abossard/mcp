// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Sandbox;

/// <summary>
/// Bounds what a script can push into a model's context. A script that returns more than the budget is
/// truncated and told the size it produced, so the next attempt can filter inside the sandbox instead.
/// </summary>
internal static class HealthModelScriptOutput
{
    private const int CharsPerToken = 4;

    public const int MaxTokens = 6_000;
    public const int MaxChars = MaxTokens * CharsPerToken;
    public const string TruncationMarker = "--- TRUNCATED ---";

    public static bool TryTruncate(string text, out string truncated)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length <= MaxChars)
        {
            truncated = text;
            return false;
        }

        var estimatedTokens = (text.Length + CharsPerToken - 1) / CharsPerToken;
        truncated = string.Concat(
            text.AsSpan(0, MaxChars),
            $"\n\n{TruncationMarker}\nResult was ~{estimatedTokens} tokens (limit: {MaxTokens}). ",
            "Filter or aggregate inside the script and return only what you need.");
        return true;
    }
}
