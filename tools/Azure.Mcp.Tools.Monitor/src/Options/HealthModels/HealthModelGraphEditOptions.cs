// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Options;
using Azure.Mcp.Tools.Monitor.Commands.HealthModels;
using Microsoft.Mcp.Core.Options;

namespace Azure.Mcp.Tools.Monitor.Options.HealthModels;

public sealed class HealthModelGraphEditOptions : ISubscriptionOption
{
    [Option(Description = HealthModelGraphEditSchema.Schema)]
    public required string Changes { get; set; }

    [Option(Description = """
        'whatIf' (the default) computes the change set and writes nothing. 'apply' writes it, and requires
        --expect to declare the affectedCount reported by the matching whatIf run.
        """)]
    public string? Mode { get; set; }

    [Option(Description = """
        Optional JSON guard, {"affectedCount":<int>,"snapshot":"<token>"}. affectedCount is REQUIRED when
        mode is 'apply': the run is rejected without any write when it does not equal the number of targets
        the change set affects. snapshot is optional and rejects the run if the targets changed since the
        whatIf that produced the token.
        """)]
    public string? Expect { get; set; }

    [Option(Description = OptionDescriptions.Tenant)]
    public string? Tenant { get; set; }

    [Option(Description = OptionDescriptions.Subscription)]
    public string? Subscription { get; set; }

    [OptionContainer(Prefix = "retry")]
    public RetryPolicyOptions? RetryPolicy { get; set; }
}
