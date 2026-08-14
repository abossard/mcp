// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Azure.Mcp.Tools.Monitor.Commands.ActivityLog;
using Azure.Mcp.Tools.Monitor.Commands.HealthModels;
using Azure.Mcp.Tools.Monitor.Commands.Instrumentation;
using Azure.Mcp.Tools.Monitor.Commands.Metrics;
using Azure.Mcp.Tools.Monitor.Commands.Table;
using Azure.Mcp.Tools.Monitor.Commands.TableType;
using Azure.Mcp.Tools.Monitor.Commands.WebTests;
using Azure.Mcp.Tools.Monitor.Commands.Workspace;
using Azure.Mcp.Tools.Monitor.Models.ActivityLog;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

namespace Azure.Mcp.Tools.Monitor.Commands;

[JsonSerializable(typeof(ActivityLogEventData))]
[JsonSerializable(typeof(ActivityLogEventLevel))]
[JsonSerializable(typeof(ActivityLogListCommand.ActivityLogListCommandResult))]
[JsonSerializable(typeof(ActivityLogListResponse))]
[JsonSerializable(typeof(ActivityLogLocalizableString))]
[JsonSerializable(typeof(GetLearningResourceCommand.GetLearningResourceCommandResult))]
[JsonSerializable(typeof(HealthModelDetail))]
[JsonSerializable(typeof(HealthModelGetCommand.HealthModelGetCommandResult))]
[JsonSerializable(typeof(HealthModelIdentity))]
[JsonSerializable(typeof(EntityListQuery))]
[JsonSerializable(typeof(EntityGetQuery))]
[JsonSerializable(typeof(EntityHistoryQuery))]
[JsonSerializable(typeof(SignalHistoryQuery))]
[JsonSerializable(typeof(SignalRecommendationsQuery))]
[JsonSerializable(typeof(DataAnnotationsQuery))]
[JsonSerializable(typeof(RelationshipListQuery))]
[JsonSerializable(typeof(SignalDefinitionListQuery))]
[JsonSerializable(typeof(HealthModelSelector))]
[JsonSerializable(typeof(CreateChange))]
[JsonSerializable(typeof(PatchChange))]
[JsonSerializable(typeof(RenameChange))]
[JsonSerializable(typeof(DeleteChange))]
[JsonSerializable(typeof(HealthModelChangeExpectation))]
[JsonSerializable(typeof(HealthModelGraphEditResult))]
[JsonSerializable(typeof(HealthModelChangeResult))]
[JsonSerializable(typeof(HealthModelTargetResult))]
[JsonSerializable(typeof(HealthModelPropertyChange))]
[JsonSerializable(typeof(HealthModelScanCounts))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(HealthModelScriptResult))]
[JsonSerializable(typeof(HealthModelQueryResult))]
[JsonSerializable(typeof(HealthModelEntityResult))]
[JsonSerializable(typeof(HealthModelCollectionItemResult))]
[JsonSerializable(typeof(HealthModelEntityPage))]
[JsonSerializable(typeof(HealthModelQueryPage))]
[JsonSerializable(typeof(List<HealthModelQueryResult>))]
[JsonSerializable(typeof(HealthModelSummary))]
[JsonSerializable(typeof(List<HealthModelSummary>))]
[JsonSerializable(typeof(List<JsonNode>))]
[JsonSerializable(typeof(MetricsDefinitionsCommand.MetricsDefinitionsCommandResult))]
[JsonSerializable(typeof(MetricsDefinitionsCommand.MetricsDefinitionsCommandResult))]
[JsonSerializable(typeof(MetricsQueryCommand.MetricsQueryCommandResult))]
[JsonSerializable(typeof(MetricsQueryCommand.MetricsQueryCommandResult))]
[JsonSerializable(typeof(TableListCommand.TableListCommandResult))]
[JsonSerializable(typeof(TableTypeListCommand.TableTypeListCommandResult))]
[JsonSerializable(typeof(WebTestsCreateOrUpdateCommand.WebTestsCreateOrUpdateCommandResult))]
[JsonSerializable(typeof(WebTestsGetCommand.WebTestsGetCommandResult))]
[JsonSerializable(typeof(WorkspaceListCommand.WorkspaceListCommandResult))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(List<object>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement?))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(long?))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(double?))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(bool?))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class MonitorJsonContext : JsonSerializerContext;
