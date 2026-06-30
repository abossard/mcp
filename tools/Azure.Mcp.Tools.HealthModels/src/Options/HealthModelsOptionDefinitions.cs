// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.HealthModels.Options;

public static class HealthModelsOptionDefinitions
{
    public const string HealthModel = "The name of the Azure Health Model (Microsoft.CloudHealth/healthmodels resource).";
    public const string Entity = "The name of the entity within the health model.";
    public const string SignalDefinition = "The name of the signal definition within the health model.";
    public const string Relationship = "The name of the relationship within the health model.";
    public const string DiscoveryRule = "The name of the discovery rule within the health model.";
    public const string AuthenticationSetting = "The name of the authentication setting within the health model.";
    public const string Location = "The Azure region for the health model (e.g., eastus2).";
    public const string Tags = "Resource tags as a JSON object (e.g., {\"env\":\"prod\"}).";
    public const string Properties = "The resource 'properties' object as a JSON string. Matches the Microsoft.CloudHealth API schema for this resource.";
    public const string SignalName = "The name of the signal.";
    public const string HealthState = "The health state to report. One of: Healthy, Degraded, Unhealthy, Unknown.";
    public const string Value = "The numeric value associated with the reported signal.";
    public const string ExpiresInMinutes = "Number of minutes after which the reported signal value expires.";
    public const string AdditionalContext = "Additional free-form context for the reported signal.";
    public const string StartTime = "The start of the time range in ISO 8601 format (e.g., 2026-01-01T00:00:00Z).";
    public const string EndTime = "The end of the time range in ISO 8601 format (e.g., 2026-01-02T00:00:00Z).";
    public const string Top = "The maximum number of items to return.";
    public const string AnnotationDetails = "Annotation details as a JSON object of string key/value pairs.";
    public const string Description = "An optional description for the annotation.";
    public const string IdentityType = "The managed identity type to assign. One of: SystemAssigned, UserAssigned, 'SystemAssigned,UserAssigned', None.";
    public const string UserAssignedIdentities = "Comma-separated user-assigned managed identity resource IDs (used with UserAssigned identity types).";
}
