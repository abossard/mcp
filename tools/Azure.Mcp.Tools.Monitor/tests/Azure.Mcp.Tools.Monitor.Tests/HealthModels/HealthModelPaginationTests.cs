// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure;
using Azure.Mcp.Tools.Monitor.Services;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

public class HealthModelPaginationTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 7, 2, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(2, "https://host/entities?skipToken=two")]
    [InlineData(0, "https://host/entities?skipToken=empty")]
    [InlineData(3, null)]
    public async Task ReadPageAsync_ReturnsExactlyOneSdkPage(int itemCount, string? nextLink)
    {
        var pages = AsyncPageable<int>.FromPages(
        [
            Page<int>.FromValues(Enumerable.Range(1, itemCount).ToArray(), "token-that-fetched-this-page", ListResponse(nextLink)),
            Page<int>.FromValues([99], null, ListResponse(null)),
        ]);

        var page = await HealthModelPaginator.ReadPageAsync(pages, continuationToken: null, CancellationToken.None);

        Assert.Equal(itemCount, page.Items.Count);
        Assert.Equal(nextLink, page.ContinuationToken);
        Assert.DoesNotContain(99, page.Items);
    }

    [Fact]
    public async Task ReadPageAsync_PassesOpaqueContinuationUnchangedToAsPages()
    {
        var source = new RecordingAsyncPageable<int>(
            Page<int>.FromValues([7], "opaque/+== token", ListResponse("https://host/entities?skipToken=next")));

        var page = await HealthModelPaginator.ReadPageAsync(
            source, "opaque/+== token", CancellationToken.None);

        Assert.Equal("opaque/+== token", source.InputContinuationToken);
        Assert.Equal([7], page.Items);
        Assert.Equal("https://host/entities?skipToken=next", page.ContinuationToken);
    }

    [Fact]
    public async Task ReadPageAsync_ReportsServiceNextLink_WhenSdkEchoesTheRequestedToken()
    {
        // The CloudHealth pageable sets ContinuationToken to the token that produced the page, so a first page
        // echoes null and a resumed page echoes the caller's own token. Both must still surface the service link.
        var firstPage = new RecordingAsyncPageable<int>(
            Page<int>.FromValues([1], null, ListResponse("https://host/entities?skipToken=page2")));
        var resumedPage = new RecordingAsyncPageable<int>(
            Page<int>.FromValues([2], "https://host/entities?skipToken=page2", ListResponse("https://host/entities?skipToken=page3")));

        var first = await HealthModelPaginator.ReadPageAsync(firstPage, null, CancellationToken.None);
        var resumed = await HealthModelPaginator.ReadPageAsync(
            resumedPage, "https://host/entities?skipToken=page2", CancellationToken.None);

        Assert.Equal("https://host/entities?skipToken=page2", first.ContinuationToken);
        Assert.Equal("https://host/entities?skipToken=page3", resumed.ContinuationToken);
        HealthModelPaginator.EnsureMarkerAdvanced("https://host/entities?skipToken=page2", resumed.ContinuationToken);
    }

    [Theory]
    [InlineData("""{"value":[]}""")]
    [InlineData("""{"value":[],"nextLink":""}""")]
    [InlineData("""{"value":[],"nextLink":null}""")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ReadPageAsync_ReportsCompletePage_WhenBodyCarriesNoNextLink(string? body)
    {
        var source = new RecordingAsyncPageable<int>(
            Page<int>.FromValues([1], "token-that-fetched-this-page", new StubResponse(
                body is null ? null : BinaryData.FromString(body))));

        var page = await HealthModelPaginator.ReadPageAsync(source, null, CancellationToken.None);

        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ReadPageAsync_ThrowsOnUnreadableBody_RatherThanReportingCompletePage()
    {
        var source = new RecordingAsyncPageable<int>(
            Page<int>.FromValues([1], null, ListResponse(null, rawBody: "not json")));

        await Assert.ThrowsAnyAsync<JsonException>(() =>
            HealthModelPaginator.ReadPageAsync(source, null, CancellationToken.None));
    }

    [Fact]
    public void RequestContent_ResumePreservesOpaqueMarkerAndDropsWindow()
    {
        const string marker = "opaque/+== marker";

        var history = HealthModelRequestContent.History(Start, End, 500, marker);
        Assert.Equal(marker, history.NextMarker);
        Assert.Equal(500, history.Top);
        Assert.Null(history.StartOn);
        Assert.Null(history.EndOn);

        var signal = HealthModelRequestContent.SignalHistory("cpu", Start, End, 250, marker);
        Assert.Equal(marker, signal.NextMarker);
        Assert.Equal(250, signal.Top);
        Assert.Null(signal.StartOn);
        Assert.Null(signal.EndOn);

        var annotations = HealthModelRequestContent.DataAnnotations(Start, End, 100, marker);
        Assert.Equal(marker, annotations.NextMarker);
        Assert.Equal(100, annotations.Top);
        Assert.Null(annotations.StartOn);
        Assert.Null(annotations.EndOn);
    }

    [Theory]
    [InlineData(null, "next")]
    [InlineData("input", null)]
    [InlineData("input", "next")]
    public void EnsureMarkerAdvanced_AllowsFirstCompleteAndAdvancedPages(string? input, string? output)
    {
        HealthModelPaginator.EnsureMarkerAdvanced(input, output);
    }

    [Fact]
    public void EnsureMarkerAdvanced_RejectsImmediateNonAdvancingMarker()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            HealthModelPaginator.EnsureMarkerAdvanced("same", "same"));

        Assert.Contains("did not advance", exception.Message);
        Assert.Contains("same", exception.Message);
    }

    private static Response ListResponse(string? nextLink, string? rawBody = null)
    {
        var body = rawBody ?? (nextLink is null
            ? """{"value":[]}"""
            : $$"""{"value":[],"nextLink":"{{nextLink}}"}""");

        return new StubResponse(BinaryData.FromString(body));
    }

    private sealed class StubResponse(BinaryData? content) : Response
    {
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = string.Empty;
        public override BinaryData Content => content!;

        public override void Dispose() { }
        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<global::Azure.Core.HttpHeader> EnumerateHeaders() => [];
        protected override bool TryGetHeader(string name, out string value)
        {
            value = string.Empty;
            return false;
        }
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            values = [];
            return false;
        }
    }

    private sealed class RecordingAsyncPageable<T>(Page<T> page) : AsyncPageable<T>
        where T : notnull
    {
        internal string? InputContinuationToken { get; private set; }

        public override async IAsyncEnumerable<Page<T>> AsPages(
            string? continuationToken = null,
            int? pageSizeHint = null)
        {
            InputContinuationToken = continuationToken;
            await Task.Yield();
            yield return page;
        }

        public override async IAsyncEnumerator<T> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            foreach (var item in page.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return item;
            }
        }
    }
}
