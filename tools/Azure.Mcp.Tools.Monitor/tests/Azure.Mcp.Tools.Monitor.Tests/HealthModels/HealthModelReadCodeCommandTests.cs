// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// cspell:ignore skiptoken

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Collections.Concurrent;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Mcp.Core.Services.Azure;
using Azure.Mcp.Core.Services.Azure.Subscription;
using Azure.Mcp.Tools.Monitor.Commands;
using Azure.Mcp.Tools.Monitor.Commands.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Sandbox;
using Azure.Mcp.Tools.Monitor.Services;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;
using Microsoft.Mcp.Core.Options;
using Microsoft.Mcp.Core.Services.Azure.Authentication;
using NSubstitute;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

public class HealthModelReadCodeCommandTests
{
    private const string Subscription = "11111111-1111-1111-1111-111111111111";
    private const string ValidQueries = """[{"kind":"entityList","resourceGroup":"rg","healthModel":"hm-a"}]""";

    [Theory]
    [InlineData(null, null)]
    [InlineData("return 1;", ValidQueries)]
    [InlineData("return 1;", " ")]
    [InlineData(" ", ValidQueries)]
    [InlineData(" ", null)]
    [InlineData(null, " ")]
    public async Task ExecuteAsync_RequiresExactlyOneOfCodeAndQueries(string? code, string? queries)
    {
        var handler = new HealthModelReadCodeHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("Validation must complete before Azure I/O."));
        var command = CreateCommand(handler);

        var args = new List<string> { "--subscription", Subscription };
        if (code is not null)
        {
            args.Add("--code");
            args.Add(code);
        }

        if (queries is not null)
        {
            args.Add("--queries");
            args.Add(queries);
        }

