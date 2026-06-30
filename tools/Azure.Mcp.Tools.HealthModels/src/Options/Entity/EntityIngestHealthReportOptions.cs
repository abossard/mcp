// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Options;
using Microsoft.Mcp.Core.Options;

namespace Azure.Mcp.Tools.HealthModels.Options.Entity;

public sealed class EntityIngestHealthReportOptions : ISubscriptionOption
{
    [Option(Description = HealthModelsOptionDefinitions.HealthModel)]
    public required string HealthModel { get; set; }

    [Option(Description = HealthModelsOptionDefinitions.Entity)]
    public required string Entity { get; set; }

    [Option(Description = HealthModelsOptionDefinitions.SignalName)]
    public required string SignalName { get; set; }

    [Option(Description = HealthModelsOptionDefinitions.HealthState)]
    public required string HealthState { get; set; }

    [Option(Description = HealthModelsOptionDefinitions.Value)]
    public double? Value { get; set; }

    [Option(Description = HealthModelsOptionDefinitions.ExpiresInMinutes)]
    public int? ExpiresInMinutes { get; set; }

    [Option(Description = HealthModelsOptionDefinitions.AdditionalContext)]
    public string? AdditionalContext { get; set; }

    [Option(Description = OptionDescriptions.Tenant)]
    public string? Tenant { get; set; }

    [Option(Description = OptionDescriptions.Subscription)]
    public string? Subscription { get; set; }

    [Option(Description = OptionDescriptions.ResourceGroup)]
    public required string ResourceGroup { get; set; }

    [OptionContainer(Prefix = "retry")]
    public RetryPolicyOptions? RetryPolicy { get; set; }
}
