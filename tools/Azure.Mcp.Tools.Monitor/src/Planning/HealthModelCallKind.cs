// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// The concrete Azure Resource Manager operation a <see cref="PlannedCall"/> maps to.
/// </summary>
internal enum HealthModelCallKind
{
    /// <summary>Entities_ListByHealthModel (optionally with a point-in-time timestamp).</summary>
    ListEntities,

    /// <summary>Entities_Get for a single entity.</summary>
    GetEntity,

    /// <summary>Entities_GetHistory for a single entity.</summary>
    GetHistory,

    /// <summary>Entities_GetSignalHistory for a single entity and signal.</summary>
    GetSignalHistory,

    /// <summary>Entities_GetSignalRecommendations for a single entity.</summary>
    GetSignalRecommendations,

    /// <summary>Entities_GetDataAnnotations for a single entity.</summary>
    GetDataAnnotations,
}
