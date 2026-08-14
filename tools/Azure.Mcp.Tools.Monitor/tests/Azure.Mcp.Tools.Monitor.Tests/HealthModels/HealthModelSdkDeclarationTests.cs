// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using Azure.Mcp.Tools.Monitor.Sandbox;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

public class HealthModelScriptNormalizerTests
{
    private static readonly FakeHealthModelSdkRunner Runner = new();

    /// <summary>
    /// The shapes a model actually emits for the same intent. Each must reach the engine as one runnable
    /// program producing the same answer, so the caller is never punished for wrapping its code.
    /// </summary>
    [Theory]
    [InlineData("return 40 + 2;")]
    [InlineData("```js\nreturn 40 + 2;\n```")]
    [InlineData("```javascript\nreturn 40 + 2;\n```")]
    [InlineData("```\nreturn 40 + 2;\n```")]
    [InlineData("async () => { return 40 + 2; }")]
    [InlineData("async () => { return 40 + 2; };")]
    [InlineData("() => { return 40 + 2; }")]
    [InlineData("export default async () => { return 40 + 2; }")]
    [InlineData("```js\nasync () => { return 40 + 2; }\n```")]
    public void Normalize_ProducesTheSameAnswer_ForEveryShapeAModelEmits(string code)
    {
        var result = HealthModelScriptRuntime.Run(code, Runner, Runner, Runner, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(42, result.Result!.GetValue<int>());
    }

    [Fact]
    public void Normalize_KeepsAwaitUsable_InABareStatementBody()
    {
        var result = HealthModelScriptRuntime.Run(
            "const page = await client.entities.listByHealthModel('rg', 'model');\nreturn page.value.length;",
            Runner, Runner, Runner, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(3, result.Result!.GetValue<int>());
    }

    [Fact]
    public void Normalize_Throws_OnNullInput()
        => Assert.Throws<ArgumentNullException>(() => HealthModelScriptNormalizer.Normalize(null!));
}

/// <summary>
/// The option value is a JavaScript string, so the generated MCP input schema can say no more than
/// <c>"type": "string"</c> and this declaration is the only contract a client sees. The expected member
/// set is transcribed from <c>@azure/arm-cloudhealth</c> 1.0.0-beta.3's published
/// <c>dist/esm/classic/*/index.d.ts</c>, not from the prelude, so a rename on either side fails the test.
/// </summary>
public class HealthModelSdkDeclarationTests
{
    private static readonly string[] EntityOperations =
    [
        "get", "listByHealthModel", "getHistory", "getSignalHistory", "getSignalRecommendations",
        "getDataAnnotations", "addDataAnnotation", "ingestHealthReport", "createOrUpdate", "delete",
    ];

    [Theory]
    [InlineData("healthModels")]
    [InlineData("entities")]
    [InlineData("relationships")]
    [InlineData("signalDefinitions")]
    public void Declaration_AndPrelude_BothCarryEachOperationGroup(string group)
    {
        Assert.Contains($"{group}: {{", HealthModelSdkDeclaration.Declaration);
        Assert.Contains($"{group}: {{", HealthModelSdkPrelude.Source);
    }

    [Fact]
    public void Declaration_AndPrelude_CarryEverySdkEntityOperation()
    {
        foreach (var operation in EntityOperations)
        {
            Assert.Contains($"{operation}(", HealthModelSdkDeclaration.Declaration);
            Assert.Contains($"{operation}: (", HealthModelSdkPrelude.Source);
        }
    }

    /// <summary>
    /// Positional argument order is the part a caller's SDK familiarity actually depends on, so it is
    /// asserted verbatim rather than by member name alone.
    /// </summary>
    [Theory]
    [InlineData("get(resourceGroupName: string, healthModelName: string, entityName: string)")]
    [InlineData("listByHealthModel(resourceGroupName: string, healthModelName: string, options?: PageOptions)")]
    [InlineData("addDataAnnotation(resourceGroupName: string, healthModelName: string, entityName: string, body: DataAnnotationWrite)")]
    public void Declaration_MirrorsTheSdksArgumentOrder(string signature)
        => Assert.Contains(signature, HealthModelSdkDeclaration.Declaration);

    /// <summary>
    /// The declaration is the whole contract a client sees, so a type it names but never defines leaves the
    /// caller inventing field names. That is not hypothetical: a script written against this surface read
    /// `point.value` on a signal-history point, got nulls for 4,000 points, and reported no error, because
    /// `SignalHistoryResponse` was a name with no shape behind it.
    /// </summary>
    [Fact]
    public void Declaration_DefinesEveryTypeItReferences()
    {
        // Bound the scan to the TypeScript region. The prose around it uses colons in ordinary sentences
        // and the trailing example is JavaScript, so scanning either would flag words as type names.
        var full = HealthModelSdkDeclaration.Declaration;
        var start = full.IndexOf("declare const client", StringComparison.Ordinal);
        var end = full.IndexOf("Entity and health-model names", StringComparison.Ordinal);
        var declared = full[start..end];

        // Comments carry ordinary sentences, colons included, so strip them before looking for type names.
        var text = Regex.Replace(Regex.Replace(declared, @"/\*\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", " ");

        var defined = Regex.Matches(text, @"(?:type|interface)\s+([A-Za-z][A-Za-z0-9]*)")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        var builtIn = new HashSet<string>(StringComparer.Ordinal)
        {
            "Promise", "string", "number", "boolean", "object", "void", "null", "T", "Array",
        };

        var referenced = Regex.Matches(text, @"(?:Promise<|:\s*)([A-Za-z][A-Za-z0-9]*)(?:<([A-Za-z][A-Za-z0-9]*)>)?")
            .SelectMany(m => new[] { m.Groups[1].Value, m.Groups[2].Value })
            .Where(name => name.Length > 0 && !builtIn.Contains(name))
            .ToHashSet(StringComparer.Ordinal);

        var undefined = referenced.Except(defined).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(undefined.Count == 0, "declaration names types it never defines: " + string.Join(", ", undefined));
    }

    /// <summary>
    /// A body typed `object` tells a caller nothing, and the service is the only thing that knows which
    /// values are legal. Both enums below were learned by having a write rejected.
    /// </summary>
    [Theory]
    [InlineData("'Standard' | 'Limited' | 'Suppressed'")]
    [InlineData("'WorstOf' | 'MinHealthy' | 'MaxNotHealthy'")]
    public void Declaration_StatesClosedValueSetsInline(string enumeration)
        => Assert.Contains(enumeration, HealthModelSdkDeclaration.Declaration);

    [Fact]
    public void Declaration_TypesEveryWriteBody_RatherThanTakingBareObject()
    {
        var signatures = Regex.Matches(HealthModelSdkDeclaration.Declaration, @"^\s+[a-zA-Z]+\(.*$", RegexOptions.Multiline)
            .Select(m => m.Value)
            .Where(line => line.Contains(": object", StringComparison.Ordinal))
            .ToList();

        Assert.True(signatures.Count == 0, "write signatures still take a bare object: " + string.Join(" | ", signatures));
    }

    [Fact]
    public void Declaration_StatesTheExecutionBudgetACallerMustPlanFor()
    {
        var text = HealthModelSdkDeclaration.Declaration;
        Assert.Contains("30s", text);
        Assert.Contains("statement", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Promise.all", text);
        Assert.Contains("azureCalls", text);
    }

    /// <summary>
    /// A ceiling just above the measured size, so growth has to be argued for rather than accumulating.
    /// </summary>
    [Fact]
    public void Declaration_StaysWithinItsSizeBudget()
        => Assert.True(HealthModelSdkDeclaration.Declaration.Length < 9000,
            $"declaration is {HealthModelSdkDeclaration.Declaration.Length} characters");

    [Fact]
    public void Declaration_StatesTheSandboxHasNoNetworkOrCredentials()
    {
        Assert.Contains("NO network", HealthModelSdkDeclaration.Declaration);
        Assert.Contains("no access to credentials", HealthModelSdkDeclaration.Declaration);
    }
}
