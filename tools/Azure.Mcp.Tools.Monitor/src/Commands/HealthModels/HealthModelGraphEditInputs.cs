// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Commands.HealthModels;

/// <summary>Reads the two guard inputs of a graph edit: the mode, and the caller's declared expectation.</summary>
internal static class HealthModelGraphEditInputs
{
    internal const string WhatIfMode = "whatIf";
    internal const string ApplyMode = "apply";

    internal static bool TryParseMode(string? value, out HealthModelChangeMode mode, out string error)
    {
        error = string.Empty;
        mode = HealthModelChangeMode.WhatIf;

        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, WhatIfMode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, ApplyMode, StringComparison.OrdinalIgnoreCase))
        {
            mode = HealthModelChangeMode.Apply;
            return true;
        }

        error = $"--mode must be '{WhatIfMode}' or '{ApplyMode}'.";
        return false;
    }

    internal static bool TryParseExpectation(
        string? value, out HealthModelChangeExpectation? expectation, out string error)
    {
        error = string.Empty;
        expectation = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            expectation = JsonSerializer.Deserialize(value, MonitorJsonContext.Default.HealthModelChangeExpectation);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"--expect must be a JSON object of the form {{\"affectedCount\":<int>}}. {ex.Message}";
            return false;
        }
    }
}
