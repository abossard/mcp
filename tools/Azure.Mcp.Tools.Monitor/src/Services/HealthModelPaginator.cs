// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure;

namespace Azure.Mcp.Tools.Monitor.Services;

internal static class HealthModelPaginator
{
    private const string NextLinkProperty = "nextLink";

    internal static async Task<(IReadOnlyList<T> Items, string? ContinuationToken)> ReadPageAsync<T>(
        AsyncPageable<T> pageable,
        string? continuationToken,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(pageable);

        await foreach (var page in pageable.AsPages(continuationToken).WithCancellation(cancellationToken))
        {
            return ([.. page.Values], ReadNextLink(page.GetRawResponse()));
        }

        return ([], null);
    }

    /// <summary>
    /// Reads the service's own <c>nextLink</c> from the page response.
    /// </summary>
    /// <remarks>
    /// The CloudHealth entity pageable reports the token that produced the current page rather than the link to
    /// the next one, so <see cref="Page{T}.ContinuationToken"/> would mark a first page complete while more
    /// entities remain, and would repeat the caller's own token on a resumed page. The response body carries the
    /// authoritative signal. A body that is present but not readable as JSON throws rather than being reported as
    /// a complete page, so pagination never ends silently. An empty <c>nextLink</c> is normalized to <c>null</c>,
    /// matching how the SDK treats it.
    /// </remarks>
    private static string? ReadNextLink(Response? response)
    {
        var content = response?.Content;
        if (content is null || content.ToMemory().IsEmpty)
        {
            return null;
        }

        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(NextLinkProperty, out var nextLink) ||
            nextLink.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = nextLink.GetString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    internal static void EnsureMarkerAdvanced(string? inputMarker, string? outputMarker)
    {
        if (inputMarker is not null &&
            string.Equals(inputMarker, outputMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Health model pagination did not advance: continuation marker '{outputMarker}' was returned unchanged.");
        }
    }
}