        var response = await ExecuteCommandAsync(command, [.. args]);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Contains("exactly one of code or queries", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.StartedRequests);
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_ExposesAllEighteenReadOperations()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var method = request.Method.Method;

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/operations", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    value = new[] { new { name = "Microsoft.CloudHealth/healthmodels/read" } },
                }));
            }

            if (method == "GET" && path.EndsWith("/subscriptions/11111111-1111-1111-1111-111111111111/resourcegroups/rg", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg",
                    name = "rg",
                    type = "Microsoft.Resources/resourceGroups",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    value = new[]
                    {
                        new
                        {
                            id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a",
                            name = "hm-a",
                            type = "Microsoft.CloudHealth/healthmodels",
                            location = "westeurope",
                            properties = new { provisioningState = "Succeeded" },
                        },
                    },
                    nextLink = (string?)null,
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a",
                    name = "hm-a",
                    type = "Microsoft.CloudHealth/healthmodels",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    value = new[]
                    {
                        new
                        {
                            id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-1",
                            name = "entity-1",
                            type = "Microsoft.CloudHealth/healthmodels/entities",
                            properties = new { healthState = "Healthy" },
                        },
                    },
                    nextLink = "https://management.azure.com/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities?api-version=2026-05-01-preview&$skiptoken=e2",
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-1", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-1",
                    name = "entity-1",
                    type = "Microsoft.CloudHealth/healthmodels/entities",
                    properties = new { healthState = "Healthy" },
                }));
            }

            if (method == "POST" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-1/getHistory", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    entityName = "entity-1",
                    history = new[] { new { previousState = "Healthy", newState = "Degraded", occurredAt = "2026-01-01T00:00:00Z", reason = "demo" } },
                    nextMarker = "hist-next",
                }));
            }

            if (method == "POST" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-1/getSignalHistory", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    entityName = "entity-1",
                    signalName = "cpu",
                    history = new[] { new { occurredAt = "2026-01-01T00:00:00Z", healthState = "Healthy", additionalContext = "ok", value = 2.5 } },
                    nextMarker = "signal-next",
                }));
            }

            if (method == "POST" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-1/getSignalRecommendations", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    recommendedSignals = new[] { new { name = "cpu", signalKind = "ResourceMetric" } },
                    recommendedConfigurations = Array.Empty<object>(),
                }));
            }

            if (method == "POST" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-1/getDataAnnotations", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    entityName = "entity-1",
                    annotations = new[]
                    {
                        new
                        {
                            annotationId = "a-1",
                            annotationDetails = new { deployment = "42" },
                            description = "deploy",
                            createdAt = "2026-01-01T00:00:00Z",
                        },
                    },
                    nextMarker = "annotation-next",
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/relationships", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    value = new[]
                    {
                        new
                        {
                            id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/relationships/rel-1",
                            name = "rel-1",
                            type = "Microsoft.CloudHealth/healthmodels/relationships",
                            properties = new { parentEntityName = "entity-1", childEntityName = "entity-2" },
                        },
                    },
                    nextLink = (string?)null,
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/relationships/rel-1", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/relationships/rel-1",
                    name = "rel-1",
                    type = "Microsoft.CloudHealth/healthmodels/relationships",
                    properties = new { parentEntityName = "entity-1", childEntityName = "entity-2" },
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/signalDefinitions", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    value = new[]
                    {
                        new
                        {
                            id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/signalDefinitions/sig-1",
                            name = "sig-1",
                            type = "Microsoft.CloudHealth/healthmodels/signalDefinitions",
                            properties = new { signalKind = "ResourceMetric" },
                        },
                    },
                    nextLink = (string?)null,
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/signalDefinitions/sig-1", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/signalDefinitions/sig-1",
                    name = "sig-1",
                    type = "Microsoft.CloudHealth/healthmodels/signalDefinitions",
                    properties = new { signalKind = "ResourceMetric" },
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/authenticationSettings", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    value = new[]
                    {
                        new
                        {
                            id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/authenticationSettings/auth-1",
                            name = "auth-1",
                            type = "Microsoft.CloudHealth/healthmodels/authenticationSettings",
                            properties = new { authenticationType = "ManagedIdentity" },
                        },
                    },
                    nextLink = (string?)null,
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/authenticationSettings/auth-1", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/authenticationSettings/auth-1",
                    name = "auth-1",
                    type = "Microsoft.CloudHealth/healthmodels/authenticationSettings",
                    properties = new { authenticationType = "ManagedIdentity" },
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/discoveryRules", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    value = new[]
                    {
                        new
                        {
                            id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/discoveryRules/rule-1",
                            name = "rule-1",
                            type = "Microsoft.CloudHealth/healthmodels/discoveryRules",
                            properties = new { displayName = "rule-1" },
                        },
                    },
                    nextLink = (string?)null,
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/discoveryRules/rule-1", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/discoveryRules/rule-1",
                    name = "rule-1",
                    type = "Microsoft.CloudHealth/healthmodels/discoveryRules",
                    properties = new { displayName = "rule-1" },
                }));
            }

            return Task.FromResult(JsonResponse(new
            {
                error = new
                {
                    code = "NotFound",
                    message = $"Unhandled route: {method} {path}",
                },
            }, HttpStatusCode.NotFound));
        });
        var command = CreateCommand(handler);

        const string code = """
            const rg = 'rg';
            const hm = 'hm-a';
            const model = await client.healthModels.get(rg, hm);
            const modelRg = await client.healthModels.listByResourceGroup(rg);
            const modelSub = await client.healthModels.listBySubscription();
            const entity = await client.entities.get(rg, hm, 'entity-1');
            const entities = await client.entities.listByHealthModel(rg, hm, { asOf: '2026-01-01T00:00:00Z' });
            const history = await client.entities.getHistory(rg, hm, 'entity-1', { startTime: '2026-01-01T00:00:00Z', endTime: '2026-01-02T00:00:00Z', top: 2 });
            const signalHistory = await client.entities.getSignalHistory(rg, hm, 'entity-1', { signalName: 'cpu', nextMarker: 'signal/+==', top: 3 });
            const recommendations = await client.entities.getSignalRecommendations(rg, hm, 'entity-1');
            const annotations = await client.entities.getDataAnnotations(rg, hm, 'entity-1', { nextMarker: 'annotation/+==', top: 4 });
            const relationship = await client.relationships.get(rg, hm, 'rel-1');
            const relationships = await client.relationships.listByHealthModel(rg, hm, { asOf: '2026-01-01T00:00:00Z' });
            const signalDefinition = await client.signalDefinitions.get(rg, hm, 'sig-1');
            const signalDefinitions = await client.signalDefinitions.listByHealthModel(rg, hm, { asOf: '2026-01-01T00:00:00Z' });
            const authSetting = await client.authenticationSettings.get(rg, hm, 'auth-1');
            const authSettings = await client.authenticationSettings.listByHealthModel(rg, hm);
            const discoveryRule = await client.discoveryRules.get(rg, hm, 'rule-1');
            const discoveryRules = await client.discoveryRules.listByHealthModel(rg, hm, { asOf: '2026-01-01T00:00:00Z' });
            const operations = await client.operations.list();
            return {
              modelName: model.name,
              modelRgCount: modelRg.value.length,
              modelSubCount: modelSub.value.length,
              entityName: entity.name,
              entityCount: entities.value.length,
              entityCursor: entities.nextLink,
              historyCount: history.history.length,
              historyCursor: history.nextMarker,
              signalHistoryName: signalHistory.signalName,
              recommendationCount: recommendations.recommendedSignals.length,
              annotationCount: annotations.annotations.length,
              relationshipName: relationship.name,
              relationshipCount: relationships.value.length,
              signalDefinitionName: signalDefinition.name,
              signalDefinitionCount: signalDefinitions.value.length,
              authSettingName: authSetting.name,
              authSettingCount: authSettings.value.length,
              discoveryRuleName: discoveryRule.name,
              discoveryRuleCount: discoveryRules.value.length,
              operationName: operations.value[0].name
            };
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.True(
            result.Error is null,
            $"script error: {result.Error}\nrequests: {string.Join(" | ", handler.Requests.Select(r => $"{r.Method} {r.Path}{r.Query} body={r.Body}"))}");
        Assert.Equal(18, result.AzureCalls);
        Assert.Equal("hm-a", result.Result!["modelName"]!.GetValue<string>());
        Assert.Equal(1, result.Result["modelRgCount"]!.GetValue<int>());
        Assert.Equal(1, result.Result["modelSubCount"]!.GetValue<int>());
        Assert.Equal("entity-1", result.Result["entityName"]!.GetValue<string>());
        Assert.Equal(1, result.Result["entityCount"]!.GetValue<int>());
        Assert.Equal("hist-next", result.Result["historyCursor"]!.GetValue<string>());
        Assert.Equal("cpu", result.Result["signalHistoryName"]!.GetValue<string>());
        Assert.Equal(1, result.Result["recommendationCount"]!.GetValue<int>());
        Assert.Equal(1, result.Result["annotationCount"]!.GetValue<int>());
        Assert.Equal("rel-1", result.Result["relationshipName"]!.GetValue<string>());
        Assert.Equal("sig-1", result.Result["signalDefinitionName"]!.GetValue<string>());
        Assert.Equal("auth-1", result.Result["authSettingName"]!.GetValue<string>());
        Assert.Equal("rule-1", result.Result["discoveryRuleName"]!.GetValue<string>());
        Assert.Equal("Microsoft.CloudHealth/healthmodels/read", result.Result["operationName"]!.GetValue<string>());

        Assert.Contains(handler.Requests, request => request.Method == "POST" && request.Path.EndsWith("/getHistory", StringComparison.OrdinalIgnoreCase) && request.Body!.Contains("\"top\":2", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.Method == "POST" && request.Path.EndsWith("/getSignalHistory", StringComparison.OrdinalIgnoreCase) && request.Body!.Contains("signal/+==", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.Method == "POST" && request.Path.EndsWith("/getDataAnnotations", StringComparison.OrdinalIgnoreCase) && request.Body!.Contains("annotation/+==", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_UsesPromiseGet_WithOverlapAndFourReadGate()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler(
            new HashSet<string>(StringComparer.Ordinal) { "hm-a", "hm-b", "hm-c", "hm-d", "hm-e" });
        var command = CreateCommand(handler);

        const string code = """
            const one = client.healthModels.get('rg', 'hm-a');
            const two = client.healthModels.get('rg', 'hm-b');
            const three = client.healthModels.get('rg', 'hm-c');
            const four = client.healthModels.get('rg', 'hm-d');
            const five = client.healthModels.get('rg', 'hm-e');
            const values = await Promise.all([one, two, three, four, five]);
            return {
              promiseLike: typeof one.then,
              names: values.map(v => v.name),
              hasEntityWrite: typeof client.entities?.createOrUpdate
            };
            """;

        var execution = ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        await handler.WaitForStartedAsync(4, TestContext.Current.CancellationToken);
        Assert.Equal(4, handler.MaxActiveRequests);
        Assert.Equal(4, handler.StartedRequests);
        Assert.DoesNotContain(handler.Requests, request => request.Model == "hm-e");

        handler.Release(1);
        await handler.WaitForStartedAsync(5, TestContext.Current.CancellationToken);
        Assert.Equal(4, handler.MaxActiveRequests);

        handler.Release(4);
        var response = await execution;

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.NotNull(result);
        Assert.Null(result.Error);
        Assert.Equal(5, result.AzureCalls);
        Assert.Equal("function", result.Result!["promiseLike"]!.GetValue<string>());
        Assert.Equal("undefined", result.Result!["hasEntityWrite"]!.GetValue<string>());
        Assert.Equal(
            ["hm-a", "hm-b", "hm-c", "hm-d", "hm-e"],
            result.Result["names"]!.AsArray().Select(value => value!.GetValue<string>()));

        Assert.All(handler.Requests, request => Assert.Equal("GET", request.Method));
        Assert.All(
            handler.Requests,
            request => Assert.Contains(
                "/providers/Microsoft.CloudHealth/healthmodels/",
                request.Path,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_RejectsWithStructuredErrorFields()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/hm-ok", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-ok",
                    name = "hm-ok",
                    type = "Microsoft.CloudHealth/healthmodels",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (path.EndsWith("/hm-forbidden", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    error = new { code = "AuthorizationFailed", message = "forbidden for this model" },
                }, HttpStatusCode.Forbidden));
            }

            return Task.FromResult(JsonResponse(new
            {
                error = new { code = "ModelNotFound", message = "requested model missing" },
            }, HttpStatusCode.NotFound));
        });
        var command = CreateCommand(handler);

        const string code = """
            const settled = await Promise.allSettled([
              client.healthModels.get('rg', 'hm-ok'),
              client.healthModels.get('rg', 'hm-forbidden'),
              client.healthModels.get('rg', 'hm-missing')
            ]);
            return settled.map(item =>
              item.status === 'fulfilled'
                ? { kind: item.status, name: item.value.name }
                : {
                    kind: item.status,
                    operation: item.reason.operation,
                    status: item.reason.status,
                    code: item.reason.code,
                    message: item.reason.message
                  });
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        var settled = result.Result!.AsArray();
        Assert.Equal("fulfilled", settled[0]!["kind"]!.GetValue<string>());
        Assert.Equal("hm-ok", settled[0]!["name"]!.GetValue<string>());
        Assert.Equal("rejected", settled[1]!["kind"]!.GetValue<string>());
        Assert.Equal("healthModels.get", settled[1]!["operation"]!.GetValue<string>());
        Assert.Equal(403, settled[1]!["status"]!.GetValue<int>());
        Assert.Equal("AuthorizationFailed", settled[1]!["code"]!.GetValue<string>());
        Assert.Contains("forbidden for this model", settled[1]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("rejected", settled[2]!["kind"]!.GetValue<string>());
        Assert.Equal(404, settled[2]!["status"]!.GetValue<int>());
        Assert.Equal("ModelNotFound", settled[2]!["code"]!.GetValue<string>());
        Assert.Contains("requested model missing", settled[2]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_PreservesStructuredErrorSpecialCharacters()
    {
        const string expectedCode = "Auth\\Denied\"Line\nOne\u2028Two\u2029Three";
        const string expectedMessage = "forbidden \"quote\" slash\\ line1\nline2\u2028line3\u2029line4";
        var handler = new HealthModelReadCodeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/hm-special", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    error = new { code = expectedCode, message = expectedMessage },
                }, HttpStatusCode.Forbidden));
            }

            return Task.FromResult(JsonResponse(new
            {
                error = new { code = "NotFound", message = $"Unhandled route: {request.Method.Method} {path}" },
            }, HttpStatusCode.NotFound));
        });
        var command = CreateCommand(handler);
        const string code = """
            const settled = await Promise.allSettled([client.healthModels.get('rg', 'hm-special')]);
            const rejection = settled[0];
            return {
              status: rejection.status,
              operation: rejection.reason.operation,
              code: rejection.reason.code,
              message: rejection.reason.message
            };
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Equal("rejected", result.Result!["status"]!.GetValue<string>());
        Assert.Equal("healthModels.get", result.Result["operation"]!.GetValue<string>());
        Assert.Equal(expectedCode, result.Result["code"]!.GetValue<string>());
        var actualMessage = result.Result["message"]!.GetValue<string>();
        Assert.StartsWith(expectedMessage, actualMessage, StringComparison.Ordinal);
        Assert.Contains("HTTP 403", actualMessage, StringComparison.Ordinal);
        Assert.Contains(expectedCode, actualMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_IgnoresUserGlobalsNamedLikeCompletionSentinels()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler(
            new HashSet<string>(StringComparer.Ordinal) { "hm-a" });
        var command = CreateCommand(handler);
        const string code = """
            Object.defineProperty(globalThis, '__ch_state', { value: 'locked', writable: false, configurable: false });
            Object.defineProperty(globalThis, '__ch_value', { value: 'locked', writable: false, configurable: false });
            Object.defineProperty(globalThis, '__ch_error', { value: 'locked', writable: false, configurable: false });
            return await Promise.resolve({ ok: true });
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Null(result.Error);
        Assert.True(result.Result!["ok"]!.GetValue<bool>());
        Assert.Equal(0, result.AzureCalls);
        Assert.Equal(0, handler.StartedRequests);
    }

    #pragma warning disable xUnit1051
    [Fact]
    public async Task ExecuteAsync_CodeMode_PendingWithNoReads_WaitsForCancellation()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler(
            new HashSet<string>(StringComparer.Ordinal) { "hm-a" });
        var command = CreateCommand(handler);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var response = await ExecuteCommandWithCancellationAsync(
            command,
            ["--subscription", Subscription, "--code", "await new Promise(() => {});"],
            cancel.Token);

        Assert.NotEqual(HttpStatusCode.OK, response.Status);
        Assert.Equal(0, handler.StartedRequests);
    }
    #pragma warning restore xUnit1051

    #pragma warning disable xUnit1051
    [Fact]
    public async Task ExecuteAsync_CodeMode_CallerCancellation_StopsQueuedReadsAtFourActive()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "hm-a", "hm-b", "hm-c", "hm-d", "hm-e", "hm-f", "hm-g", "hm-h", "hm-i",
            });
        var command = CreateCommand(handler);

        const string code = """
            await Promise.all([
              client.healthModels.get('rg', 'hm-a'),
              client.healthModels.get('rg', 'hm-b'),
              client.healthModels.get('rg', 'hm-c'),
              client.healthModels.get('rg', 'hm-d'),
              client.healthModels.get('rg', 'hm-e'),
              client.healthModels.get('rg', 'hm-f'),
              client.healthModels.get('rg', 'hm-g'),
              client.healthModels.get('rg', 'hm-h'),
              client.healthModels.get('rg', 'hm-i')
            ]);
            return 'done';
            """;

        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var execution = ExecuteCommandWithCancellationAsync(command, ["--subscription", Subscription, "--code", code], cancel.Token);
        await handler.WaitForStartedAsync(4, TestContext.Current.CancellationToken);
        cancel.Cancel();
        var response = await execution;

        Assert.NotEqual(HttpStatusCode.OK, response.Status);
        Assert.Equal(4, handler.StartedRequests);
        Assert.Equal(4, handler.MaxActiveRequests);
        Assert.DoesNotContain(handler.Requests, request => request.Model == "hm-i");
    }
    #pragma warning restore xUnit1051

    #pragma warning disable xUnit1051
    [Fact]
    public async Task ExecuteAsync_CodeMode_CallerCancellation_DoesNotDispatchQueuedReads_UnderStress()
    {
        const int attempts = 80;
        const string code = """
            await Promise.all([
              client.healthModels.get('rg', 'hm-a'),
              client.healthModels.get('rg', 'hm-b'),
              client.healthModels.get('rg', 'hm-c'),
              client.healthModels.get('rg', 'hm-d'),
              client.healthModels.get('rg', 'hm-e'),
              client.healthModels.get('rg', 'hm-f'),
              client.healthModels.get('rg', 'hm-g'),
              client.healthModels.get('rg', 'hm-h'),
              client.healthModels.get('rg', 'hm-i')
            ]);
            return 'done';
            """;

        var leakedAttempts = new List<string>();

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var firstFourStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var responderStarts = 0;
            var responderStartsWithCancelledToken = 0;
            var handler = new HealthModelReadCodeHttpMessageHandler(async (request, cancellationToken) =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                if (cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Increment(ref responderStartsWithCancelledToken);
                }

                var start = Interlocked.Increment(ref responderStarts);
                if (start == 4)
                {
                    firstFourStarted.TrySetResult();
                }

                if (start <= 4)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                var model = ReadSegmentValue(path, "healthmodels") ?? "unknown";
                return JsonResponse(new
                {
                    id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/{model}",
                    name = model,
                    type = "Microsoft.CloudHealth/healthmodels",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                });
            });

            var command = CreateCommand(handler);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var execution = ExecuteCommandWithCancellationAsync(
                command,
                ["--subscription", Subscription, "--code", code],
                cancel.Token);

            await firstFourStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            cancel.Cancel();
            var response = await execution;

            Assert.NotEqual(HttpStatusCode.OK, response.Status);
            Assert.Equal(4, handler.MaxActiveRequests);

            if (handler.StartedRequests > 4 || responderStartsWithCancelledToken > 0)
            {
                leakedAttempts.Add(
                    $"attempt={attempt}, started={handler.StartedRequests}, cancelled-dispatch={responderStartsWithCancelledToken}");
            }
        }

        Assert.True(
            leakedAttempts.Count == 0,
            $"Queued reads dispatched after cancellation: {string.Join(" | ", leakedAttempts)}");
    }
    #pragma warning restore xUnit1051

    [Fact]
    public async Task ExecuteAsync_CodeMode_CleansUpReadsWithoutAwait_AndFreezesAzureCallCount()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "hm-a", "hm-b", "hm-c", "hm-d", "hm-e", "hm-f", "hm-g", "hm-h", "hm-i",
            });
        var command = CreateCommand(handler);

        const string code = """
            client.healthModels.get('rg', 'hm-a');
            client.healthModels.get('rg', 'hm-b');
            client.healthModels.get('rg', 'hm-c');
            client.healthModels.get('rg', 'hm-d');
            client.healthModels.get('rg', 'hm-e');
            client.healthModels.get('rg', 'hm-f');
            client.healthModels.get('rg', 'hm-g');
            client.healthModels.get('rg', 'hm-h');
            client.healthModels.get('rg', 'hm-i');
            return { done: true };
            """;

        var execution = ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);
        await handler.WaitForStartedAsync(4, TestContext.Current.CancellationToken);
        var response = await execution;

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Null(result.Error);
        Assert.Equal(0, result.AzureCalls);
        Assert.Equal(4, handler.MaxActiveRequests);
        var startedAtCompletion = handler.StartedRequests;
        Assert.True(startedAtCompletion >= 4);
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        Assert.Equal(startedAtCompletion, handler.StartedRequests);
        Assert.Equal(0, handler.ActiveRequests);
        Assert.True(handler.CancelledRequests >= startedAtCompletion);
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_CompletedResultSurvivesRejectedReadWithoutAwait()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/hm-ok", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(new
                {
                    id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-ok",
                    name = "hm-ok",
                    type = "Microsoft.CloudHealth/healthmodels",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                });
            }

            if (path.EndsWith("/hm-fail", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(40), TestContext.Current.CancellationToken);
                return JsonResponse(new
                {
                    error = new { code = "AuthorizationFailed", message = "forbidden for this model" },
                }, HttpStatusCode.Forbidden);
            }

            return JsonResponse(new
            {
                error = new { code = "NotFound", message = $"Unhandled route: {request.Method.Method} {path}" },
            }, HttpStatusCode.NotFound);
        });
        var command = CreateCommand(handler);
        const string code = """
            client.healthModels.get('rg', 'hm-fail').catch(() => {
              while (true) { Math.imul(123456, 789012); }
            });
            const model = await client.healthModels.get('rg', 'hm-ok');
            return { name: model.name };
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Null(result.Error);
        Assert.Equal("hm-ok", result.Result!["name"]!.GetValue<string>());
        Assert.Equal(1, result.AzureCalls);
        Assert.Equal(2, handler.StartedRequests);
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        Assert.Equal(0, handler.ActiveRequests);
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_RejectsForeignContinuationBeforeHttpDispatch()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler((request, _) =>
        {
            return Task.FromResult(JsonResponse(new { value = Array.Empty<object>(), nextLink = (string?)null }));
        });
        var command = CreateCommand(handler);
        const string code = """
            try {
              await client.entities.listByHealthModel(
                'rg',
                'hm-a',
                { cursor: 'https://evil.example/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities?api-version=2026-05-01-preview&$skiptoken=e2' });
              return 'unexpected';
            } catch (error) {
              return { operation: error.operation, message: error.message, status: error.status };
            }
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Contains("cursor", result.Result!["message"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("entities.listByHealthModel", result.Result["operation"]!.GetValue<string>());
        Assert.Equal(0, handler.StartedRequests);
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_AllowsValidContinuationAndPreservesMarkerBytes()
    {
        const string cursor = "https://management.azure.com/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities?api-version=2026-05-01-preview&$skiptoken=11111111-1111-1111-1111-111111111111";
        var handler = new HealthModelReadCodeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/subscriptions/11111111-1111-1111-1111-111111111111/resourcegroups/rg", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg",
                    name = "rg",
                    type = "Microsoft.Resources/resourceGroups",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a",
                    name = "hm-a",
                    type = "Microsoft.CloudHealth/healthmodels",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    value = new[]
                    {
                        new
                        {
                            id = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-2",
                            name = "entity-2",
                            type = "Microsoft.CloudHealth/healthmodels/entities",
                            properties = new { healthState = "Degraded" },
                        },
                    },
                    nextLink = (string?)null,
                }));
            }

            return Task.FromResult(JsonResponse(new
            {
                error = new { code = "NotFound", message = $"Unhandled route: {request.Method.Method} {path}" },
            }, HttpStatusCode.NotFound));
        });
        var command = CreateCommand(handler);
        var code = $$"""
            const page = await client.entities.listByHealthModel('rg', 'hm-a', { cursor: '{{cursor}}' });
            return { count: page.value.length, nextLink: page.nextLink ?? null };
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Null(result.Error);
        Assert.Equal(1, result.Result!["count"]!.GetValue<int>());
        if (result.Result is JsonObject objectResult && objectResult.TryGetPropertyValue("nextLink", out var nextLink))
        {
            Assert.True(
                nextLink is null ||
                nextLink.GetValue<JsonElement>().ValueKind == JsonValueKind.Null);
        }
        var sent = Assert.Single(
            handler.Requests,
            request => request.Method == "GET" &&
                request.Path.EndsWith(
                    "/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities",
                    StringComparison.OrdinalIgnoreCase));
        Assert.Equal(cursor, $"https://management.azure.com{sent.Path}{sent.Query}");
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_RejectsCursorCombinedWithAsOfWindow()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler((request, _) =>
        {
            return Task.FromResult(JsonResponse(new { value = Array.Empty<object>(), nextLink = (string?)null }));
        });
        var command = CreateCommand(handler);
        const string code = """
            try {
              await client.relationships.listByHealthModel(
                'rg',
                'hm-a',
                {
                  cursor: 'https://management.azure.com/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/relationships?api-version=2026-05-01-preview&$skiptoken=rel2',
                  asOf: '2026-01-01T00:00:00Z'
                });
              return 'unexpected';
            } catch (error) {
              return { message: error.message };
            }
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Contains("cursor", result.Result!["message"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("asOf", result.Result["message"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.StartedRequests);
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_NinePromiseReads_RespectsFourGateAndInputOrder()
    {
        var models = new[] { "hm-a", "hm-b", "hm-c", "hm-d", "hm-e", "hm-f", "hm-g", "hm-h", "hm-i" };
        var releases = models.ToDictionary(
            model => model,
            _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            StringComparer.Ordinal);
        var completionOrder = new ConcurrentQueue<string>();

        var handler = new HealthModelReadCodeHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var model = ReadSegmentValue(path, "healthmodels");
            if (request.Method != HttpMethod.Get || model is null || !releases.TryGetValue(model, out var gate))
            {
                return JsonResponse(new
                {
                    error = new { code = "NotFound", message = $"Unhandled route: {request.Method.Method} {path}" },
                }, HttpStatusCode.NotFound);
            }

            await gate.Task.WaitAsync(cancellationToken);
            completionOrder.Enqueue(model);
            return JsonResponse(new
            {
                id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/{model}",
                name = model,
                type = "Microsoft.CloudHealth/healthmodels",
                location = "westeurope",
                properties = new { provisioningState = "Succeeded" },
            });
        });

        var command = CreateCommand(handler);
        const string code = """
            const names = ['hm-a', 'hm-b', 'hm-c', 'hm-d', 'hm-e', 'hm-f', 'hm-g', 'hm-h', 'hm-i'];
            const values = await Promise.all(names.map(name => client.healthModels.get('rg', name)));
            return values.map(value => value.name);
            """;

        var execution = ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        await handler.WaitForStartedAsync(4, TestContext.Current.CancellationToken);
        Assert.Equal(4, handler.MaxActiveRequests);
        Assert.Equal(4, handler.StartedRequests);
        Assert.DoesNotContain(handler.Requests, request => request.Model == "hm-e");

        releases["hm-d"].SetResult(true);
        await WaitForCompletionCountAsync(completionOrder, 1, TestContext.Current.CancellationToken);
        releases["hm-c"].SetResult(true);
        await WaitForCompletionCountAsync(completionOrder, 2, TestContext.Current.CancellationToken);
        releases["hm-b"].SetResult(true);
        await WaitForCompletionCountAsync(completionOrder, 3, TestContext.Current.CancellationToken);
        releases["hm-a"].SetResult(true);
        await WaitForCompletionCountAsync(completionOrder, 4, TestContext.Current.CancellationToken);
        releases["hm-e"].SetResult(true);
        releases["hm-f"].SetResult(true);
        releases["hm-g"].SetResult(true);
        releases["hm-h"].SetResult(true);
        releases["hm-i"].SetResult(true);

        var response = await execution;

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Null(result.Error);
        Assert.Equal(9, result.AzureCalls);
        Assert.Equal(models, result.Result!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Equal(9, handler.StartedRequests);
        Assert.Equal(4, handler.MaxActiveRequests);
        Assert.Equal(["hm-d", "hm-c", "hm-b", "hm-a"], completionOrder.Take(4));
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_DependencyDiscovery_DrivesFollowUpReadsWithUnevenHistories()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var method = request.Method.Method;

            if (method == "GET" && path.EndsWith($"/subscriptions/{Subscription}/resourcegroups/rg", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = $"/subscriptions/{Subscription}/resourceGroups/rg",
                    name = "rg",
                    type = "Microsoft.Resources/resourceGroups",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a",
                    name = "hm-a",
                    type = "Microsoft.CloudHealth/healthmodels",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    value = new[]
                    {
                        new
                        {
                            id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-a",
                            name = "entity-a",
                            type = "Microsoft.CloudHealth/healthmodels/entities",
                            properties = new { healthState = "Healthy" },
                        },
                        new
                        {
                            id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-b",
                            name = "entity-b",
                            type = "Microsoft.CloudHealth/healthmodels/entities",
                            properties = new { healthState = "Degraded" },
                        },
                        new
                        {
                            id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-c",
                            name = "entity-c",
                            type = "Microsoft.CloudHealth/healthmodels/entities",
                            properties = new { healthState = "Healthy" },
                        },
                        new
                        {
                            id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-d",
                            name = "entity-d",
                            type = "Microsoft.CloudHealth/healthmodels/entities",
                            properties = new { healthState = "Degraded" },
                        },
                        new
                        {
                            id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-e",
                            name = "entity-e",
                            type = "Microsoft.CloudHealth/healthmodels/entities",
                            properties = new { healthState = "Unhealthy" },
                        },
                    },
                    nextLink = (string?)null,
                }));
            }

            if (method == "POST" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-b/getHistory", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    entityName = "entity-b",
                    history = new[]
                    {
                        new { previousState = "Healthy", newState = "Degraded", occurredAt = "2026-01-01T00:00:00Z", reason = "first" },
                        new { previousState = "Degraded", newState = "Unhealthy", occurredAt = "2026-01-02T00:00:00Z", reason = "second" },
                        new { previousState = "Unhealthy", newState = "Degraded", occurredAt = "2026-01-03T00:00:00Z", reason = "third" },
                    },
                    nextMarker = (string?)null,
                }));
            }

            if (method == "POST" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-d/getHistory", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    entityName = "entity-d",
                    history = new[]
                    {
                        new { previousState = "Healthy", newState = "Degraded", occurredAt = "2026-01-01T00:00:00Z", reason = "single" },
                    },
                    nextMarker = (string?)null,
                }));
            }

            return Task.FromResult(JsonResponse(new
            {
                error = new { code = "NotFound", message = $"Unhandled route: {method} {path}" },
            }, HttpStatusCode.NotFound));
        });

        var command = CreateCommand(handler);
        const string code = """
            const entities = await client.entities.listByHealthModel('rg', 'hm-a');
            const counts = entities.value.reduce(
              (acc, entity) => {
                const state = entity.properties?.healthState ?? 'Unknown';
                acc[state] = (acc[state] ?? 0) + 1;
                return acc;
              },
              { Healthy: 0, Degraded: 0, Unhealthy: 0 });
            const targets = entities.value
              .filter(entity => entity.properties?.healthState === 'Degraded')
              .map(entity => entity.name);
            const histories = await Promise.all(
              targets.map(name => client.entities.getHistory('rg', 'hm-a', name, { top: 5 })));
            return {
              counts,
              targets,
              historyLengths: histories.map(history => history.history.length),
              totalHistory: histories.reduce((sum, history) => sum + history.history.length, 0)
            };
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Null(result.Error);
        Assert.Equal(3, result.AzureCalls);
        Assert.Equal(2, result.Result!["counts"]!["Healthy"]!.GetValue<int>());
        Assert.Equal(2, result.Result["counts"]!["Degraded"]!.GetValue<int>());
        Assert.Equal(1, result.Result["counts"]!["Unhealthy"]!.GetValue<int>());
        Assert.Equal(["entity-b", "entity-d"], result.Result["targets"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Equal([3, 1], result.Result["historyLengths"]!.AsArray().Select(node => node!.GetValue<int>()));
        Assert.Equal(4, result.Result["totalHistory"]!.GetValue<int>());

        var requests = handler.Requests;
        var entityListIndex = Array.FindIndex(requests.ToArray(), request => request.Path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities", StringComparison.OrdinalIgnoreCase));
        var firstHistoryIndex = Array.FindIndex(requests.ToArray(), request => request.Path.EndsWith("/getHistory", StringComparison.OrdinalIgnoreCase));
        Assert.True(entityListIndex >= 0);
        Assert.True(firstHistoryIndex > entityListIndex);
        Assert.DoesNotContain(requests, request => request.Path.Contains("/entities/entity-a/getHistory", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(requests, request => request.Path.Contains("/entities/entity-e/getHistory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_UncaughtPromiseAllRejection_ReportsScriptErrorAndCompletedCallCount()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (path.EndsWith("/hm-ok", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(new
                {
                    id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-ok",
                    name = "hm-ok",
                    type = "Microsoft.CloudHealth/healthmodels",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                });
            }

            if (path.EndsWith("/hm-forbidden", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken);
                return JsonResponse(new
                {
                    error = new { code = "AuthorizationFailed", message = "forbidden for this model" },
                }, HttpStatusCode.Forbidden);
            }

            return JsonResponse(new
            {
                error = new { code = "NotFound", message = $"Unhandled route: {request.Method.Method} {path}" },
            }, HttpStatusCode.NotFound);
        });

        var command = CreateCommand(handler);
        const string code = """
            console.log('before-batch');
            await Promise.all([
              client.healthModels.get('rg', 'hm-ok'),
              client.healthModels.get('rg', 'hm-forbidden')
            ]);
            return { done: true };
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.NotNull(result.Error);
        Assert.Contains("forbidden for this model", result.Error, StringComparison.Ordinal);
        Assert.Contains("HTTP 403", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AuthorizationFailed", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization:", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, result.AzureCalls);
        Assert.Contains(result.Logs, log => log.Contains("before-batch", StringComparison.Ordinal));
    }

    #pragma warning disable xUnit1051
    [Fact]
    public async Task ExecuteAsync_CodeMode_CallerCancellation_CpuLoopIsCancelledWithoutAzureCalls()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler(
            new HashSet<string>(StringComparer.Ordinal) { "hm-a" });
        var command = CreateCommand(handler);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var execution = ExecuteCommandWithCancellationAsync(
            command,
            ["--subscription", Subscription, "--code", "const payload = 'x'.repeat(20000); while (true) { JSON.stringify(payload); }"],
            cancellation.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var response = await execution;

        Assert.NotEqual(HttpStatusCode.OK, response.Status);
        Assert.Equal(0, handler.StartedRequests);
    }
    #pragma warning restore xUnit1051

    [Fact]
    public async Task ExecuteAsync_CodeMode_NeverSettlingPromise_HitsThirtySecondExecutionLimit()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler(
            new HashSet<string>(StringComparer.Ordinal) { "hm-a" });
        var command = CreateCommand(handler);

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", "await new Promise(() => {});");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.NotNull(result.Error);
        Assert.Contains("30s execution limit", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.AzureCalls);
        Assert.Equal(0, handler.StartedRequests);
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_HeldHttpRead_HitsThirtySecondExecutionLimit()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler(
            new HashSet<string>(StringComparer.Ordinal) { "hm-a" });
        var command = CreateCommand(handler);

        var response = await ExecuteCommandAsync(
            command,
            "--subscription",
            Subscription,
            "--code",
            "await client.healthModels.get('rg', 'hm-a');");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.NotNull(result.Error);
        Assert.Contains("30s execution limit", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.AzureCalls);
        Assert.Equal(1, handler.StartedRequests);
        Assert.True(handler.CancelledRequests >= 1);
        Assert.Equal(0, handler.ActiveRequests);
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_CpuLoop_HitsThirtySecondExecutionLimit()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler(
            new HashSet<string>(StringComparer.Ordinal) { "hm-a" });
        var command = CreateCommand(handler);

        var response = await ExecuteCommandAsync(
            command,
            "--subscription",
            Subscription,
            "--code",
            "const payload = 'x'.repeat(100000); const spin = () => Promise.resolve().then(() => JSON.stringify(payload)).then(spin); await spin();");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.NotNull(result.Error);
        Assert.Contains("30s execution limit", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_PaginatesThreeEntityPages_AndPreservesContinuationEncoding()
    {
        const string cursor1 = "https://management.azure.com/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities?api-version=2026-05-01-preview&$skiptoken=token-1%2B%2F%3D";
        const string cursor2 = "https://management.azure.com/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities?api-version=2026-05-01-preview&$skiptoken=token-2%2B%2F%3D";

        var handler = new HealthModelReadCodeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var method = request.Method.Method;
            var query = request.RequestUri?.Query ?? string.Empty;

            if (method == "GET" && path.EndsWith($"/subscriptions/{Subscription}/resourcegroups/rg", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = $"/subscriptions/{Subscription}/resourceGroups/rg",
                    name = "rg",
                    type = "Microsoft.Resources/resourceGroups",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a",
                    name = "hm-a",
                    type = "Microsoft.CloudHealth/healthmodels",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (method == "GET" && path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities", StringComparison.OrdinalIgnoreCase))
            {
                if (query.Contains("$skiptoken=token-1%2B%2F%3D", StringComparison.Ordinal))
                {
                    return Task.FromResult(JsonResponse(new
                    {
                        value = Array.Empty<object>(),
                        nextLink = cursor2,
                    }));
                }

                if (query.Contains("$skiptoken=token-2%2B%2F%3D", StringComparison.Ordinal))
                {
                    return Task.FromResult(JsonResponse(new
                    {
                        value = new[]
                        {
                            new
                            {
                                id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-3",
                                name = "entity-3",
                                type = "Microsoft.CloudHealth/healthmodels/entities",
                                properties = new { healthState = "Healthy" },
                            },
                            new
                            {
                                id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-4",
                                name = "entity-4",
                                type = "Microsoft.CloudHealth/healthmodels/entities",
                                properties = new { healthState = "Healthy" },
                            },
                            new
                            {
                                id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-5",
                                name = "entity-5",
                                type = "Microsoft.CloudHealth/healthmodels/entities",
                                properties = new { healthState = "Healthy" },
                            },
                        },
                        nextLink = (string?)null,
                    }));
                }

                return Task.FromResult(JsonResponse(new
                {
                    value = new[]
                    {
                        new
                        {
                            id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-1",
                            name = "entity-1",
                            type = "Microsoft.CloudHealth/healthmodels/entities",
                            properties = new { healthState = "Healthy" },
                        },
                        new
                        {
                            id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-2",
                            name = "entity-2",
                            type = "Microsoft.CloudHealth/healthmodels/entities",
                            properties = new { healthState = "Healthy" },
                        },
                    },
                    nextLink = cursor1,
                }));
            }

            return Task.FromResult(JsonResponse(new
            {
                error = new { code = "NotFound", message = $"Unhandled route: {method} {path}" },
            }, HttpStatusCode.NotFound));
        });

        var command = CreateCommand(handler);
        const string code = """
            const first = await client.entities.listByHealthModel('rg', 'hm-a');
            const second = await client.entities.listByHealthModel('rg', 'hm-a', { cursor: first.nextLink });
            const third = await client.entities.listByHealthModel('rg', 'hm-a', { cursor: second.nextLink });
            return {
              counts: [first.value.length, second.value.length, third.value.length],
              firstCursor: first.nextLink,
              secondCursor: second.nextLink,
              thirdNames: third.value.map(entity => entity.name)
            };
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Null(result.Error);
        Assert.Equal([2, 0, 3], result.Result!["counts"]!.AsArray().Select(node => node!.GetValue<int>()));
        Assert.Equal(cursor1, result.Result["firstCursor"]!.GetValue<string>());
        Assert.Equal(cursor2, result.Result["secondCursor"]!.GetValue<string>());
        Assert.Equal(["entity-3", "entity-4", "entity-5"], result.Result["thirdNames"]!.AsArray().Select(node => node!.GetValue<string>()));

        var entityRequests = handler.Requests
            .Where(request => request.Method == "GET" && request.Path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(3, entityRequests.Length);
        Assert.Contains("$skiptoken=token-1%2B%2F%3D", entityRequests[1].Query, StringComparison.Ordinal);
        Assert.Contains("$skiptoken=token-2%2B%2F%3D", entityRequests[2].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_RejectsUnchangedContinuationMarker()
    {
        const string cursor = "https://management.azure.com/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities?api-version=2026-05-01-preview&$skiptoken=marker-same";
        var handler = new HealthModelReadCodeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (request.Method == HttpMethod.Get &&
                path.EndsWith($"/subscriptions/{Subscription}/resourcegroups/rg", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = $"/subscriptions/{Subscription}/resourceGroups/rg",
                    name = "rg",
                    type = "Microsoft.Resources/resourceGroups",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a",
                    name = "hm-a",
                    type = "Microsoft.CloudHealth/healthmodels",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    value = new[]
                    {
                        new
                        {
                            id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-2",
                            name = "entity-2",
                            type = "Microsoft.CloudHealth/healthmodels/entities",
                            properties = new { healthState = "Healthy" },
                        },
                    },
                    nextLink = cursor,
                }));
            }

            return Task.FromResult(JsonResponse(new
            {
                error = new { code = "NotFound", message = $"Unhandled route: {request.Method.Method} {path}" },
            }, HttpStatusCode.NotFound));
        });
        var command = CreateCommand(handler);
        var code = $$"""
            try {
              await client.entities.listByHealthModel('rg', 'hm-a', { cursor: '{{cursor}}' });
              return 'unexpected';
            } catch (error) {
              return { operation: error.operation, message: error.message };
            }
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Equal("entities.listByHealthModel", result.Result!["operation"]!.GetValue<string>());
        Assert.Contains("did not advance", result.Result["message"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://evil.example/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities?api-version=2026-05-01-preview&$skiptoken=e2", "configured ARM endpoint")]
    [InlineData("http://management.azure.com/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities?api-version=2026-05-01-preview&$skiptoken=e2", "must use https")]
    [InlineData("https://user@management.azure.com/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities?api-version=2026-05-01-preview&$skiptoken=e2", "cannot include user info")]
    [InlineData("https://management.azure.com/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities?api-version=2026-05-01-preview&$skiptoken=e2", "same collection")]
    [InlineData("https://management.azure.com/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-b/entities?api-version=2026-05-01-preview&$skiptoken=e2", "same collection")]
    [InlineData("https://management.azure.com/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities%2Fextra?api-version=2026-05-01-preview&$skiptoken=e2", "invalid path")]
    [InlineData("https://management.azure.com/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities?api-version=2026-04-01-preview&$skiptoken=e2", "api-version=2026-05-01-preview")]
    public async Task ExecuteAsync_CodeMode_RejectsInvalidContinuationMatrixBeforeDispatch(string cursor, string expectedMessageFragment)
    {
        var handler = new HealthModelReadCodeHttpMessageHandler((request, _) =>
            Task.FromResult(JsonResponse(new { value = Array.Empty<object>(), nextLink = (string?)null })));
        var command = CreateCommand(handler);
        var code = $$"""
            try {
              await client.entities.listByHealthModel('rg', 'hm-a', { cursor: '{{cursor}}' });
              return 'unexpected';
            } catch (error) {
              return { operation: error.operation, message: error.message };
            }
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Equal("entities.listByHealthModel", result.Result!["operation"]!.GetValue<string>());
        Assert.Contains(expectedMessageFragment, result.Result["message"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.StartedRequests);
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_AllowsValidRelativeContinuation()
    {
        const string cursor = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities?api-version=2026-05-01-preview&$skiptoken=relative-1%2B%2F%3D";
        var handler = new HealthModelReadCodeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Get &&
                path.EndsWith($"/subscriptions/{Subscription}/resourcegroups/rg", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = $"/subscriptions/{Subscription}/resourceGroups/rg",
                    name = "rg",
                    type = "Microsoft.Resources/resourceGroups",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a",
                    name = "hm-a",
                    type = "Microsoft.CloudHealth/healthmodels",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (request.Method == HttpMethod.Get &&
                path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    value = new[]
                    {
                        new
                        {
                            id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities/entity-1",
                            name = "entity-1",
                            type = "Microsoft.CloudHealth/healthmodels/entities",
                            properties = new { healthState = "Healthy" },
                        },
                    },
                    nextLink = (string?)null,
                }));
            }

            return Task.FromResult(JsonResponse(new
            {
                error = new { code = "NotFound", message = $"Unhandled route: {request.Method.Method} {path}" },
            }, HttpStatusCode.NotFound));
        });
        var command = CreateCommand(handler);
        var code = $$"""
            const page = await client.entities.listByHealthModel('rg', 'hm-a', { cursor: '{{cursor}}' });
            return { count: page.value.length };
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Null(result.Error);
        Assert.Equal(1, result.Result!["count"]!.GetValue<int>());
        var entityRequest = Assert.Single(
            handler.Requests,
            request => request.Method == "GET" &&
                request.Path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-a/entities", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(cursor, $"{entityRequest.Path}{entityRequest.Query}");
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_ReadOnlySandbox_DeniesWriteCapabilitiesAndAllowsReadPosts()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/getHistory", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new { entityName = "entity-1", history = new[] { new { previousState = "Healthy", newState = "Degraded", occurredAt = "2026-01-01T00:00:00Z", reason = "a" } }, nextMarker = (string?)null }));
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/getSignalHistory", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new { entityName = "entity-1", signalName = "cpu", history = new[] { new { occurredAt = "2026-01-01T00:00:00Z", healthState = "Healthy", additionalContext = "ok", value = 1.0 } }, nextMarker = (string?)null }));
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/getSignalRecommendations", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new { recommendedSignals = new[] { new { name = "cpu" } }, recommendedConfigurations = Array.Empty<object>() }));
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/getDataAnnotations", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new { entityName = "entity-1", annotations = new[] { new { annotationId = "a-1", annotationDetails = new { deployment = "42" }, description = "deploy", createdAt = "2026-01-01T00:00:00Z" } }, nextMarker = (string?)null }));
            }

            return Task.FromResult(JsonResponse(new
            {
                error = new { code = "NotFound", message = $"Unhandled route: {request.Method.Method} {path}" },
            }, HttpStatusCode.NotFound));
        });
        var command = CreateCommand(handler);
        const string code = """
            let writeError = null;
            try {
              await client.entities.createOrUpdate('rg', 'hm-a', 'entity-1', { properties: {} });
            } catch (error) {
              writeError = String(error);
            }

            const history = await client.entities.getHistory('rg', 'hm-a', 'entity-1', { top: 1 });
            const signalHistory = await client.entities.getSignalHistory('rg', 'hm-a', 'entity-1', { signalName: 'cpu', top: 1 });
            const recommendations = await client.entities.getSignalRecommendations('rg', 'hm-a', 'entity-1');
            const annotations = await client.entities.getDataAnnotations('rg', 'hm-a', 'entity-1', { top: 1 });

            return {
              createOrUpdateType: typeof client.entities.createOrUpdate,
              addDataAnnotationType: typeof client.entities.addDataAnnotation,
              ingestHealthReportType: typeof client.entities.ingestHealthReport,
              fetchType: typeof fetch,
              requireType: typeof require,
              processType: typeof process,
              systemType: typeof System,
              writeError,
              postCounts: [
                history.history.length,
                signalHistory.history.length,
                recommendations.recommendedSignals.length,
                annotations.annotations.length
              ]
            };
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Null(result.Error);
        Assert.Equal("undefined", result.Result!["createOrUpdateType"]!.GetValue<string>());
        Assert.Equal("undefined", result.Result["addDataAnnotationType"]!.GetValue<string>());
        Assert.Equal("undefined", result.Result["ingestHealthReportType"]!.GetValue<string>());
        Assert.Equal("undefined", result.Result["fetchType"]!.GetValue<string>());
        Assert.Equal("undefined", result.Result["requireType"]!.GetValue<string>());
        Assert.Equal("undefined", result.Result["processType"]!.GetValue<string>());
        Assert.Equal("undefined", result.Result["systemType"]!.GetValue<string>());
        Assert.Contains("not a function", result.Result["writeError"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal([1, 1, 1, 1], result.Result["postCounts"]!.AsArray().Select(node => node!.GetValue<int>()));
        Assert.Equal(
            4,
            handler.Requests.Count(request =>
                request.Method == "POST" &&
                (request.Path.EndsWith("/getHistory", StringComparison.OrdinalIgnoreCase) ||
                 request.Path.EndsWith("/getSignalHistory", StringComparison.OrdinalIgnoreCase) ||
                 request.Path.EndsWith("/getSignalRecommendations", StringComparison.OrdinalIgnoreCase) ||
                 request.Path.EndsWith("/getDataAnnotations", StringComparison.OrdinalIgnoreCase))));
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_ConcurrentRequests_IsolateDeniedScopeFromSuccessfulScope()
    {
        const string subscriptionA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        const string subscriptionB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

        var handler = new HealthModelReadCodeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (path.Contains($"/subscriptions/{subscriptionA}/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-ok", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = $"/subscriptions/{subscriptionA}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-ok",
                    name = "hm-ok",
                    type = "Microsoft.CloudHealth/healthmodels",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                }));
            }

            if (path.Contains($"/subscriptions/{subscriptionB}/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-denied", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse(new
                {
                    error = new { code = "AuthorizationFailed", message = "denied for this subscription" },
                }, HttpStatusCode.Forbidden));
            }

            return Task.FromResult(JsonResponse(new
            {
                error = new { code = "NotFound", message = $"Unhandled route: {request.Method.Method} {path}" },
            }, HttpStatusCode.NotFound));
        });
        var command = CreateCommand(handler);

        const string successCode = """
            const model = await client.healthModels.get('rg', 'hm-ok');
            return { name: model.name };
            """;
        const string deniedCode = """
            await client.healthModels.get('rg', 'hm-denied');
            return { done: true };
            """;

        var successTask = ExecuteCommandAsync(
            command,
            "--subscription",
            subscriptionA,
            "--tenant",
            "tenant-a",
            "--code",
            successCode);
        var deniedTask = ExecuteCommandAsync(
            command,
            "--subscription",
            subscriptionB,
            "--tenant",
            "tenant-b",
            "--code",
            deniedCode);

        await Task.WhenAll(successTask, deniedTask);

        var success = Deserialize(await successTask, MonitorJsonContext.Default.HealthModelScriptResult);
        var denied = Deserialize(await deniedTask, MonitorJsonContext.Default.HealthModelScriptResult);

        Assert.Null(success.Error);
        Assert.Equal("hm-ok", success.Result!["name"]!.GetValue<string>());
        Assert.Equal(1, success.AzureCalls);

        Assert.NotNull(denied.Error);
        Assert.Contains("HTTP 403", denied.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AuthorizationFailed", denied.Error, StringComparison.Ordinal);
        Assert.Equal(0, denied.AzureCalls);

        Assert.Contains(handler.Requests, request => request.Path.Contains($"/subscriptions/{subscriptionA}/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(handler.Requests, request => request.Path.Contains($"/subscriptions/{subscriptionB}/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path.Contains($"/subscriptions/{subscriptionA}/", StringComparison.OrdinalIgnoreCase) &&
                request.Path.EndsWith("/hm-denied", StringComparison.OrdinalIgnoreCase));
    }

    #pragma warning disable xUnit1051
    [Fact]
    public async Task ExecuteAsync_CodeMode_ConcurrentRequests_IsolateCancellationFromSuccessfulScope()
    {
        const string subscriptionA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        const string subscriptionB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

        var handler = new HealthModelReadCodeHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (path.Contains($"/subscriptions/{subscriptionA}/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-ok", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse(new
                {
                    id = $"/subscriptions/{subscriptionA}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/hm-ok",
                    name = "hm-ok",
                    type = "Microsoft.CloudHealth/healthmodels",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                });
            }

            if (path.Contains($"/subscriptions/{subscriptionB}/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith("/providers/Microsoft.CloudHealth/healthmodels/hm-block", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return JsonResponse(new
            {
                error = new { code = "NotFound", message = $"Unhandled route: {request.Method.Method} {path}" },
            }, HttpStatusCode.NotFound);
        });
        var command = CreateCommand(handler);

        const string successCode = """
            const model = await client.healthModels.get('rg', 'hm-ok');
            return { name: model.name };
            """;
        const string blockedCode = """
            await client.healthModels.get('rg', 'hm-block');
            return { done: true };
            """;

        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var blockedTask = ExecuteCommandWithCancellationAsync(
            command,
            ["--subscription", subscriptionB, "--tenant", "tenant-b", "--code", blockedCode],
            cancel.Token);
        var successTask = ExecuteCommandAsync(
            command,
            "--subscription",
            subscriptionA,
            "--tenant",
            "tenant-a",
            "--code",
            successCode);

        await handler.WaitForStartedAsync(2, TestContext.Current.CancellationToken);
        cancel.Cancel();
        var successResponse = await successTask;
        var blockedResponse = await blockedTask;

        Assert.Equal(HttpStatusCode.OK, successResponse.Status);
        var success = Deserialize(successResponse, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Null(success.Error);
        Assert.Equal("hm-ok", success.Result!["name"]!.GetValue<string>());
        Assert.Equal(1, success.AzureCalls);

        Assert.NotEqual(HttpStatusCode.OK, blockedResponse.Status);
        Assert.True(handler.CancelledRequests >= 1);
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path.Contains($"/subscriptions/{subscriptionA}/", StringComparison.OrdinalIgnoreCase) &&
                request.Path.EndsWith("/hm-block", StringComparison.OrdinalIgnoreCase));
    }
    #pragma warning restore xUnit1051

    [Fact]
    public async Task ExecuteAsync_CodeMode_TruncatesOversizedResults_AndKeepsMarker()
    {
        var handler = new HealthModelReadCodeHttpMessageHandler(
            new HashSet<string>(StringComparer.Ordinal) { "hm-a" });
        var command = CreateCommand(handler);

        var response = await ExecuteCommandAsync(
            command,
            "--subscription",
            Subscription,
            "--code",
            "return { blob: 'x'.repeat(26000) };");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Null(result.Error);
        Assert.True(result.Truncated);
        Assert.Equal(0, result.AzureCalls);
        var payload = result.Result!.GetValue<string>();
        Assert.Contains(HealthModelScriptOutput.TruncationMarker, payload, StringComparison.Ordinal);
        Assert.Contains("limit: 6000", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CodeMode_Allows257ReadsWithoutCallCap()
    {
        var modelNames = Enumerable.Range(0, 257).Select(index => $"hm-{index:000}").ToHashSet(StringComparer.Ordinal);
        var handler = new HealthModelReadCodeHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var model = ReadSegmentValue(path, "healthmodels");
            if (request.Method == HttpMethod.Get && model is not null && modelNames.Contains(model))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(2), cancellationToken);
                return JsonResponse(new
                {
                    id = $"/subscriptions/{Subscription}/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/{model}",
                    name = model,
                    type = "Microsoft.CloudHealth/healthmodels",
                    location = "westeurope",
                    properties = new { provisioningState = "Succeeded" },
                });
            }

            return JsonResponse(new
            {
                error = new { code = "NotFound", message = $"Unhandled route: {request.Method.Method} {path}" },
            }, HttpStatusCode.NotFound);
        });
        var command = CreateCommand(handler);
        const string code = """
            const names = Array.from({ length: 257 }, (_, index) => `hm-${index.toString().padStart(3, '0')}`);
            const values = await Promise.all(names.map(name => client.healthModels.get('rg', name)));
            return { count: values.length, first: values[0].name, last: values[256].name };
            """;

        var response = await ExecuteCommandAsync(command, "--subscription", Subscription, "--code", code);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = Deserialize(response, MonitorJsonContext.Default.HealthModelScriptResult);
        Assert.Null(result.Error);
        Assert.Equal(257, result.AzureCalls);
        Assert.Equal(257, result.Result!["count"]!.GetValue<int>());
        Assert.Equal("hm-000", result.Result["first"]!.GetValue<string>());
        Assert.Equal("hm-256", result.Result["last"]!.GetValue<string>());
        Assert.Equal(257, handler.StartedRequests);
        Assert.InRange(handler.MaxActiveRequests, 1, 4);
    }

    private static HealthModelQueryCommand CreateCommand(
        HealthModelReadCodeHttpMessageHandler handler,
        Func<string, string?, SubscriptionResource>? subscriptionFactory = null)
    {
        var cloud = Substitute.For<IAzureCloudConfiguration>();
        cloud.ArmEnvironment.Returns(ArmEnvironment.AzurePublicCloud);

        var azureService = Substitute.For<IAzureService>();
        azureService.CloudConfiguration.Returns(cloud);
        azureService.ResolveTenantIdAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<string?>()));

        var credential = CreateCredential();
        azureService.GetTokenCredentialAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TokenCredential>(credential));
        azureService.GetClient(Arg.Any<string?>()).Returns(_ => new HttpClient(handler));

        subscriptionFactory ??= (requestedSubscription, _) =>
            CreateSubscriptionResource(credential, handler, requestedSubscription);

        azureService.GetSubscription(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RetryPolicyOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var requestedSubscription = call.ArgAt<string>(0);
                var requestedTenant = call.ArgAt<string?>(1);
                return Task.FromResult(subscriptionFactory(requestedSubscription, requestedTenant));
            });

        var monitorService = new MonitorHealthModelService(
            azureService,
            Substitute.For<ILogger<MonitorHealthModelService>>());

        var subscriptionResolver = Substitute.For<ISubscriptionResolver>();
        subscriptionResolver.ResolveSubscription(Arg.Any<string?>()).Returns(call => call.Arg<string?>());

        return new HealthModelQueryCommand(monitorService, subscriptionResolver);
    }

    private static TokenCredential CreateCredential()
    {
        var credential = Substitute.For<TokenCredential>();
        var accessToken = new AccessToken("token", DateTimeOffset.UtcNow.AddHours(1));
        credential.GetToken(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(accessToken);
        credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<AccessToken>(accessToken));
        return credential;
    }

    private static SubscriptionResource CreateSubscriptionResource(
        TokenCredential credential,
        HttpMessageHandler handler,
        string subscriptionId)
    {
        var armClientOptions = new ArmClientOptions
        {
            Transport = new HttpClientTransport(new HttpClient(handler)),
            Environment = ArmEnvironment.AzurePublicCloud,
        };

        return new ArmClient(credential, default, armClientOptions)
            .GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{subscriptionId}"));
    }

    private static async Task<CommandResponse> ExecuteCommandWithCancellationAsync(
        HealthModelQueryCommand command,
        string[] args,
        CancellationToken cancellationToken)
    {
        var context = new CommandContext();
        return await ((IBaseCommand)command)
            .ExecuteAsync(context, command.GetCommand().Parse(args), cancellationToken);
    }

    private static Task<CommandResponse> ExecuteCommandAsync(HealthModelQueryCommand command, params string[] args) =>
        ExecuteCommandWithCancellationAsync(command, args, TestContext.Current.CancellationToken);

    private static HttpResponseMessage JsonResponse(object body, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"),
        };

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

    private static async Task WaitForCompletionCountAsync(
        ConcurrentQueue<string> completionOrder,
        int expected,
        CancellationToken cancellationToken)
    {
        while (completionOrder.Count < expected)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private static T Deserialize<T>(CommandResponse response, JsonTypeInfo<T> jsonTypeInfo) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(response.Results), jsonTypeInfo)!;
}
