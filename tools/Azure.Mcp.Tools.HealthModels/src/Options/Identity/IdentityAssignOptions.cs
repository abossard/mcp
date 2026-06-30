// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Options;
using Microsoft.Mcp.Core.Options;

namespace Azure.Mcp.Tools.HealthModels.Options.Identity;

public sealed class IdentityAssignOptions : ISubscriptionOption
{
    [Option(Description = HealthModelsOptionDefinitions.HealthModel)]
    public required string HealthModel { get; set; }

    [Option(Description = HealthModelsOptionDefinitions.IdentityType)]
    public required string IdentityType { get; set; }

    [Option(Description = HealthModelsOptionDefinitions.UserAssignedIdentities)]
    public string? UserAssignedIdentities { get; set; }

    [Option(Description = OptionDescriptions.Tenant)]
    public string? Tenant { get; set; }

    [Option(Description = OptionDescriptions.Subscription)]
    public string? Subscription { get; set; }

    [Option(Description = OptionDescriptions.ResourceGroup)]
    public required string ResourceGroup { get; set; }

    [OptionContainer(Prefix = "retry")]
    public RetryPolicyOptions? RetryPolicy { get; set; }
}
