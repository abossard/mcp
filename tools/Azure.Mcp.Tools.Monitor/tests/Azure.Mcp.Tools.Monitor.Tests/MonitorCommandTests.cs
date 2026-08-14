// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.ResourceManager.CloudHealth.Models;
using Microsoft.Mcp.Tests;
using Microsoft.Mcp.Tests.Client;
using Microsoft.Mcp.Tests.Client.Helpers;
using Microsoft.Mcp.Tests.Generated.Models;
using Microsoft.Mcp.Tests.Helpers;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests;

public sealed class MonitorCommandTests(ITestOutputHelper output, TestProxyFixture fixture, LiveServerFixture liveServerFixture)
    : RecordedCommandTestsBase(output, fixture, liveServerFixture)
{
    private string? _appInsightsName;
    private string? _bingWebTestName;
    private string? _healthModelParentName;
    private string? _healthModelChildName;
    private string? _healthModelTopologyName;

    private static readonly string[] s_validHealthStates =
    [
        EntityHealthState.Healthy.ToString(),
        EntityHealthState.Degraded.ToString(),
        EntityHealthState.Unhealthy.ToString(),
        EntityHealthState.Unknown.ToString(),
        EntityHealthState.Deleted.ToString(),
    ];

    private static readonly string[] s_sanitizedHeaders =
    [
        "x-ms-correlation-request-id",
        "x-ms-operation-identifier",
        "x-ms-routing-request-id",
        "x-ms-served-by",
        "X-MSEdge-Ref"
    ];

    private List<GeneralRegexSanitizer>? _generalRegexSanitizers;

    public override List<GeneralRegexSanitizer> GeneralRegexSanitizers =>
        _generalRegexSanitizers ??=
        [
            .. base.GeneralRegexSanitizers,
            new(new GeneralRegexSanitizerBody
            {
                Regex = @"ResourceHealth-[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}",
                Value = "ResourceHealth-Sanitized"
            })
        ];

    public override List<UriRegexSanitizer> UriRegexSanitizers { get; } =
    [
        new(new UriRegexSanitizerBody
        {
            Regex = "resource[Gg]roups/([^?\\/]+)",
            Value = "Sanitized",
            GroupForReplace = "1"
        }),
        new(new UriRegexSanitizerBody
        {
            Regex = "webtests/([^?\\/]+)",
            Value = "Sanitized",
            GroupForReplace = "1"
        })
    ];

    public override List<BodyKeySanitizer> BodyKeySanitizers =>
    [
        ..base.BodyKeySanitizers,
        new BodyKeySanitizer(new BodyKeySanitizerBody("$..resourceGroup")),
        new BodyKeySanitizer(new BodyKeySanitizerBody("$..displayName")),
        new BodyKeySanitizer(new BodyKeySanitizerBody("$..TimeZone")),
        new BodyKeySanitizer(new BodyKeySanitizerBody("$..id"){
            Regex = "resource[Gg]roups/([^?\\/]+)",
            Value = "Sanitized",
            GroupForReplace = "1"
        })
    ];

    public override List<HeaderRegexSanitizer> HeaderRegexSanitizers =>
    [
        .. base.HeaderRegexSanitizers,
        .. s_sanitizedHeaders.Select(h => new HeaderRegexSanitizer(new HeaderRegexSanitizerBody(h)))
    ];

    // The default body-key sanitizers AZSDK3430 ($..id) and AZSDK3493 ($..name) replace the WHOLE
    // value with "Sanitized". We disable both so CloudHealth ids and entity names are only
    // base-name-sanitized (via the ResourceBaseName GeneralRegexSanitizer) and therefore stay
    // DISTINCT. The health-model query test cross-references entity names between the entity list
    // (.name) and the getHistory (.entityName) responses; with $..name active they would all
    // collapse to the same "Sanitized" and the fan-out-only-to-matching-entities assertion could
    // not distinguish entities. Subscription id, tenant id, createdBy/lastModifiedBy (email),
    // resource group and displayName remain sanitized by their own dedicated rules.
    public override List<string> DisabledDefaultSanitizers { get; } = ["AZSDK3430", "AZSDK3493"];

    public override CustomDefaultMatcher? TestMatcher => new()
    {
        CompareBodies = false
    };

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _appInsightsName = $"{Settings.ResourceBaseName}-ai";
        _bingWebTestName = $"{Settings.ResourceBaseName}-bing-test";
        _healthModelParentName = $"{Settings.ResourceBaseName}-hm-a";
        _healthModelChildName = $"{Settings.ResourceBaseName}-hm-b";
        _healthModelTopologyName = $"{Settings.ResourceBaseName}-hm-c";
    }

    // Every MCP tool returns one text content block carrying the whole serialized CommandResponse, regardless of what
    // the individual command puts at `results`. Tests that pass `resultProcessor: root => root` reach that raw envelope
    // and route it through here, so the same invariant is asserted against a bare-payload tool (monitor_healthmodels_list),
    // a wrapper-payload tool (monitor_webtests_get) and the batch query tool (monitor_healthmodels_query). The expected
    // key list is a parameter because a failure response legitimately omits `results` (JsonIgnore WhenWritingNull).
    private static JsonElement AssertCommandResponseEnvelope(JsonElement? root, params string[] expectedKeys)
    {
        Assert.NotNull(root);
        Assert.Equal(JsonValueKind.Object, root.Value.ValueKind);

        var actualKeys = root.Value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedKeys.Order(StringComparer.Ordinal).ToArray(), actualKeys);

        return root.Value.GetProperty("results");
    }

    // [Fact]
    // public async Task Should_list_monitor_tables()
    // {
    //     var result = await CallToolAsync(
    //         "monitor_table_list",
    //         new()
    //         {
    //             { "subscription", Settings.SubscriptionId },
    //             { "workspace", Settings.ResourceBaseName },
    //             { "resource-group", Settings.ResourceGroupName },
    //             { "table-type", "Microsoft" }
    //         });

    //     var tablesArray = result.AssertProperty("tables");
    //     Assert.Equal(JsonValueKind.Array, tablesArray.ValueKind);
    //     var array = tablesArray.EnumerateArray();
    //     Assert.NotEmpty(array);
    // }

    // [Fact]
    // public async Task Should_list_monitor_workspaces()
    // {
    //     var result = await CallToolAsync(
    //         "monitor_workspace_list",
    //         new()
    //         {
    //             { "subscription", Settings.SubscriptionId }
    //         });

    //     var workspacesArray = result.AssertProperty("workspaces");
    //     Assert.Equal(JsonValueKind.Array, workspacesArray.ValueKind);
    //     var array = workspacesArray.EnumerateArray();
    //     Assert.NotEmpty(array);
    // }

    // [Fact]
    // public async Task Should_get_table_contents()
    // {
    //     // Query AzureMetrics table - fastest to propagate and most reliable
    //     var resourceGroup = Settings.DeploymentOutputs.GetValueOrDefault("staticResourceGroup", "static-test-resources");
    //     var workspace = Settings.DeploymentOutputs.GetValueOrDefault("staticWorkspace", "monitor-query-ws");
    //     await QueryForLogsAsync(
    //         async args => await CallToolAsync("monitor_workspace_log_query", args),
    //         new()
    //         {
    //             { "subscription", Settings.SubscriptionId },
    //             { "workspace", workspace },
    //             { "resource-group", resourceGroup },
    //             { "query", "AzureMetrics | where ResourceProvider == 'MICROSOFT.STORAGE' | project TimeGenerated, MetricName, Total, ResourceId" },
    //             { "table", "AzureMetrics" },
    //             { "limit", 5 },
    //             { "hours", 24 }
    //         },
    //         $"AzureMetrics | where ResourceProvider == 'MICROSOFT.STORAGE' | project TimeGenerated, MetricName, Total, ResourceId",
    //         sendLogInfo: null,
    //         sendLogAction: null,
    //         output: Output,
    //         cancellationToken: TestContext.Current.CancellationToken,
    //         maxWaitTimeSeconds: 180, // 3 minutes - metrics are faster than logs
    //         failMessage: "No storage metrics found after waiting 180 seconds");
    // }

    // [Fact]
    // public async Task Should_query_monitor_logs()
    // {
    //     var resourceGroup = Settings.DeploymentOutputs.GetValueOrDefault("staticResourceGroup", "static-test-resources");
    //     var workspace = Settings.DeploymentOutputs.GetValueOrDefault("staticWorkspace", "monitor-query-ws");
    //     await QueryForLogsAsync(
    //         async args => await CallToolAsync("monitor_workspace_log_query", args),
    //         new()
    //         {
    //             { "subscription", Settings.SubscriptionId },
    //             { "workspace", workspace },
    //             { "resource-group", resourceGroup },
    //             { "table", "StorageBlobLogs" },
    //             { "query", "StorageBlobLogs | project TimeGenerated, OperationName, StatusText" },
    //             { "limit", 1 },
    //             { "hours", 24 }
    //         },
    //         $"StorageBlobLogs | project TimeGenerated, OperationName, StatusText",
    //         sendLogInfo: null,
    //         sendLogAction: null,
    //         cancellationToken: TestContext.Current.CancellationToken,
    //         maxWaitTimeSeconds: 300, // 5 minutes - realistic for storage diagnostic logs
    //         failMessage: "No storage blob logs found after waiting 300 seconds");
    // }

    // [Fact]
    // public async Task Should_list_monitor_table_types()
    // {
    //     var result = await CallToolAsync(
    //         "monitor_table_type_list",
    //         new()
    //         {
    //             { "subscription", Settings.SubscriptionId },
    //             { "workspace", Settings.ResourceBaseName },
    //             { "resource-group", Settings.ResourceGroupName }
    //         });

    //     var tableTypesArray = result.AssertProperty("tableTypes");
    //     Assert.Equal(JsonValueKind.Array, tableTypesArray.ValueKind);
    //     var array = tableTypesArray.EnumerateArray();
    //     Assert.NotEmpty(array);
    // }

    // [Fact]
    // public async Task Should_query_monitor_logs_by_resource_id()
    // {
    //     var subscriptionId = Settings.SubscriptionId;
    //     var resourceGroup = Settings.DeploymentOutputs.GetValueOrDefault("staticResourceGroup", "static-test-resources");
    //     var storageAccountName = Settings.DeploymentOutputs.GetValueOrDefault("staticStorageAccountName", "azuresdktrainingdatatme");

    //     var storageResourceId = $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Storage/storageAccounts/{storageAccountName}";

    //     await QueryForLogsAsync(
    //         async args => await CallToolAsync("monitor_resource_log_query", args),
    //         new()
    //         {
    //             { "subscription", Settings.SubscriptionId },
    //             { "resource-id", storageResourceId },
    //             { "table", "StorageBlobLogs" },
    //             { "query", "StorageBlobLogs | project TimeGenerated, OperationName, StatusText" },
    //             { "limit", 1 },
    //             { "hours", 24 }
    //         },
    //         $"StorageBlobLogs | where TimeGenerated > datetime({DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}) | project TimeGenerated, OperationName, StatusText",
    //         sendLogInfo: null,
    //         sendLogAction: null,
    //         output: Output,
    //         cancellationToken: TestContext.Current.CancellationToken,
    //         maxWaitTimeSeconds: 300, // 5 minutes - realistic for storage diagnostic logs
    //         failMessage: "No storage blob logs found for resource after waiting 300 seconds");
    // }

    // private static async Task QueryForLogsAsync(
    //     Func<Dictionary<string, object?>, Task<JsonElement?>> callToolAsync,
    //     Dictionary<string, object?> initialQueryArgs,
    //     string logQuery,
    //     string? sendLogInfo = null,
    //     Func<Task>? sendLogAction = null,
    //     ITestOutputHelper? output = null,
    //     CancellationToken cancellationToken = default,
    //     int maxWaitTimeSeconds = 60,
    //     string? failMessage = null)
    // {
    //     // First try to find any existing logs
    //     output?.WriteLine($"Checking for existing logs...");
    //     var queryStartTime = DateTime.UtcNow;
    //     var result = await callToolAsync(initialQueryArgs);
    //     Assert.NotNull(result);
    //     Assert.Equal(JsonValueKind.Array, result.Value.ValueKind);
    //     var logs = result.Value.EnumerateArray();
    //     var queryDuration = (DateTime.UtcNow - queryStartTime).TotalSeconds;

    //     if (logs.Any())
    //     {
    //         output?.WriteLine($"Found existing logs");
    //         output?.WriteLine($"Query performance: {queryDuration:F1}s to execute");
    //         return;
    //     }

    //     if (sendLogAction != null)
    //     {
    //         output?.WriteLine($"No recent logs found, sending new log...");
    //         await sendLogAction();
    //         output?.WriteLine(sendLogInfo ?? "Info log sent.");
    //     }

    //     // Start time for query window - use the current time
    //     var testStartTime = DateTime.UtcNow;
    //     output?.WriteLine($"Starting to query for new log (max wait: {maxWaitTimeSeconds}s)...");
    //     var attemptCount = 0;

    //     while ((DateTime.UtcNow - testStartTime).TotalSeconds < maxWaitTimeSeconds)
    //     {
    //         // More aggressive polling at start (1s, 2s, 4s, 8s, 15s...)
    //         var delaySeconds = Math.Min(Math.Pow(2, attemptCount), 15);
    //         attemptCount++;

    //         var elapsed = (DateTime.UtcNow - testStartTime).TotalSeconds;
    //         output?.WriteLine($"Attempt {attemptCount}: Querying for logs at {elapsed:F1}s...");

    //         queryStartTime = DateTime.UtcNow;
    //         var queryArgs = new Dictionary<string, object?>(initialQueryArgs)
    //         {
    //             ["query"] = logQuery
    //         };
    //         result = await callToolAsync(queryArgs);
    //         queryDuration = (DateTime.UtcNow - queryStartTime).TotalSeconds;
    //         output?.WriteLine($"Query completed in {queryDuration:F1} seconds");

    //         Assert.NotNull(result);
    //         Assert.Equal(JsonValueKind.Array, result.Value.ValueKind);
    //         logs = result.Value.EnumerateArray();
    //         if (logs.Any())
    //         {
    //             var totalTime = (DateTime.UtcNow - testStartTime).TotalSeconds;
    //             output?.WriteLine($"Success! Found new log after {totalTime:F1} seconds (attempt {attemptCount})");
    //             output?.WriteLine($"Query performance: {queryDuration:F1}s to execute, {totalTime:F1}s total test time");
    //             return;
    //         }

    //         output?.WriteLine($"No logs found yet (attempt {attemptCount}), waiting {delaySeconds:F1} seconds before retrying...");
    //         await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
    //     }

    //     Assert.Fail(failMessage ?? $"No logs found after waiting {maxWaitTimeSeconds} seconds");
    // }

    // [Fact]
    // public async Task Should_list_metric_definitions()
    // {
    //     // Example resource ID - uses a storage account that should exist from the test fixture
    //     string resourceId = $"/subscriptions/{Settings.SubscriptionId}/resourceGroups/{Settings.ResourceGroupName}/providers/Microsoft.Storage/storageAccounts/{_storageAccountName}";

    //     var result = await CallToolAsync(
    //         "monitor_metrics_definitions",
    //         new()
    //         {
    //             { "subscription", Settings.SubscriptionId },
    //             { "resource", _storageAccountName },
    //             { "resource-type", "Microsoft.Storage/storageAccounts" }
    //         });

    //     var resultsArray = result.AssertProperty("results");
    //     Assert.Equal(JsonValueKind.Array, resultsArray.ValueKind);
    //     Assert.NotEmpty(resultsArray.EnumerateArray());

    //     // Validate the status message
    //     var status = result.AssertProperty("status");
    //     Assert.Equal(JsonValueKind.String, status.ValueKind);
    //     var statusString = status.GetString();
    //     Assert.NotNull(statusString);
    //     Assert.Contains("metric definitions returned", statusString);
    //     Assert.StartsWith("All", statusString);

    //     // Validate at least one metric definition has all expected properties populated
    //     var firstDefinition = resultsArray.EnumerateArray().First();

    //     // Verify required properties exist and are populated
    //     var name = firstDefinition.AssertProperty("name");
    //     Assert.Equal(JsonValueKind.String, name.ValueKind);
    //     Assert.False(string.IsNullOrEmpty(name.GetString()));

    //     var category = firstDefinition.AssertProperty("category");
    //     Assert.Equal(JsonValueKind.String, category.ValueKind);
    //     Assert.False(string.IsNullOrEmpty(category.GetString()));

    //     var description = firstDefinition.AssertProperty("description");
    //     Assert.Equal(JsonValueKind.String, description.ValueKind);
    //     Assert.False(string.IsNullOrEmpty(description.GetString()));

    //     var unit = firstDefinition.AssertProperty("unit");
    //     Assert.Equal(JsonValueKind.String, unit.ValueKind);
    //     Assert.False(string.IsNullOrEmpty(unit.GetString()));

    //     var defaultAggregation = firstDefinition.AssertProperty("defaultAggregation");
    //     Assert.Equal(JsonValueKind.String, defaultAggregation.ValueKind);
    //     Assert.False(string.IsNullOrEmpty(defaultAggregation.GetString()));

    //     var supportedAggregationTypes = firstDefinition.AssertProperty("supportedAggregationTypes");
    //     Assert.Equal(JsonValueKind.Array, supportedAggregationTypes.ValueKind);
    //     Assert.NotEmpty(supportedAggregationTypes.EnumerateArray());

    //     var isDimensionRequired = firstDefinition.AssertProperty("isDimensionRequiredWhenQuerying");
    //     Assert.Equal(JsonValueKind.False, isDimensionRequired.ValueKind);

    //     var metricNamespace = firstDefinition.AssertProperty("metricNamespace");
    //     Assert.Equal(JsonValueKind.String, metricNamespace.ValueKind);
    //     Assert.False(string.IsNullOrEmpty(metricNamespace.GetString()));

    //     var allowedIntervals = firstDefinition.AssertProperty("allowedIntervals");
    //     Assert.Equal(JsonValueKind.Array, allowedIntervals.ValueKind);
    //     Assert.NotEmpty(allowedIntervals.EnumerateArray());

    //     var dimensions = firstDefinition.AssertProperty("dimensions");
    //     Assert.Equal(JsonValueKind.Array, dimensions.ValueKind);
    //     // Dimensions array can be empty, so we just verify it exists and is an array
    // }

    // [Fact]
    // public async Task Should_query_metrics()
    // {
    //     // Example resource ID - uses a storage account that should exist from the test fixture
    //     string resourceId = $"/subscriptions/{Settings.SubscriptionId}/resourceGroups/{Settings.ResourceGroupName}/providers/Microsoft.Storage/storageAccounts/{_storageAccountName}";

    //     var result = await CallToolAsync(
    //         "monitor_metrics_query",
    //         new()
    //         {
    //             { "subscription", Settings.SubscriptionId },
    //             { "resource", _storageAccountName },
    //             { "resource-type", "Microsoft.Storage/storageAccounts" },
    //             { "metric-namespace", "Microsoft.storage/storageAccounts" },
    //             { "metric-names", "UsedCapacity" } // Common storage account metric
    //         });

    //     var resultsArray = result.AssertProperty("results");
    //     Assert.Equal(JsonValueKind.Array, resultsArray.ValueKind);
    //     Assert.NotEmpty(resultsArray.EnumerateArray());

    //     // Validate the first metric has all expected properties
    //     var firstMetric = resultsArray.EnumerateArray().First();

    //     // Verify metric-level properties
    //     var name = firstMetric.AssertProperty("name");
    //     Assert.Equal(JsonValueKind.String, name.ValueKind);
    //     Assert.False(string.IsNullOrEmpty(name.GetString()));

    //     var unit = firstMetric.AssertProperty("unit");
    //     Assert.Equal(JsonValueKind.String, unit.ValueKind);
    //     Assert.False(string.IsNullOrEmpty(unit.GetString()));

    //     var timeSeries = firstMetric.AssertProperty("timeSeries");
    //     Assert.Equal(JsonValueKind.Array, timeSeries.ValueKind);
    //     Assert.NotEmpty(timeSeries.EnumerateArray());

    //     // Validate the first timeSeries entry has all expected properties
    //     var firstTimeSeries = timeSeries.EnumerateArray().First();

    //     var metadata = firstTimeSeries.AssertProperty("metadata");
    //     Assert.Equal(JsonValueKind.Object, metadata.ValueKind);

    //     var start = firstTimeSeries.AssertProperty("start");
    //     Assert.Equal(JsonValueKind.String, start.ValueKind);
    //     Assert.False(string.IsNullOrEmpty(start.GetString()));
    //     // Verify it's a valid ISO date format
    //     Assert.True(DateTime.TryParse(start.GetString(), out _));

    //     var end = firstTimeSeries.AssertProperty("end");
    //     Assert.Equal(JsonValueKind.String, end.ValueKind);
    //     Assert.False(string.IsNullOrEmpty(end.GetString()));
    //     // Verify it's a valid ISO date format
    //     Assert.True(DateTime.TryParse(end.GetString(), out _));

    //     var interval = firstTimeSeries.AssertProperty("interval");
    //     Assert.Equal(JsonValueKind.String, interval.ValueKind);
    //     Assert.False(string.IsNullOrEmpty(interval.GetString()));
    //     // Verify it follows duration format (starts with PT)
    //     Assert.StartsWith("PT", interval.GetString());
    // }

    // private async Task GenerateStorageActivityAsync()
    // {
    //     try
    //     {
    //         // First, generate basic activity (creates metrics)
    //         var listResult = await CallToolAsync("storage_blob_container_list", new()
    //         {
    //             { "subscription", Settings.SubscriptionId },
    //             { "account", _storageAccountName }
    //         });

    //         Output.WriteLine("Listed storage containers to generate metrics");

    //         // Try to list blobs in a container if any exist (also generates metrics)
    //         var containersArray = listResult?.GetProperty("containers");
    //         if (containersArray?.ValueKind == JsonValueKind.Array && containersArray.Value.EnumerateArray().Any())
    //         {
    //             var firstContainer = containersArray.Value.EnumerateArray().First();
    //             if (firstContainer.TryGetProperty("name", out var containerName))
    //             {
    //                 var blobListResult = await CallToolAsync("storage_blob_list", new()
    //                 {
    //                     { "subscription", Settings.SubscriptionId },
    //                     { "account", _storageAccountName },
    //                     { "container", containerName.GetString() }
    //                 });

    //                 Output.WriteLine($"Listed blobs in container '{containerName.GetString()}' to generate metrics");

    //                 // Try to get properties of a blob if any exist (generates StorageBlobLogs)
    //                 var blobsArray = blobListResult?.GetProperty("blobs");
    //                 if (blobsArray?.ValueKind == JsonValueKind.Array && blobsArray.Value.EnumerateArray().Any())
    //                 {
    //                     var firstBlob = blobsArray.Value.EnumerateArray().First();
    //                     if (firstBlob.TryGetProperty("name", out var blobName))
    //                     {
    //                         try
    //                         {
    //                             // Note: This would require a blob details command if available
    //                             // For now, the list operations should generate some transaction logs
    //                             Output.WriteLine($"Found blob '{blobName.GetString()}' - operations should generate diagnostic logs");
    //                         }
    //                         catch
    //                         {
    //                             // Ignore blob property errors
    //                         }
    //                     }
    //                 }
    //             }
    //         }

    //         // Even if no blobs exist, the container/blob listing operations
    //         // will generate transaction metrics that should appear in AzureMetrics table
    //         Output.WriteLine("Storage operations completed - should generate metrics and potentially some blob logs");
    //     }
    //     catch (Exception ex)
    //     {
    //         Output.WriteLine($"Note: Storage activity generation encountered an issue: {ex.Message}");
    //         // Don't fail the test if storage activity generation fails
    //     }
    // }

    #region WebTests Integration Tests

    [Fact]
    public async Task Should_List_WebTests()
    {
        var envelope = await CallToolAsync(
            "monitor_webtests_get",
            new()
            {
                { "subscription", Settings.SubscriptionId }
            },
            resultProcessor: root => root);

        var result = AssertCommandResponseEnvelope(envelope, "duration", "message", "results", "status");

        var webTestsArray = result.AssertProperty("webTests");
        Assert.Equal(JsonValueKind.Array, webTestsArray.ValueKind);

        // Should have at least 2 web tests (bing and microsoft from test resources)
        var webTests = webTestsArray.EnumerateArray().ToList();
        Assert.True(webTests.Count >= 2, $"Expected at least 2 web tests, but found {webTests.Count}");

        foreach (var webTest in webTests)
        {
            var resourceName = webTest.AssertProperty("resourceName");
            Assert.Equal(JsonValueKind.String, resourceName.ValueKind);
            Assert.False(string.IsNullOrEmpty(resourceName.GetString()));

            var location = webTest.AssertProperty("location");
            Assert.Equal(JsonValueKind.String, location.ValueKind);
            Assert.False(string.IsNullOrEmpty(location.GetString()));
        }
    }

    [Fact]
    public async Task Should_List_WebTests_ByResourceGroup()
    {
        var result = await CallToolAsync(
            "monitor_webtests_get",
            new()
            {
                { "subscription", Settings.SubscriptionId },
                { "resource-group", Settings.ResourceGroupName }
            });

        var webTestsArray = result.AssertProperty("webTests");
        Assert.Equal(JsonValueKind.Array, webTestsArray.ValueKind);

        // The array can be empty if no web tests exist in the resource group, which is fine for this test
        foreach (var webTest in webTestsArray.EnumerateArray())
        {
            Assert.True(webTest.TryGetProperty("resourceName", out var resourceName));
            Assert.Equal(JsonValueKind.String, resourceName.ValueKind);
            Assert.False(string.IsNullOrEmpty(resourceName.GetString()));

            Assert.True(webTest.TryGetProperty("location", out var location));
            Assert.Equal(JsonValueKind.String, location.ValueKind);
            Assert.False(string.IsNullOrEmpty(location.GetString()));

            // Verify the resource group matches what was requested
            if (webTest.TryGetProperty("resourceGroup", out var resourceGroup))
            {
                Assert.Equal(Settings.ResourceGroupName, resourceGroup.GetString());
            }
        }
    }

    [Fact]
    public async Task Should_Get_WebTest_Details()
    {
        // Use one of the test availability tests we created - using resource base name pattern
        var webTestName = _bingWebTestName;

        var result = await CallToolAsync(
            "monitor_webtests_get",
            new()
            {
                { "subscription", Settings.SubscriptionId },
                { "resource-group", Settings.ResourceGroupName },
                { "webtest-resource", webTestName }
            });

        var webTest = result.AssertProperty("webTest");
        Assert.Equal(JsonValueKind.Object, webTest.ValueKind);

        // Verify required properties exist
        Assert.True(webTest.TryGetProperty("resourceName", out var resourceName));
        Assert.Equal(TestMode == TestMode.Playback ? "Sanitized" : webTestName, resourceName.GetString());

        Assert.True(webTest.TryGetProperty("location", out var location));
        Assert.Equal(JsonValueKind.String, location.ValueKind);
        Assert.False(string.IsNullOrEmpty(location.GetString()));

        Assert.True(webTest.TryGetProperty("kind", out var webTestKind));
        Assert.Equal("Standard", webTestKind.GetString());

        Assert.True(webTest.TryGetProperty("isEnabled", out var enabled));
        Assert.True(enabled.GetBoolean());
    }

    [Fact]
    public async Task Should_Create_WebTest()
    {
        // Use the Application Insights component we created in test resources with proper base name pattern
        var webTestName = $"test-webtest-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var appInsightsComponentId = $"/subscriptions/{Settings.SubscriptionId}/resourceGroups/{Settings.ResourceGroupName}/providers/Microsoft.Insights/components/{_appInsightsName}";

        var result = await CallToolAsync(
            "monitor_webtests_createorupdate",
            new()
            {
                { "subscription", Settings.SubscriptionId },
                { "resource-group", Settings.ResourceGroupName },
                { "webtest-resource", webTestName },
                { "appinsights-component", appInsightsComponentId },
                { "location", "West US" },
                { "webtest-locations", "us-ca-sjc-azr" },
                { "request-url", "https://example.com" },
                { "webtest", "Test Web Test" },
                { "description", "Integration test web test" },
                { "enabled", "true" },
                { "frequency", "300" },
                { "timeout", "30" }
            });

        var webTest = result.AssertProperty("webTest");
        Assert.Equal(JsonValueKind.Object, webTest.ValueKind);

        // Verify the created web test
        Assert.True(webTest.TryGetProperty("resourceName", out var resourceName));
        Assert.Equal(TestMode == TestMode.Playback ? "Sanitized" : webTestName, resourceName.GetString());

        Assert.True(webTest.TryGetProperty("isEnabled", out var enabled));
        Assert.True(enabled.GetBoolean());

        Assert.True(webTest.TryGetProperty("kind", out var webTestKind));
        Assert.Equal("Standard", webTestKind.GetString());
    }

    [Fact]
    public async Task Should_Update_WebTest()
    {
        // Use the existing bing web test that should already exist from test infrastructure
        var webTestName = _bingWebTestName;

        var result = await CallToolAsync(
            "monitor_webtests_createorupdate",
            new()
            {
                { "subscription", Settings.SubscriptionId },
                { "resource-group", Settings.ResourceGroupName },
                { "webtest-resource", webTestName },
                { "webtest", "Updated Test Name" },
                { "description", "Updated integration test web test description" },
                { "enabled", "true" },
                { "frequency", "600" }, // Change frequency from default
                { "timeout", "60" } // Change timeout from default
            });

        var webTest = result.AssertProperty("webTest");
        Assert.Equal(JsonValueKind.Object, webTest.ValueKind);

        // Verify the updated web test
        Assert.True(webTest.TryGetProperty("resourceName", out var resourceName));
        Assert.Equal(TestMode == TestMode.Playback ? "Sanitized" : webTestName, resourceName.GetString());

        // Verify that the updates were applied
        Assert.True(webTest.TryGetProperty("webTestName", out var updatedName));
        Assert.Equal("Updated Test Name", updatedName.GetString());

        Assert.True(webTest.TryGetProperty("frequencyInSeconds", out var frequency));
        Assert.Equal(600, frequency.GetInt32());

        Assert.True(webTest.TryGetProperty("timeoutInSeconds", out var timeout));
        Assert.Equal(60, timeout.GetInt32());

        Assert.True(webTest.TryGetProperty("kind", out var webTestKind));
        Assert.Equal("Standard", webTestKind.GetString());
    }

    #endregion

    #region Health Models

    [Fact]
    public async Task Should_List_HealthModels()
    {
        var envelope = await CallToolAsync(
            "monitor_healthmodels_list",
            new()
            {
                { "subscription", Settings.SubscriptionId },
                { "resource-group", Settings.ResourceGroupName }
            },
            resultProcessor: root => root);

        var result = AssertCommandResponseEnvelope(envelope, "duration", "message", "results", "status");

        Assert.Equal(JsonValueKind.Array, result.ValueKind);

        var models = result.EnumerateArray().ToList();
        Assert.NotEmpty(models);

        Assert.All(models, model =>
        {
            var name = model.AssertProperty("name");
            Assert.Equal(JsonValueKind.String, name.ValueKind);
            Assert.False(string.IsNullOrEmpty(name.GetString()));

            var provisioningState = model.AssertProperty("provisioningState");
            Assert.Equal(JsonValueKind.String, provisioningState.ValueKind);
            Assert.False(string.IsNullOrEmpty(provisioningState.GetString()));
        });

        var idSuffixes = models
            .Select(m => m.AssertProperty("id").GetString()!.Split('/').Last())
            .ToList();
        Assert.Contains(_healthModelParentName, idSuffixes);
        Assert.Contains(_healthModelChildName, idSuffixes);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    public async Task Should_Get_HealthModel_WithValidHealthState(string suffix)
    {
        var healthModelName = $"{Settings.ResourceBaseName}-hm-{suffix}";

        var result = await CallToolAsync(
            "monitor_healthmodels_get",
            new()
            {
                { "subscription", Settings.SubscriptionId },
                { "resource-group", Settings.ResourceGroupName },
                { "health-model", healthModelName }
            });

        var healthModel = result.AssertProperty("healthModel");
        Assert.Equal(JsonValueKind.Object, healthModel.ValueKind);

        Assert.EndsWith(healthModelName, healthModel.AssertProperty("id").GetString());
        Assert.Equal(Settings.ResourceGroupName, healthModel.AssertProperty("resourceGroup").GetString());
        Assert.False(string.IsNullOrEmpty(healthModel.AssertProperty("provisioningState").GetString()));

        var healthState = healthModel.AssertProperty("healthState");
        Assert.Equal(JsonValueKind.String, healthState.ValueKind);
        Assert.Contains(healthState.GetString(), s_validHealthStates);
    }

    // Recorded end-to-end proof for the real ArmHealthModelCallRunner. Entity discovery and entity history each return
    // exactly one SDK page with independent machine-readable completeness and the exact service continuation.
    // Requires a fixture whose child model has an entity with >=2 history records and >=1 non-Healthy entity among
    // several (see test-resources.healthmodels.module.bicep).
    [Fact]
    public async Task Should_Query_HealthModel_PagesHistoryAcrossMarkers_AndFansOutByRealHealthState()
    {
        var model = _healthModelChildName!;
        var rootEntity = _healthModelChildName!;
        var queries = $$"""
            [
              {"kind":"entityList","resourceGroup":"{{Settings.ResourceGroupName}}","healthModel":"{{model}}"},
              {"kind":"entityHistory","resourceGroup":"{{Settings.ResourceGroupName}}","healthModel":"{{model}}","page":{"size":1},"target":{"entity":"{{rootEntity}}"} },
              {"kind":"entityHistory","resourceGroup":"{{Settings.ResourceGroupName}}","healthModel":"{{model}}","page":{"size":1},"target":{"whereHealth":"notHealthy"} }
            ]
            """;

        var envelope = await CallToolAsync(
            "monitor_healthmodels_query",
            new()
            {
                { "subscription", Settings.SubscriptionId },
                { "queries", queries }
            },
            resultProcessor: root => root);

        var result = AssertCommandResponseEnvelope(envelope, "duration", "message", "results", "status");

        Assert.Equal(JsonValueKind.Array, result.ValueKind);
        var ordered = result.EnumerateArray().ToList();
        // Correlated by zero-based input position, returned in input order.
        Assert.Equal(new[] { 0, 1, 2 }, ordered.Select(r => r.AssertProperty("queryIndex").GetInt32()));

        // (2) Real health-state mapping: every entity node carries the verbatim SDK entity payload whose
        //     properties.healthState is a genuine CloudHealth health-state string.
        var entities = ordered[0];
        Assert.True(entities.AssertProperty("success").GetBoolean());
        Assert.Equal("entityList", entities.AssertProperty("kind").GetString());
        var entityNodes = entities.AssertProperty("entities").EnumerateArray().ToList();
        Assert.NotEmpty(entityNodes);
        var listPage = entities.AssertProperty("page");
        Assert.Equal(entityNodes.Count, listPage.AssertProperty("returnedCount").GetInt32());
        Assert.True(listPage.AssertProperty("complete").GetBoolean());
        Assert.All(entityNodes, n =>
        {
            var healthState = n.AssertProperty("entity").AssertProperty("properties").AssertProperty("healthState").GetString();
            Assert.Contains(healthState, s_validHealthStates);
        });

        var notHealthy = entityNodes
            .Where(n => n.AssertProperty("entity").AssertProperty("properties").AssertProperty("healthState").GetString() != EntityHealthState.Healthy.ToString())
            .Select(n => n.AssertProperty("entityName").GetString())
            .ToHashSet();
        Assert.NotEmpty(notHealthy); // fixture must contain at least one non-Healthy entity for the fan-out to be meaningful

        // top=1 is the API page size. One response exposes one record and the exact marker for the next request.
        var history = ordered[1];
        Assert.True(history.AssertProperty("success").GetBoolean());
        var historyNode = Assert.Single(history.AssertProperty("entities").EnumerateArray().ToList());
        var points = historyNode.AssertProperty("history").AssertProperty("history").EnumerateArray()
            .Select(p => p.AssertProperty("occurredAt").GetDateTimeOffset())
            .ToList();
        Assert.Single(points);
        var historyPage = historyNode.AssertProperty("page");
        Assert.Equal(1, historyPage.AssertProperty("returnedCount").GetInt32());
        Assert.False(historyPage.AssertProperty("complete").GetBoolean());
        var nextMarker = historyPage.AssertProperty("cursor").GetString();
        Assert.False(string.IsNullOrEmpty(nextMarker));

        // (2) HealthFilter fan-out: the dependent per-entity query produced one envelope node per entity the list
        //     reported as non-Healthy, resolved from the single shared entity list.
        var fanOut = ordered[2];
        Assert.True(fanOut.AssertProperty("success").GetBoolean());
        var fanOutEntities = fanOut.AssertProperty("entities").EnumerateArray()
            .Select(n => n.AssertProperty("entityName").GetString())
            .ToHashSet();
        Assert.NotEmpty(fanOutEntities);
        Assert.All(fanOutEntities, name => Assert.Contains(name, notHealthy));
        var discoveryPage = fanOut.AssertProperty("page");
        Assert.Equal(entityNodes.Count, discoveryPage.AssertProperty("returnedCount").GetInt32());
        Assert.True(discoveryPage.AssertProperty("complete").GetBoolean());

        var resumeQueries = $$"""
            [
              {"kind":"entityHistory","resourceGroup":"{{Settings.ResourceGroupName}}","healthModel":"{{model}}","page":{"size":1,"cursor":{{JsonSerializer.Serialize(nextMarker)}} },"target":{"entity":"{{rootEntity}}"} }
            ]
            """;
        var resumeEnvelope = await CallToolAsync(
            "monitor_healthmodels_query",
            new()
            {
                { "subscription", Settings.SubscriptionId },
                { "queries", resumeQueries }
            },
            resultProcessor: root => root);

        var resumeResult = AssertCommandResponseEnvelope(resumeEnvelope, "duration", "message", "results", "status");
        var resumedQuery = Assert.Single(resumeResult.EnumerateArray().ToList());
        Assert.Equal(0, resumedQuery.AssertProperty("queryIndex").GetInt32());
        Assert.True(resumedQuery.AssertProperty("success").GetBoolean());
        var resumedNode = Assert.Single(resumedQuery.AssertProperty("entities").EnumerateArray().ToList());
        Assert.Equal(rootEntity, resumedNode.AssertProperty("entityName").GetString());
        var resumedPoint = Assert.Single(
            resumedNode.AssertProperty("history").AssertProperty("history").EnumerateArray().ToList());
        Assert.NotEqual(points[0], resumedPoint.AssertProperty("occurredAt").GetDateTimeOffset());
        var resumedPage = resumedNode.AssertProperty("page");
        Assert.True(resumedPage.AssertProperty("complete").GetBoolean());
        Assert.Equal(1, resumedPage.AssertProperty("returnedCount").GetInt32());
        Assert.False(resumedPage.TryGetProperty("cursor", out _));
    }

    // Recorded end-to-end proof that a dependency rollup is explainable from one batch: the root's aggregation
    // rule and the named children it aggregates come back together, and the model's signal definitions expose
    // real signal names with the thresholds that decide their state. Requires the topology fixture model
    // (see test-resources.healthmodels.module.bicep: two relationships plus one signaldefinitions child).
    [Fact]
    public async Task Should_Query_HealthModel_ReadsDependencyEdgesAndSignalDefinitionThresholds()
    {
        var model = _healthModelTopologyName!;
        var rootEntity = _healthModelTopologyName!;
        var queries = $$"""
            [
              {"kind":"relationshipList","resourceGroup":"{{Settings.ResourceGroupName}}","healthModel":"{{model}}"},
              {"kind":"signalDefinitionList","resourceGroup":"{{Settings.ResourceGroupName}}","healthModel":"{{model}}"},
              {"kind":"entityGet","resourceGroup":"{{Settings.ResourceGroupName}}","healthModel":"{{model}}","entity":"{{rootEntity}}","select":["signals"]}
            ]
            """;

        var envelope = await CallToolAsync(
            "monitor_healthmodels_query",
            new()
            {
                { "subscription", Settings.SubscriptionId },
                { "queries", queries }
            },
            resultProcessor: root => root);

        var result = AssertCommandResponseEnvelope(envelope, "duration", "message", "results", "status");
        var ordered = result.EnumerateArray().ToList();
        Assert.Equal(new[] { 0, 1, 2 }, ordered.Select(r => r.AssertProperty("queryIndex").GetInt32()));

        // Relationship list: both edges of the fixture, each naming its parent and child entity.
        var relationships = ordered[0];
        Assert.True(relationships.AssertProperty("success").GetBoolean());
        Assert.Equal("relationshipList", relationships.AssertProperty("kind").GetString());
        var edges = relationships.AssertProperty("relationships").EnumerateArray()
            .Select(edge => edge.AssertProperty("relationship").AssertProperty("properties"))
            .Select(properties => (
                Parent: properties.AssertProperty("parentEntityName").GetString(),
                Child: properties.AssertProperty("childEntityName").GetString()))
            .ToList();
        Assert.Equal(2, edges.Count);
        Assert.All(edges, edge => Assert.Equal(rootEntity, edge.Parent));
        Assert.Contains($"{model}-leaf-frontend", edges.Select(edge => edge.Child));
        Assert.Contains($"{model}-leaf-backend", edges.Select(edge => edge.Child));
        var edgePage = relationships.AssertProperty("page");
        Assert.Equal(2, edgePage.AssertProperty("returnedCount").GetInt32());
        Assert.True(edgePage.AssertProperty("complete").GetBoolean());

        // Signal definition list: a real signal name plus the thresholds that decide its state, so a caller
        // never has to guess a name that would silently return an empty history.
        var definitions = ordered[1];
        Assert.True(definitions.AssertProperty("success").GetBoolean());
        Assert.Equal("signalDefinitionList", definitions.AssertProperty("kind").GetString());
        var definition = Assert.Single(
            definitions.AssertProperty("signalDefinitions").EnumerateArray().ToList(),
            item => item.AssertProperty("name").GetString() == "storage-availability");
        var definitionProperties = definition.AssertProperty("signalDefinition").AssertProperty("properties");
        Assert.Equal("AzureResourceMetric", definitionProperties.AssertProperty("signalKind").GetString());
        Assert.Equal(
            95,
            definitionProperties.AssertProperty("evaluationRules").AssertProperty("unhealthyRule")
                .AssertProperty("threshold").GetDouble());
        // Kind-specific detail stays behind the full field group.
        Assert.False(definitionProperties.TryGetProperty("metricName", out _));

        // The rollup rule and the children it aggregates now come from the same batch.
        var root = ordered[2];
        Assert.True(root.AssertProperty("success").GetBoolean());
        var rootNode = Assert.Single(root.AssertProperty("entities").EnumerateArray().ToList());
        Assert.Equal(
            "WorstOf",
            rootNode.AssertProperty("entity").AssertProperty("properties").AssertProperty("signalGroups")
                .AssertProperty("dependencies").AssertProperty("aggregationType").GetString());
    }

    #endregion
}
