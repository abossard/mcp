// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ClientModel.Primitives;
using System.Globalization;
using System.Text.Json.Nodes;
using Azure;
using Azure.Mcp.Tools.Monitor.Planning;
using Azure.Mcp.Tools.Monitor.Services;
using Azure.ResourceManager.CloudHealth;
using Azure.ResourceManager.CloudHealth.Models;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace Azure.Mcp.Tools.Monitor.Sandbox;

internal sealed class HealthModelReadBridge(
    IHealthModelReadCodeRunner readRunner,
    IList<string> logs,
    CancellationToken cancellationToken)
{
    private const string ApiVersion = "2026-05-01-preview";
    private static readonly ModelReaderWriterOptions WireFormat = new("J");
    private readonly IHealthModelReadCodeRunner _readRunner = readRunner;
    private readonly IList<string> _logs = logs;
    private readonly CancellationToken _cancellationToken = cancellationToken;
    private readonly SemaphoreSlim _readGate = new(4, 4);
    private readonly List<PendingRead> _pendingReads = [];
    private int _azureCalls;
    private int _frozenAzureCalls = -1;
    private int _admissionClosed;

    internal int AzureCalls
    {
        get
        {
            var frozen = Volatile.Read(ref _frozenAzureCalls);
            return frozen >= 0 ? frozen : Volatile.Read(ref _azureCalls);
        }
    }

    public void Bind(Engine engine)
    {
        RegisterLocal(engine, "__ch_log", args =>
        {
            var prefix = args[0].AsString();
            var parts = args[1].AsArray().Select(part => part.IsUndefined() ? "undefined" : part.ToString());
            _logs.Add(prefix + string.Join(' ', parts));
            return JsValue.Undefined;
        });

        RegisterRead(engine, "__ch_healthModels_get", "healthModels.get",
            args => ReadHealthModelAsync(Text(args, 0), Text(args, 1)));
        RegisterRead(engine, "__ch_healthModels_listByResourceGroup", "healthModels.listByResourceGroup",
            args => ListHealthModelsAsync(Text(args, 0), Options(args, 1), bySubscription: false));
        RegisterRead(engine, "__ch_healthModels_listBySubscription", "healthModels.listBySubscription",
            args => ListHealthModelsAsync(resourceGroup: null, Options(args, 0), bySubscription: true));

        RegisterRead(engine, "__ch_entities_get", "entities.get",
            args => ReadEntityAsync(Scope(args), Text(args, 2)));
        RegisterRead(engine, "__ch_entities_listByHealthModel", "entities.listByHealthModel",
            args => ListEntitiesAsync(Scope(args), Options(args, 2)));
        RegisterRead(engine, "__ch_entities_getHistory", "entities.getHistory",
            args => GetHistoryAsync(Scope(args), Text(args, 2), Options(args, 3)));
        RegisterRead(engine, "__ch_entities_getSignalHistory", "entities.getSignalHistory",
            args => GetSignalHistoryAsync(Scope(args), Text(args, 2), Options(args, 3)));
        RegisterRead(engine, "__ch_entities_getSignalRecommendations", "entities.getSignalRecommendations",
            args => GetSignalRecommendationsAsync(Scope(args), Text(args, 2)));
        RegisterRead(engine, "__ch_entities_getDataAnnotations", "entities.getDataAnnotations",
            args => GetDataAnnotationsAsync(Scope(args), Text(args, 2), Options(args, 3)));

        RegisterRead(engine, "__ch_relationships_get", "relationships.get",
            args => GetRelationshipAsync(Scope(args), Text(args, 2)));
        RegisterRead(engine, "__ch_relationships_listByHealthModel", "relationships.listByHealthModel",
            args => ListRelationshipsAsync(Scope(args), Options(args, 2)));

        RegisterRead(engine, "__ch_signalDefinitions_get", "signalDefinitions.get",
            args => GetSignalDefinitionAsync(Scope(args), Text(args, 2)));
        RegisterRead(engine, "__ch_signalDefinitions_listByHealthModel", "signalDefinitions.listByHealthModel",
            args => ListSignalDefinitionsAsync(Scope(args), Options(args, 2)));

        RegisterRead(engine, "__ch_authenticationSettings_get", "authenticationSettings.get",
            args => GetAuthenticationSettingAsync(Scope(args), Text(args, 2)));
        RegisterRead(engine, "__ch_authenticationSettings_listByHealthModel", "authenticationSettings.listByHealthModel",
            args => ListAuthenticationSettingsAsync(Scope(args), Options(args, 2)));

        RegisterRead(engine, "__ch_discoveryRules_get", "discoveryRules.get",
            args => GetDiscoveryRuleAsync(Scope(args), Text(args, 2)));
        RegisterRead(engine, "__ch_discoveryRules_listByHealthModel", "discoveryRules.listByHealthModel",
            args => ListDiscoveryRulesAsync(Scope(args), Options(args, 2)));

        RegisterRead(engine, "__ch_operations_list", "operations.list",
            _ => ListOperationsAsync());
    }

    public JsValue AwaitCompletion(Engine engine, JsValue completion)
    {
        var completionState = CompletionState.Pending;
        var completionValue = JsValue.Undefined;
        var completionError = JsValue.Undefined;

        RegisterLocal(engine, "__ch_internal_completion_resolve_7f5d1904", args =>
        {
            completionState = CompletionState.Fulfilled;
            completionValue = args.Length > 0 ? args[0] : JsValue.Undefined;
            return JsValue.Undefined;
        });

        RegisterLocal(engine, "__ch_internal_completion_reject_7f5d1904", args =>
        {
            completionState = CompletionState.Rejected;
            completionError = args.Length > 0 ? args[0] : JsValue.Undefined;
            return JsValue.Undefined;
        });

        engine.SetValue("__ch_internal_completion_value_7f5d1904", completion);
        engine.Execute(
            """
            Promise.resolve(__ch_internal_completion_value_7f5d1904)
              .then(__ch_internal_completion_resolve_7f5d1904)
              .catch(__ch_internal_completion_reject_7f5d1904);
            """);

        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            SettleCompleted(engine);
            engine.Advanced.ProcessTasks();

            if (completionState is CompletionState.Fulfilled)
            {
                return completionValue;
            }

            if (completionState is CompletionState.Rejected)
            {
                throw new PromiseRejectedException(completionError);
            }

            if (_pendingReads.Count == 0)
            {
                Task.Delay(TimeSpan.FromMilliseconds(10), _cancellationToken).Wait(_cancellationToken);
                continue;
            }

            Task.WhenAny(_pendingReads.Select(read => read.Operation)).Wait(_cancellationToken);
        }
    }

    public void DrainPending()
    {
        Interlocked.Exchange(ref _admissionClosed, 1);
        var pendingReads = _pendingReads.ToArray();
        _pendingReads.Clear();

        var pendingOperations = pendingReads.Select(read => read.Operation).ToHashSet();
        while (pendingOperations.Count > 0)
        {
            var completed = Task.WhenAny(pendingOperations).GetAwaiter().GetResult();
            pendingOperations.Remove(completed);
        }

        foreach (var operation in pendingReads.Select(read => read.Operation))
        {
            if (operation.IsFaulted)
            {
                var fault = operation.Exception?.GetBaseException() ?? new InvalidOperationException("Unknown read failure.");
                _logs.Add($"[warn] cleanup observed read fault: {HealthModelError.Describe(fault)}");
                continue;
            }

            if (operation.IsCanceled)
            {
                _logs.Add("[warn] cleanup observed cancelled read.");
            }
        }

        Volatile.Write(ref _frozenAzureCalls, Volatile.Read(ref _azureCalls));
    }

    private void RegisterRead(Engine engine, string bindingName, string operationName, Func<JsValue[], Task<string>> handler) =>
        Register(engine, bindingName, args =>
        {
            if (Volatile.Read(ref _admissionClosed) == 1)
            {
                throw new OperationCanceledException($"{operationName} cancelled.");
            }

            var manualPromise = engine.Advanced.RegisterPromise();
            _pendingReads.Add(new PendingRead(operationName, handler(args), manualPromise.Resolve, manualPromise.Reject));
            return manualPromise.Promise;
        });

    private void Register(Engine engine, string name, Func<JsValue[], JsValue> handler) =>
        engine.SetValue(name, new ClrFunction(engine, name, (_, args) => handler(args)));

    private void RegisterLocal(Engine engine, string name, Func<JsValue[], JsValue> handler) =>
        engine.SetValue(name, new ClrFunction(engine, name, (_, args) => handler(args)));

    private async Task<string> ReadHealthModelAsync(string resourceGroup, string healthModelName)
    {
        var payload = await ReadAsync(
            () => _readRunner.GetHealthModelReadAsync(resourceGroup, healthModelName, _cancellationToken));
        return Json(payload);
    }

    private async Task<string> ListHealthModelsAsync(string? resourceGroup, JsonObject? options, bool bySubscription)
    {
        var expectedPath = bySubscription
            ? $"/subscriptions/{_readRunner.SubscriptionId}/providers/Microsoft.CloudHealth/healthmodels"
            : $"/subscriptions/{_readRunner.SubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.CloudHealth/healthmodels";
        var operation = bySubscription ? "healthModels.listBySubscription" : "healthModels.listByResourceGroup";
        var (_, cursor) = ParseListOptions(options, expectedPath, operation, allowAsOf: false);

        var page = await ReadAsync(() =>
            _readRunner.ListHealthModelsAsync(resourceGroup, cursor, _cancellationToken));
        return Page(page.Items, page.ContinuationToken);
    }

    private async Task<string> ReadEntityAsync(PlanScope scope, string entityName)
    {
        var payload = await ReadAsync(() => _readRunner.GetEntityAsync(scope, entityName, _cancellationToken));
        return Json(payload);
    }

    private async Task<string> ListEntitiesAsync(PlanScope scope, JsonObject? options)
    {
        var (asOf, cursor) = ParseListOptions(
            options,
            CollectionPath(scope, "entities"),
            "entities.listByHealthModel",
            allowAsOf: true);
        var page = await ReadAsync(() => _readRunner.ListEntitiesAsync(scope, asOf, cursor, _cancellationToken));
        return Page(page.Items, page.ContinuationToken);
    }

    private async Task<string> GetHistoryAsync(PlanScope scope, string entityName, JsonObject? body)
    {
        var request = ParseHistoryRequest(body, "entities.getHistory", requireSignalName: false);
        var payload = await ReadAsync(() =>
            _readRunner.GetHistoryAsync(
                scope,
                entityName,
                request.StartTime,
                request.EndTime,
                request.Top,
                request.NextMarker,
                _cancellationToken));
        HealthModelPaginator.EnsureMarkerAdvanced(request.NextMarker, payload.NextMarker);
        return Json(payload);
    }

    private async Task<string> GetSignalHistoryAsync(PlanScope scope, string entityName, JsonObject? body)
    {
        var request = ParseHistoryRequest(body, "entities.getSignalHistory", requireSignalName: true);
        var payload = await ReadAsync(() =>
            _readRunner.GetSignalHistoryAsync(
                scope,
                entityName,
                request.SignalName!,
                request.StartTime,
                request.EndTime,
                request.Top,
                request.NextMarker,
                _cancellationToken));
        HealthModelPaginator.EnsureMarkerAdvanced(request.NextMarker, payload.NextMarker);
        return Json(payload);
    }

    private async Task<string> GetSignalRecommendationsAsync(PlanScope scope, string entityName)
    {
        var payload = await ReadAsync(() =>
            _readRunner.GetSignalRecommendationsAsync(scope, entityName, _cancellationToken));
        return Json(payload);
    }

    private async Task<string> GetDataAnnotationsAsync(PlanScope scope, string entityName, JsonObject? body)
    {
        var request = ParseHistoryRequest(body, "entities.getDataAnnotations", requireSignalName: false);
        var payload = await ReadAsync(() =>
            _readRunner.GetDataAnnotationsAsync(
                scope,
                entityName,
                request.StartTime,
                request.EndTime,
                request.Top,
                request.NextMarker,
                _cancellationToken));
        HealthModelPaginator.EnsureMarkerAdvanced(request.NextMarker, payload.NextMarker);
        return Json(payload);
    }

    private async Task<string> GetRelationshipAsync(PlanScope scope, string relationshipName)
    {
        var payload = await ReadAsync(() =>
            _readRunner.GetRelationshipAsync(scope, relationshipName, _cancellationToken));
        return Json(payload);
    }

    private async Task<string> ListRelationshipsAsync(PlanScope scope, JsonObject? options)
    {
        var (asOf, cursor) = ParseListOptions(
            options,
            CollectionPath(scope, "relationships"),
            "relationships.listByHealthModel",
            allowAsOf: true);
        var page = await ReadAsync(() => _readRunner.ListRelationshipsAsync(scope, asOf, cursor, _cancellationToken));
        return Page(page.Items, page.ContinuationToken);
    }

    private async Task<string> GetSignalDefinitionAsync(PlanScope scope, string signalDefinitionName)
    {
        var payload = await ReadAsync(() =>
            _readRunner.GetSignalDefinitionAsync(scope, signalDefinitionName, _cancellationToken));
        return Json(payload);
    }

    private async Task<string> ListSignalDefinitionsAsync(PlanScope scope, JsonObject? options)
    {
        var (asOf, cursor) = ParseListOptions(
            options,
            CollectionPath(scope, "signalDefinitions"),
            "signalDefinitions.listByHealthModel",
            allowAsOf: true);
        var page = await ReadAsync(() => _readRunner.ListSignalDefinitionsAsync(scope, asOf, cursor, _cancellationToken));
        return Page(page.Items, page.ContinuationToken);
    }

    private async Task<string> GetAuthenticationSettingAsync(PlanScope scope, string authenticationSettingName)
    {
        var payload = await ReadAsync(() =>
            _readRunner.GetAuthenticationSettingAsync(scope, authenticationSettingName, _cancellationToken));
        return Json(payload);
    }

    private async Task<string> ListAuthenticationSettingsAsync(PlanScope scope, JsonObject? options)
    {
        var (_, cursor) = ParseListOptions(
            options,
            CollectionPath(scope, "authenticationSettings"),
            "authenticationSettings.listByHealthModel",
            allowAsOf: false);
        var page = await ReadAsync(() => _readRunner.ListAuthenticationSettingsAsync(scope, cursor, _cancellationToken));
        return Page(page.Items, page.ContinuationToken);
    }

    private async Task<string> GetDiscoveryRuleAsync(PlanScope scope, string discoveryRuleName)
    {
        var payload = await ReadAsync(() =>
            _readRunner.GetDiscoveryRuleAsync(scope, discoveryRuleName, _cancellationToken));
        return Json(payload);
    }

    private async Task<string> ListDiscoveryRulesAsync(PlanScope scope, JsonObject? options)
    {
        var (asOf, cursor) = ParseListOptions(
            options,
            CollectionPath(scope, "discoveryRules"),
            "discoveryRules.listByHealthModel",
            allowAsOf: true);
        var page = await ReadAsync(() => _readRunner.ListDiscoveryRulesAsync(scope, asOf, cursor, _cancellationToken));
        return Page(page.Items, page.ContinuationToken);
    }

    private async Task<string> ListOperationsAsync()
    {
        var payload = await ReadAsync(() => _readRunner.ListOperationsAsync(_cancellationToken));
        return payload.ToJsonString();
    }

    private async Task<T> ReadAsync<T>(Func<Task<T>> readOperation)
    {
        await _readGate.WaitAsync(_cancellationToken).ConfigureAwait(false);
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var payload = await readOperation().ConfigureAwait(false);
            Interlocked.Increment(ref _azureCalls);
            return payload;
        }
        finally
        {
            _readGate.Release();
        }
    }

    private void SettleCompleted(Engine engine)
    {
        for (var index = _pendingReads.Count - 1; index >= 0; index--)
        {
            var pending = _pendingReads[index];
            if (!pending.Operation.IsCompleted)
            {
                continue;
            }

            _pendingReads.RemoveAt(index);

            if (pending.Operation.IsCanceled)
            {
                pending.Reject(CreateJsError(engine, pending.OperationName, status: null, code: "Cancelled", message: "cancelled."));
                continue;
            }

            if (pending.Operation.IsFaulted)
            {
                var exception = pending.Operation.Exception?.GetBaseException() ?? new InvalidOperationException("Unknown read failure.");
                pending.Reject(CreateError(engine, pending.OperationName, exception));
                continue;
            }

            pending.Resolve(JsString.Create(pending.Operation.Result));
        }
    }

    private static JsValue CreateError(Engine engine, string operationName, Exception exception)
    {
        if (exception is RequestFailedException requestFailedException)
        {
            return CreateJsError(
                engine,
                operationName,
                requestFailedException.Status,
                requestFailedException.ErrorCode ?? "Unknown",
                DescribeRequestFailedForScript(requestFailedException));
        }

        if (exception is OperationCanceledException)
        {
            return CreateJsError(engine, operationName, status: null, code: "Cancelled", message: "cancelled.");
        }

        return CreateJsError(engine, operationName, status: null, code: exception.GetType().Name, message: HealthModelError.Describe(exception));
    }

    private static JsValue CreateJsError(
        Engine engine,
        string operationName,
        int? status,
        string? code,
        string message)
    {
        var error = engine.Intrinsics.Error.Construct(
            [JsString.Create(message)],
            JsValue.Undefined);

        if (error is not ObjectInstance errorObject)
        {
            return error;
        }

        errorObject.Set(JsString.Create("operation"), JsString.Create(operationName), errorObject);
        errorObject.Set(
            JsString.Create("status"),
            status is { } value ? JsNumber.Create(value) : JsValue.Null,
            errorObject);
        errorObject.Set(
            JsString.Create("code"),
            code is null ? JsValue.Null : JsString.Create(code),
            errorObject);
        errorObject.Set(JsString.Create("message"), JsString.Create(message), errorObject);

        return errorObject;
    }

    private static string DescribeRequestFailedForScript(RequestFailedException exception)
    {
        var serviceMessage = ExtractServiceMessage(exception.Message);
        if (string.IsNullOrEmpty(serviceMessage))
        {
            return HealthModelError.Describe(exception);
        }

        var hasCode = !string.IsNullOrWhiteSpace(exception.ErrorCode);
        var qualifier = hasCode
            ? $"HTTP {exception.Status}, {exception.ErrorCode}"
            : $"HTTP {exception.Status}";
        return $"{serviceMessage} ({qualifier})";
    }

    private static string ExtractServiceMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var lines = message.Split('\n');
        var serviceLines = new List<string>(capacity: lines.Length);
        foreach (var line in lines)
        {
            var normalized = line.TrimEnd('\r');
            if (IsDecoratedStatusLine(normalized))
            {
                break;
            }

            if (normalized.Length == 0)
            {
                continue;
            }

            serviceLines.Add(normalized);
        }

        return string.Join('\n', serviceLines);
    }

    private static bool IsDecoratedStatusLine(string line)
    {
        if (!line.StartsWith("Status:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var span = line.AsSpan("Status:".Length).TrimStart();
        var digits = 0;
        while (digits < span.Length && char.IsDigit(span[digits]))
        {
            digits++;
        }

        return digits > 0;
    }

    private static PlanScope Scope(JsValue[] args) => new(Text(args, 0), Text(args, 1));

    private static string Text(JsValue[] args, int index) =>
        index < args.Length && !args[index].IsNull() && !args[index].IsUndefined()
            ? args[index].AsString()
            : throw new ArgumentException($"Argument {index} is required.");

    private static JsonObject? Options(JsValue[] args, int index)
    {
        if (index >= args.Length || args[index].IsNull() || args[index].IsUndefined())
        {
            return null;
        }

        var raw = args[index].AsString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var node = JsonNode.Parse(raw);
        if (node is null)
        {
            return null;
        }

        return node as JsonObject ?? throw new ArgumentException($"Argument {index} must be an object.");
    }

    private (DateTimeOffset? AsOf, string? Cursor) ParseListOptions(
        JsonObject? options,
        string expectedPath,
        string operationName,
        bool allowAsOf)
    {
        var asOf = DateTime(options, "asOf");
        var cursor = String(options, "cursor");

        if (!allowAsOf && asOf is not null)
        {
            throw new ArgumentException($"{operationName} does not accept options.asOf.");
        }

        if (!string.IsNullOrEmpty(cursor) && asOf is not null)
        {
            throw new ArgumentException(
                $"{operationName}: options.cursor resumes a page and cannot be combined with options.asOf.");
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            cursor = ValidateContinuation(cursor, expectedPath, operationName);
        }

        return (asOf, cursor);
    }

    private string ValidateContinuation(string cursor, string expectedPath, string operationName)
    {
        if (!Uri.TryCreate(cursor, UriKind.RelativeOrAbsolute, out var parsed))
        {
            throw new ArgumentException($"{operationName}: options.cursor must be an absolute ARM URL or absolute ARM path.");
        }

        var endpoint = new Uri(_readRunner.ArmEndpoint.EndsWith("/", StringComparison.Ordinal)
            ? _readRunner.ArmEndpoint
            : $"{_readRunner.ArmEndpoint}/");

        Uri absolute;
        if (parsed.IsAbsoluteUri)
        {
            absolute = parsed;
        }
        else
        {
            if (!cursor.StartsWith("/", StringComparison.Ordinal))
            {
                throw new ArgumentException($"{operationName}: options.cursor must start with '/'.");
            }

            absolute = new Uri(endpoint, cursor);
        }

        var escapedPath = absolute.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        if (escapedPath.Contains("%2f", StringComparison.OrdinalIgnoreCase) ||
            escapedPath.Contains("%5c", StringComparison.OrdinalIgnoreCase) ||
            escapedPath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{operationName}: options.cursor has an invalid path.");
        }

        if (!string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{operationName}: options.cursor must use https.");
        }

        if (!string.IsNullOrEmpty(absolute.UserInfo) || !string.IsNullOrEmpty(absolute.Fragment))
        {
            throw new ArgumentException($"{operationName}: options.cursor cannot include user info or fragments.");
        }

        if (!string.Equals(absolute.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase) ||
            absolute.Port != endpoint.Port)
        {
            throw new ArgumentException($"{operationName}: options.cursor must target the configured ARM endpoint.");
        }

        if (!string.Equals(
                TrimTrailingSlash(absolute.AbsolutePath),
                TrimTrailingSlash(expectedPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{operationName}: options.cursor must stay in the same collection.");
        }

        if (!TryReadQueryValue(absolute.Query, "api-version", out var apiVersion) ||
            !string.Equals(apiVersion, ApiVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{operationName}: options.cursor must keep api-version={ApiVersion}.");
        }

        return parsed.IsAbsoluteUri ? cursor : absolute.AbsoluteUri;
    }

    private static bool TryReadQueryValue(string query, string key, out string? value)
    {
        value = null;
        if (string.IsNullOrEmpty(query))
        {
            return false;
        }

        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = segment.IndexOf('=');
            var currentKey = separator >= 0 ? segment[..separator] : segment;
            if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = separator >= 0 ? Uri.UnescapeDataString(segment[(separator + 1)..]) : string.Empty;
            return true;
        }

        return false;
    }

    private static string TrimTrailingSlash(string path) =>
        path.EndsWith("/", StringComparison.Ordinal) ? path[..^1] : path;

    private string CollectionPath(PlanScope scope, string collection) =>
        $"/subscriptions/{_readRunner.SubscriptionId}/resourceGroups/{scope.ResourceGroup}/providers/Microsoft.CloudHealth/healthmodels/{scope.HealthModel}/{collection}";

    private static string? String(JsonObject? options, string name) =>
        options?[name] is JsonValue value ? value.GetValue<string>() : null;

    private static int? Int(JsonObject? options, string name) =>
        options?[name] is JsonValue value ? value.GetValue<int>() : null;

    private static DateTimeOffset? DateTime(JsonObject? options, string name)
    {
        if (options?[name] is not JsonValue value)
        {
            return null;
        }

        return DateTimeOffset.Parse(value.GetValue<string>(), CultureInfo.InvariantCulture);
    }

    private static string Json<T>(T payload) where T : IPersistableModel<T> =>
        ModelReaderWriter.Write(
            payload,
            WireFormat,
            ResourceManager.CloudHealth.AzureResourceManagerCloudHealthContext.Default).ToString();

    private static string Page<T>(IEnumerable<T> items, string? nextLink)
        where T : IPersistableModel<T>
    {
        var page = new JsonObject
        {
            ["value"] = new JsonArray([.. items.Select(item => JsonNode.Parse(Json(item)))]),
            ["nextLink"] = nextLink is null ? null : JsonValue.Create(nextLink),
        };
        return page.ToJsonString();
    }

    private sealed record HistoryRequest(
        DateTimeOffset? StartTime,
        DateTimeOffset? EndTime,
        int? Top,
        string? NextMarker,
        string? SignalName);

    private static HistoryRequest ParseHistoryRequest(JsonObject? body, string operationName, bool requireSignalName)
    {
        var start = DateTime(body, "startTime");
        var end = DateTime(body, "endTime");
        var top = Int(body, "top");
        var marker = String(body, "nextMarker");
        var signal = String(body, "signalName");

        if (!string.IsNullOrEmpty(marker) && (start is not null || end is not null))
        {
            throw new ArgumentException(
                $"{operationName}: body.nextMarker resumes a page and cannot be combined with body.startTime or body.endTime.");
        }

        if (requireSignalName && string.IsNullOrWhiteSpace(signal))
        {
            throw new ArgumentException($"{operationName}: body.signalName is required.");
        }

        return new HistoryRequest(start, end, top, marker, signal);
    }

    private sealed record PendingRead(
        string OperationName,
        Task<string> Operation,
        Action<JsValue> Resolve,
        Action<JsValue> Reject);

    private enum CompletionState
    {
        Pending,
        Fulfilled,
        Rejected,
    }
}
