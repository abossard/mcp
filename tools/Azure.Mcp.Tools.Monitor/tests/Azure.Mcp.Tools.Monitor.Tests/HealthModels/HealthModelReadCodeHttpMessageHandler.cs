// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

internal sealed class HealthModelReadCodeHttpMessageHandler(IReadOnlySet<string>? models) : HttpMessageHandler
{
    private readonly IReadOnlySet<string>? _models = models;
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _responder;
    private readonly bool _gateEnabled = models is not null;
    private readonly SemaphoreSlim _releaseGate = new(0);
    private readonly ConcurrentQueue<ObservedRequest> _requests = [];
    private int _activeRequests;
    private int _maxActiveRequests;
    private int _startedRequests;
    private int _cancelledRequests;

    internal HealthModelReadCodeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : this(models: null)
    {
        _responder = responder;
    }

    internal int StartedRequests => Volatile.Read(ref _startedRequests);
    internal int MaxActiveRequests => Volatile.Read(ref _maxActiveRequests);
    internal int CancelledRequests => Volatile.Read(ref _cancelledRequests);
    internal int ActiveRequests => Volatile.Read(ref _activeRequests);
    internal IReadOnlyList<ObservedRequest> Requests => _requests.ToArray();

    internal void Release(int count) => _releaseGate.Release(count);

    internal async Task WaitForStartedAsync(int expected, CancellationToken cancellationToken)
    {
        while (StartedRequests < expected)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var active = Interlocked.Increment(ref _activeRequests);
        UpdateMax(active);
        Interlocked.Increment(ref _startedRequests);

        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var query = request.RequestUri?.Query ?? string.Empty;
        var model = ReadSegmentValue(path, "healthmodels");
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Enqueue(new ObservedRequest(request.Method.Method, path, query, model, body));

        try
        {
            if (_gateEnabled)
            {
                await _releaseGate.WaitAsync(cancellationToken);
            }

            if (_responder is not null)
            {
                return await _responder(request, cancellationToken);
            }

            if (request.Method != HttpMethod.Get || model is null || _models is null || !_models.Contains(model))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(
                        """{"error":{"code":"NotFound","message":"The requested health model was not found."}}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            var resourceGroup = ReadSegmentValue(path, "resourceGroups") ?? "rg";
            var payload = JsonSerializer.Serialize(new
            {
                id = $"/subscriptions/sub-test/resourceGroups/{resourceGroup}/providers/Microsoft.CloudHealth/healthmodels/{model}",
                name = model,
                type = "Microsoft.CloudHealth/healthmodels",
                location = "westeurope",
                properties = new
                {
                    provisioningState = "Succeeded",
                },
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelledRequests);
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _activeRequests);
        }
    }

    private void UpdateMax(int active)
    {
        var snapshot = Volatile.Read(ref _maxActiveRequests);
        while (active > snapshot)
        {
            var observed = Interlocked.CompareExchange(ref _maxActiveRequests, active, snapshot);
            if (observed == snapshot)
            {
                break;
            }

            snapshot = observed;
        }
    }

    private static string? ReadSegmentValue(string path, string key)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 1 < segments.Length; index++)
        {
            if (string.Equals(segments[index], key, StringComparison.OrdinalIgnoreCase))
            {
                return segments[index + 1];
            }
        }

        return null;
    }

    internal sealed record ObservedRequest(
        string Method,
        string Path,
        string Query,
        string? Model,
        string? Body);
}
