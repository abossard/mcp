// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Planning;
using Azure.Mcp.Tools.Monitor.Services;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace Azure.Mcp.Tools.Monitor.Sandbox;

/// <summary>
/// Runs one caller-authored script against one engine, then throws the engine away. CLR interop is left at
/// its default (off), so the script has no route to a .NET type, and the engine is created with no network,
/// filesystem, module loader, or timer host functions — the only capabilities it holds are the CloudHealth
/// functions the bridge binds, which close over credentials the script can never name.
/// </summary>
internal static class HealthModelScriptRuntime
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    public const int MaxStatements = 5_000_000;
    public const int MaxRecursionDepth = 64;

    public static HealthModelScriptResult Run(
        string code,
        IHealthModelCallRunner readRunner,
        IHealthModelWriteRunner writeRunner,
        IHealthModelSdkRunner sdkRunner,
        CancellationToken cancellationToken)
    {
        var logs = new List<string>();
        var engine = new Engine(options =>
        {
            options.TimeoutInterval(Timeout);
            options.MaxStatements(MaxStatements);
            options.LimitRecursion(MaxRecursionDepth);
            options.CancellationToken(cancellationToken);
            options.Strict = true;
        });

        var bridge = new HealthModelSdkBridge(readRunner, writeRunner, sdkRunner, logs, cancellationToken);
        bridge.Bind(engine);
        engine.Execute(HealthModelSdkPrelude.Source);

        try
        {
            var completion = engine.Evaluate(HealthModelScriptNormalizer.Normalize(code)).UnwrapIfPromise(cancellationToken);
            var serialized = Serialize(engine, completion);
            var truncated = serialized is not null && HealthModelScriptOutput.TryTruncate(serialized, out var capped);

            return new HealthModelScriptResult
            {
                Result = truncated ? JsonValue.Create(Capped(serialized!)) : Parse(serialized),
                Logs = logs,
                Truncated = truncated,
                AzureCalls = bridge.AzureCalls,
            };
        }
        catch (ExecutionCanceledException)
        {
            // Jint reports an observed cancellation with its own type, which does not derive from
            // OperationCanceledException. A cancelled request aborts; it is not a script-level failure.
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthModelScriptResult { Logs = logs, Error = Describe(ex), AzureCalls = bridge.AzureCalls };
        }
    }

    private static string Capped(string serialized)
    {
        HealthModelScriptOutput.TryTruncate(serialized, out var capped);
        return capped;
    }

    private static string? Serialize(Engine engine, JsValue completion)
    {
        if (completion.IsUndefined() || completion.IsNull())
        {
            return null;
        }

        engine.SetValue("__ch_completion", completion);
        var json = engine.Evaluate("JSON.stringify(__ch_completion ?? null)");
        return json.IsString() ? json.AsString() : null;
    }

    private static JsonNode? Parse(string? serialized) =>
        string.IsNullOrEmpty(serialized) ? null : JsonNode.Parse(serialized);

    /// <summary>
    /// A failure raised by an Azure call inside a script belongs to that script, not to the command: it is
    /// reported through the same result contract so whatever the script logged before it failed survives.
    /// Service failures go through <see cref="HealthModelError"/>, which keeps the service's own sentence
    /// and drops the status line, body echo and header dump the SDK appends to it.
    /// </summary>
    private static string Describe(Exception ex) => ex switch
    {
        TimeoutException => $"Script exceeded the {Timeout.TotalSeconds:F0}s execution limit.",
        StatementsCountOverflowException => $"Script exceeded the {MaxStatements} statement limit.",
        RecursionDepthOverflowException => $"Script exceeded the recursion depth limit of {MaxRecursionDepth}.",
        MemoryLimitExceededException => "Script exceeded the sandbox memory limit.",
        JavaScriptException or PromiseRejectedException => ex.Message,
        _ => HealthModelError.Describe(ex),
    };
}
