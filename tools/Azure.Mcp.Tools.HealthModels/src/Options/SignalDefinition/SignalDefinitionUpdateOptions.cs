// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Options;
using Microsoft.Mcp.Core.Options;

namespace Azure.Mcp.Tools.HealthModels.Options.SignalDefinition;

public sealed class SignalDefinitionUpdateOptions : ISubscriptionOption
{
    [Option(Description = HealthModelsOptionDefinitions.HealthModel)]
    public required string HealthModel { get; set; }

    [Option(Description = HealthModelsOptionDefinitions.SignalDefinition)]
    public required string SignalDefinition { get; set; }

    [Option(Description = HealthModelsOptionDefinitions.Properties)]
    public required string Properties { get; set; }

    [Option(Description = OptionDescriptions.Tenant)]
    public string? Tenant { get; set; }

    [Option(Description = OptionDescriptions.Subscription)]
    public string? Subscription { get; set; }

    [Option(Description = OptionDescriptions.ResourceGroup)]
    public required string ResourceGroup { get; set; }

    [OptionContainer(Prefix = "retry")]
    public RetryPolicyOptions? RetryPolicy { get; set; }
}
