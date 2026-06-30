// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.HealthModels.Models;
using Xunit;

namespace Azure.Mcp.Tools.HealthModels.Tests;

public class HealthModelsInputTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseDictionary_ReturnsNull_ForEmptyInput(string? input)
    {
        Assert.Null(HealthModelsInput.ParseDictionary(input));
    }

    [Fact]
    public void ParseDictionary_ParsesJsonObject()
    {
        var result = HealthModelsInput.ParseDictionary("""{"env":"prod","tier":"frontend"}""");

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal("prod", result["env"]);
        Assert.Equal("frontend", result["tier"]);
    }

    [Fact]
    public void ParseDictionary_Throws_WhenNotAnObject()
    {
        Assert.Throws<ArgumentException>(() => HealthModelsInput.ParseDictionary("""["a","b"]"""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ParseList_ReturnsNull_ForEmptyInput(string? input)
    {
        Assert.Null(HealthModelsInput.ParseList(input));
    }

    [Fact]
    public void ParseList_SplitsTrimsAndDropsEmptyEntries()
    {
        var result = HealthModelsInput.ParseList(" id1 , id2 ,, id3 ");

        Assert.NotNull(result);
        Assert.Equal(new[] { "id1", "id2", "id3" }, result);
    }
}
