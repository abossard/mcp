// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Text.RegularExpressions;

namespace Azure.Mcp.Tools.Monitor.Services;

/// <summary>
/// Renders an exception into the compact, actionable text carried by a query or entity <c>error</c> field.
/// </summary>
/// <remarks>
/// <see cref="RequestFailedException.Message"/> appends the status line, a verbatim echo of the response body,
/// and every response header to the service's own sentence, which pushes the one actionable line under roughly a
/// kilobyte of transport noise. This keeps the service message first and re-states only the two facts a caller
/// acts on — HTTP status and service error code — as a short suffix. Non-Azure exceptions are passed through
/// unchanged.
/// </remarks>
internal static partial class HealthModelError
{
    /// <summary>
    /// Matches only the SDK's own decorated status line, for example <c>Status: 400 (Bad Request)</c>. A service
    /// sentence that happens to contain its own <c>Status:</c> line (such as "Status: pending") is kept.
    /// </summary>
    [GeneratedRegex(@"^Status:\s*\d+\b")]
    private static partial Regex DecoratedStatusLine { get; }

    internal static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is RequestFailedException failure
            ? Describe(failure)
            : exception.Message;
    }

    private static string Describe(RequestFailedException failure)
    {
        var summary = ServiceMessage(failure.Message);
        var qualifier = Qualifier(failure.Status, failure.ErrorCode);

        if (summary.Length == 0)
        {
            return qualifier ?? failure.Message.Trim();
        }

        return qualifier is null ? summary : $"{summary} ({qualifier})";
    }

    /// <summary>
    /// Keeps every line the service wrote and drops the SDK's appended diagnostics, which always start at the
    /// decorated status line. Returns empty when the service supplied no message of its own — the SDK then opens
    /// its text with that status line, and echoing the raw message would reinstate the whole header dump.
    /// </summary>
    private static string ServiceMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var line in message.Split('\n'))
        {
            var trimmed = line.Trim();
            if (DecoratedStatusLine.IsMatch(trimmed))
            {
                break;
            }
            if (trimmed.Length == 0)
            {
                continue;
            }
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }
            builder.Append(trimmed);
        }

        return builder.ToString();
    }

    private static string? Qualifier(int status, string? errorCode)
    {
        var hasStatus = status > 0;
        var hasCode = !string.IsNullOrWhiteSpace(errorCode);

        return (hasStatus, hasCode) switch
        {
            (true, true) => $"HTTP {status}, {errorCode}",
            (true, false) => $"HTTP {status}",
            (false, true) => errorCode,
            _ => null,
        };
    }
}
