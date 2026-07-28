// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.ResourceManager.CloudHealth.Models;

namespace Azure.Mcp.Tools.Monitor.Services;

/// <summary>
/// Builds the CloudHealth request-content objects for the paginated per-entity reads. The first page (no
/// continuation marker) carries the caller's time window plus page size (<c>Top</c>); continuation pages carry
/// only the opaque <c>NextMarker</c> (plus the same <c>Top</c>) because the 2026-05-01-preview / beta.3 contract
/// states the marker "must not be combined with startAt or endAt". <c>Top</c> is documented as the maximum
/// number of records to return <em>per page</em> (not an overall cap), so it is preserved on every page.
/// </summary>
internal static class HealthModelRequestContent
{
    internal static EntityHistoryContent History(DateTimeOffset? startTime, DateTimeOffset? endTime, int? top, string? marker) =>
        marker is null
            ? new EntityHistoryContent { StartOn = startTime, EndOn = endTime, Top = top }
            : new EntityHistoryContent { Top = top, NextMarker = marker };

    internal static EntitySignalHistoryContent SignalHistory(string signalName, DateTimeOffset? startTime, DateTimeOffset? endTime, int? top, string? marker) =>
        marker is null
            ? new EntitySignalHistoryContent(signalName) { StartOn = startTime, EndOn = endTime, Top = top }
            : new EntitySignalHistoryContent(signalName) { Top = top, NextMarker = marker };

    internal static EntityGetDataAnnotationsContent DataAnnotations(DateTimeOffset? startTime, DateTimeOffset? endTime, int? top, string? marker) =>
        marker is null
            ? new EntityGetDataAnnotationsContent { StartOn = startTime, EndOn = endTime, Top = top }
            : new EntityGetDataAnnotationsContent { Top = top, NextMarker = marker };
}
