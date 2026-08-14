// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;
using Azure.Mcp.Tools.Monitor.Planning;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;

namespace Azure.Mcp.Tools.Monitor.Sandbox;

/// <summary>
/// Binds the CloudHealth runners into the engine as flat host functions, which the prelude then assembles
/// into the <c>client</c> object. Every binding is a <see cref="ClrFunction"/> rather than a CLR delegate
/// or object: <c>Engine.SetValue(string, Delegate)</c> marshals through <c>Delegate.CreateDelegate</c> and
/// warns IL2026/IL2111 at each call site, and passing an object reaches Jint's reflection-based
/// <c>ObjectWrapper</c>, neither of which survives NativeAOT.
/// </summary>
/// <remarks>
/// The functions are synchronous and return the service's JSON as a string. A script still writes
/// <c>await client.entities.get(...)</c> — awaiting a non-promise is a no-op — so caller code reads like
/// the real SDK while the host never has to settle a promise from off the engine thread, which Jint's
/// event loop does not observe.
/// </remarks>
internal sealed class HealthModelSdkBridge(
    IHealthModelCallRunner readRunner,
    IHealthModelWriteRunner writeRunner,
    IHealthModelSdkRunner sdkRunner,
    IList<string> logs,
    CancellationToken cancellationToken)
{
    private static readonly ModelReaderWriterOptions WireFormat = new("J");

    /// <summary>Counts the Azure calls the script completed, so a run that hits a limit still reports progress.</summary>
    internal int AzureCalls { get; private set; }

    public void Bind(Engine engine)
    {
        RegisterLocal(engine, "__ch_log", args =>
        {
            var prefix = args[0].AsString();
            var parts = args[1].AsArray().Select(part => part.IsUndefined() ? "undefined" : part.ToString());
            logs.Add(prefix + string.Join(' ', parts));
            return JsValue.Undefined;
        });

        Register(engine, "__ch_healthModels_get", args => Json(
            sdkRunner.GetHealthModelAsync(Text(args, 0), Text(args, 1), cancellationToken).GetAwaiter().GetResult()));

        Register(engine, "__ch_healthModels_list", args =>
        {
            var page = sdkRunner.ListHealthModelsAsync(
                Optional(args, 0), Cursor(args, 1), cancellationToken).GetAwaiter().GetResult();
            return Page(page.Items.Select(Wire), page.ContinuationToken);
        });

        Register(engine, "__ch_entities_get", args => Json(
            readRunner.GetEntityAsync(Scope(args), Text(args, 2), cancellationToken).GetAwaiter().GetResult()));

        Register(engine, "__ch_entities_list", args =>
        {
            var options = Options(args, 2);
            var page = readRunner.ListEntitiesAsync(
                Scope(args), AsOf(options), CursorOf(options), cancellationToken).GetAwaiter().GetResult();
            return Page(page.Items.Select(Wire), page.ContinuationToken);
        });

        Register(engine, "__ch_relationships_list", args =>
        {
            var options = Options(args, 2);
            var page = readRunner.ListRelationshipsAsync(
                Scope(args), AsOf(options), CursorOf(options), cancellationToken).GetAwaiter().GetResult();
            return Page(page.Items.Select(Wire), page.ContinuationToken);
        });

        Register(engine, "__ch_signalDefinitions_list", args =>
        {
            var options = Options(args, 2);
            var page = readRunner.ListSignalDefinitionsAsync(
                Scope(args), AsOf(options), CursorOf(options), cancellationToken).GetAwaiter().GetResult();
            return Page(page.Items.Select(Wire), page.ContinuationToken);
        });

        Register(engine, "__ch_entities_getHistory", args =>
        {
            var body = Options(args, 3);
            return Json(readRunner.GetHistoryAsync(
                Scope(args), Text(args, 2), Time(body, "startTime"), Time(body, "endTime"),
                Count(body, "top"), String(body, "nextMarker"), cancellationToken).GetAwaiter().GetResult());
        });

        Register(engine, "__ch_entities_getSignalHistory", args =>
        {
            var body = Options(args, 3) ?? throw new ArgumentException("getSignalHistory requires a body with signalName.");
            var signal = String(body, "signalName") ?? throw new ArgumentException("getSignalHistory requires signalName.");
            return Json(readRunner.GetSignalHistoryAsync(
                Scope(args), Text(args, 2), signal, Time(body, "startTime"), Time(body, "endTime"),
                Count(body, "top"), String(body, "nextMarker"), cancellationToken).GetAwaiter().GetResult());
        });

        Register(engine, "__ch_entities_getSignalRecommendations", args => Json(
            readRunner.GetSignalRecommendationsAsync(Scope(args), Text(args, 2), cancellationToken).GetAwaiter().GetResult()));

        Register(engine, "__ch_entities_getDataAnnotations", args =>
        {
            var body = Options(args, 3);
            return Json(readRunner.GetDataAnnotationsAsync(
                Scope(args), Text(args, 2), Time(body, "startTime"), Time(body, "endTime"),
                Count(body, "top"), String(body, "nextMarker"), cancellationToken).GetAwaiter().GetResult());
        });

        Register(engine, "__ch_entities_addDataAnnotation", args => JsString.Create(
            sdkRunner.AddDataAnnotationAsync(Scope(args), Text(args, 2), Body(args, 3), cancellationToken)
                .GetAwaiter().GetResult().ToJsonString()));

        Register(engine, "__ch_entities_ingestHealthReport", args =>
        {
            sdkRunner.IngestHealthReportAsync(Scope(args), Text(args, 2), Body(args, 3), cancellationToken)
                .GetAwaiter().GetResult();
            return JsValue.Undefined;
        });

        Register(engine, "__ch_write_put", args =>
        {
            writeRunner.PutAsync(Scope(args), Kind(args, 2), Text(args, 3), Resource(args, 4), cancellationToken)
                .GetAwaiter().GetResult();
            return JsValue.Undefined;
        });

        Register(engine, "__ch_write_delete", args =>
        {
            writeRunner.DeleteAsync(Scope(args), Kind(args, 2), Text(args, 3), cancellationToken)
                .GetAwaiter().GetResult();
            return JsValue.Undefined;
        });
    }

    private void Register(Engine engine, string name, Func<JsValue[], JsValue> handler) =>
        engine.SetValue(name, new ClrFunction(engine, name, (_, args) =>
        {
            var value = handler(args);
            AzureCalls++;
            return value;
        }));

    private void RegisterLocal(Engine engine, string name, Func<JsValue[], JsValue> handler) =>
        engine.SetValue(name, new ClrFunction(engine, name, (_, args) => handler(args)));

    private static PlanScope Scope(JsValue[] args) => new(Text(args, 0), Text(args, 1));

    private static HealthModelResourceKind Kind(JsValue[] args, int index) => Text(args, index) switch
    {
        "entity" => HealthModelResourceKind.Entity,
        "relationship" => HealthModelResourceKind.Relationship,
        _ => HealthModelResourceKind.SignalDefinition,
    };

    private static string Text(JsValue[] args, int index) =>
        Optional(args, index) ?? throw new ArgumentException($"Argument {index} is required.");

    private static string? Optional(JsValue[] args, int index) =>
        index < args.Length && !args[index].IsNull() && !args[index].IsUndefined() ? args[index].AsString() : null;

    private static JsonObject Body(JsValue[] args, int index) =>
        Options(args, index) ?? throw new ArgumentException($"Argument {index} must be an object.");

    /// <summary>
    /// A resource body, with the properties the resource provider owns removed.
    /// </summary>
    /// <remarks>
    /// Only a resource upsert strips. The action payloads share several of these names as ordinary inputs:
    /// a health report exists to carry a <c>healthState</c>, and stripping it by name deletes the one field
    /// the operation is for, with no error. Applying the deny list by operation rather than by field name
    /// is what keeps that distinction.
    /// </remarks>
    private static JsonObject Resource(JsValue[] args, int index) => Strip(Body(args, index));

    /// <summary>
    /// Removes the properties the resource provider owns. A body read back from the service carries them,
    /// and the write endpoint rejects them, so a script that round-trips a resource would otherwise have to
    /// delete each one by hand. The deny list is anchored to the envelope root and directly under
    /// <c>properties</c>, matching <see cref="HealthModelMergePatch"/>; deeper, these names are caller data.
    /// </summary>
    private static JsonObject Strip(JsonObject body)
    {
        foreach (var owned in HealthModelMergePatch.ServerOwnedProperties)
        {
            body.Remove(owned);
        }

        if (body["properties"] is JsonObject properties)
        {
            foreach (var owned in HealthModelMergePatch.ServerOwnedProperties)
            {
                properties.Remove(owned);
            }
        }

        return body;
    }

    private static JsonObject? Options(JsValue[] args, int index) =>
        Optional(args, index) is { } raw ? JsonNode.Parse(raw)?.AsObject() : null;

    private static string? Cursor(JsValue[] args, int index) => CursorOf(Options(args, index));

    private static string? CursorOf(JsonObject? options) => String(options, "cursor");

    private static DateTimeOffset? AsOf(JsonObject? options) => Time(options, "asOf");

    private static string? String(JsonObject? options, string name) =>
        options?[name] is { } node ? node.GetValue<string>() : null;

    private static int? Count(JsonObject? options, string name) =>
        options?[name] is { } node ? node.GetValue<int>() : null;

    private static DateTimeOffset? Time(JsonObject? options, string name) =>
        String(options, name) is { } text ? DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : null;

    private static JsValue Page(IEnumerable<JsonNode> items, string? nextLink)
    {
        var page = new JsonObject
        {
            ["value"] = new JsonArray([.. items]),
            ["nextLink"] = nextLink is null ? null : JsonValue.Create(nextLink),
        };
        return JsString.Create(page.ToJsonString());
    }

    private static JsValue Json<T>(T payload) where T : IPersistableModel<T> =>
        JsString.Create(payload.Write(WireFormat).ToString());

    private static JsonNode Wire<T>(T payload) where T : IPersistableModel<T> =>
        JsonNode.Parse(payload.Write(WireFormat).ToMemory().Span)!;
}
