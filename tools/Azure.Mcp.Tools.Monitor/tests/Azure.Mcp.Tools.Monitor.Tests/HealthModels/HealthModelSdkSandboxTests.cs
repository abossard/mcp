// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Azure;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Sandbox;
using Xunit;

namespace Azure.Mcp.Tools.Monitor.Tests.HealthModels;

/// <summary>
/// The sandbox is the only thing standing between caller-authored JavaScript and the host process, so
/// these tests assert on what the engine actually does rather than on how it was configured: a test that
/// read <c>Interop.Enabled == false</c> would keep passing after a later binding leaked a CLR object.
/// </summary>
public class HealthModelSdkSandboxTests
{
    private static HealthModelScriptResultAssertion Run(string code)
        => Run(code, new FakeHealthModelSdkRunner());

    private static HealthModelScriptResultAssertion Run(string code, FakeHealthModelSdkRunner runner)
    {
        var result = HealthModelScriptRuntime.Run(code, runner, runner, runner, CancellationToken.None);
        return new HealthModelScriptResultAssertion(result, runner.Calls);
    }

    /// <summary>
    /// The verbatim shape of an Azure SDK failure: the service's own sentence, then the decorated status
    /// line, a body echo and every response header. Roughly a kilobyte for one actionable line.
    /// </summary>
    private static RequestFailedException ArmFailure() => new(
        403,
        """
        The client 'someone@example.com' with object id '8fd16e4b' does not have authorization to perform action 'Microsoft.CloudHealth/healthmodels/entities/read'.
        Status: 403 (Forbidden)
        ErrorCode: AuthorizationFailed

        Content:
        {"error":{"code":"AuthorizationFailed","message":"The client does not have authorization."}}

        Headers:
        Cache-Control: no-cache
        x-ms-request-id: REDACTED
        Date: Wed, 12 Aug 2026 06:34:23 GMT
        """,
        "AuthorizationFailed",
        null);

    internal sealed record HealthModelScriptResultAssertion(
        HealthModelScriptResult Result, List<string> Calls);

    [Fact]
    public void Run_ReturnsValueAndCapturedLogs_ForAScriptThatUsesTheClient()
    {
        var run = Run("""
            const page = await client.entities.listByHealthModel('rg', 'model');
            const unhealthy = page.value.filter(e => e.properties.healthState !== 'Healthy');
            console.log('scanned', page.value.length);
            return { total: page.value.length, unhealthy: unhealthy.length, names: unhealthy.map(e => e.name) };
            """);

        Assert.Null(run.Result.Error);
        Assert.Equal(3, run.Result.Result!["total"]!.GetValue<int>());
        Assert.Equal(2, run.Result.Result["unhealthy"]!.GetValue<int>());
        Assert.Equal(["api", "db"], run.Result.Result["names"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(["scanned 3"], run.Result.Logs);
        Assert.Single(run.Calls);
    }

    [Fact]
    public void Run_RoutesEachClientCallToTheRunner_WithTheScriptsArguments()
    {
        var run = Run("""
            await client.entities.get('rg', 'model', 'api');
            await client.entities.getHistory('rg', 'model', 'db', { top: 5 });
            await client.entities.getSignalHistory('rg', 'model', 'web', { signalName: 'cpu' });
            await client.healthModels.get('rg', 'model');
            return 'done';
            """);

        Assert.Null(run.Result.Error);
        Assert.Equal(
            [
                "getEntity:rg/model/api",
                "getHistory:db:top=5:from=-",
                "getSignalHistory:web:cpu",
                "getHealthModel:rg/model",
            ],
            run.Calls);
    }

    [Fact]
    public void Run_ExposesWriteOperations_AndForwardsTheScriptsBody()
    {
        var run = Run("""
            await client.entities.createOrUpdate('rg', 'model', 'cache', { properties: { displayName: 'Cache' } });
            await client.entities.addDataAnnotation('rg', 'model', 'cache', { reason: 'deploy' });
            await client.entities.ingestHealthReport('rg', 'model', 'cache', { signalName: 'cpu' });
            await client.relationships.delete('rg', 'model', 'web-to-api');
            return 'written';
            """);

        Assert.Null(run.Result.Error);
        Assert.Equal("written", run.Result.Result!.GetValue<string>());
        Assert.Equal(
            [
                """put:Entity:cache:{"properties":{"displayName":"Cache"}}""",
                """addDataAnnotation:cache:{"reason":"deploy"}""",
                """ingestHealthReport:cache:{"signalName":"cpu"}""",
                "delete:Relationship:web-to-api",
            ],
            run.Calls);
    }

    [Fact]
    public void Run_EchoesNextLinkAsCursor_SoPagingIsCallerDriven()
    {
        var run = Run("""
            const first = await client.entities.listByHealthModel('rg', 'model');
            const second = await client.entities.listByHealthModel('rg', 'model', { cursor: first.nextLink });
            return { firstLink: first.nextLink, secondLink: second.nextLink };
            """);

        Assert.Equal("page2", run.Result.Result!["firstLink"]!.GetValue<string>());
        Assert.Null(run.Result.Result["secondLink"]);
        Assert.Equal("listEntities:rg/model:asOf=-:cursor=page2", run.Calls[1]);
    }

    [Theory]
    [InlineData("System.IO.File")]
    [InlineData("importNamespace('System')")]
    [InlineData("clrHelper")]
    [InlineData("({}).GetType()")]
    public void Run_FailsTheScript_WhenItReachesForTheClr(string expression)
    {
        var run = Run($"return {expression};");

        Assert.NotNull(run.Result.Error);
        Assert.Null(run.Result.Result);
    }

    [Theory]
    [InlineData("fetch")]
    [InlineData("XMLHttpRequest")]
    [InlineData("require")]
    [InlineData("process")]
    [InlineData("importScripts")]
    [InlineData("WebSocket")]
    public void Run_ExposesNoHostOrNetworkGlobal(string name)
    {
        var run = Run($"return typeof {name};");

        Assert.Null(run.Result.Error);
        Assert.Equal("undefined", run.Result.Result!.GetValue<string>());
    }

    /// <summary>
    /// The script can build a function with the Function constructor — that is ordinary JavaScript and not
    /// an escape, because the sandbox's security rests on what is bound rather than on blocking dynamic
    /// code. What must hold is that dynamically built code reaches the CLR no more than static code does.
    /// </summary>
    [Fact]
    public void Run_KeepsTheClrOutOfReach_EvenThroughTheFunctionConstructor()
    {
        var run = Run(""""
            const build = this.constructor.constructor;
            return build("return [typeof System, typeof importNamespace, typeof clrHelper].join(',')")();
            """");

        Assert.Null(run.Result.Error);
        Assert.Equal("undefined,undefined,undefined", run.Result.Result!.GetValue<string>());
    }

    [Fact]
    public void Run_ExposesNoCredentialBearingGlobal()
    {
        var run = Run("""
            const named = Object.getOwnPropertyNames(globalThis);
            return { client: typeof client, suspicious: named.filter(n => /credential|armclient|pipeline|token|subscription/i.test(n)) };
            """);

        Assert.Equal("object", run.Result.Result!["client"]!.GetValue<string>());
        Assert.Empty(run.Result.Result["suspicious"]!.AsArray());
    }

    [Fact]
    public void Run_TerminatesANonYieldingLoop_WithinTheTimeout()
    {
        var elapsed = Stopwatch.StartNew();
        var run = Run("while (true) {}");
        elapsed.Stop();

        Assert.NotNull(run.Result.Error);
        Assert.True(
            elapsed.Elapsed < HealthModelScriptRuntime.Timeout + TimeSpan.FromSeconds(15),
            $"script ran for {elapsed.Elapsed} against a {HealthModelScriptRuntime.Timeout} limit");
    }

    /// <summary>
    /// An SDK failure inside a script used to escape the sandbox entirely and become a whole-command
    /// failure, taking the script's logs with it. The logs are the only record of how far a partially
    /// applied write batch got, so losing them costs exactly the diagnosis a caller needs most.
    /// </summary>
    [Fact]
    public void Run_KeepsTheScriptContract_WhenAnAzureCallFails()
    {
        var runner = new FakeHealthModelSdkRunner { ThrowOn = ArmFailure, ThrowOnCall = "getEntity" };
        var run = Run("""
            console.log('step 1 done');
            console.log('step 2 done');
            await client.entities.get('rg', 'model', 'missing');
            return 'unreachable';
            """, runner);

        Assert.Equal(["step 1 done", "step 2 done"], run.Result.Logs);
        Assert.NotNull(run.Result.Error);
        Assert.Null(run.Result.Result);
    }

    [Fact]
    public void Run_ReportsAnAzureFailure_WithTheServiceSentenceAndNoTransportNoise()
    {
        var runner = new FakeHealthModelSdkRunner { ThrowOn = ArmFailure, ThrowOnCall = "getEntity" };
        var run = Run("await client.entities.get('rg', 'model', 'missing'); return 1;", runner);

        var error = run.Result.Error!;
        Assert.Contains("does not have authorization", error);
        Assert.Contains("403", error);
        Assert.Contains("AuthorizationFailed", error);
        Assert.DoesNotContain("at Jint.", error);
        Assert.DoesNotContain("Headers:", error);
        Assert.DoesNotContain("Cache-Control", error);
        Assert.True(error.Length < 300, $"error was {error.Length} chars: {error}");
    }

    [Fact]
    public void Run_KeepsWritesThatSucceededBeforeAFailure_Visible()
    {
        var runner = new FakeHealthModelSdkRunner { ThrowOn = ArmFailure, ThrowOnCall = "put" };
        var run = Run("""
            console.log('about to write');
            await client.entities.createOrUpdate('rg', 'model', 'one', { properties: { displayName: 'One' } });
            return 'unreachable';
            """, runner);

        Assert.Equal(["about to write"], run.Result.Logs);
        Assert.NotNull(run.Result.Error);
        Assert.Single(run.Calls);
    }

    /// <summary>
    /// A body read back from the service echoes properties the resource provider owns, and the write
    /// endpoint rejects them. Stripping them at the bridge is what lets a script round-trip a resource
    /// without a hand-written delete for each one.
    /// </summary>
    [Fact]
    public void Run_StripsServerOwnedProperties_FromAWriteBody()
    {
        var run = Run("""
            await client.entities.createOrUpdate('rg', 'model', 'one', {
              id: '/subscriptions/x/resourceGroups/rg/providers/Microsoft.CloudHealth/healthmodels/model/entities/one',
              name: 'one',
              type: 'Microsoft.CloudHealth/healthmodels/entities',
              systemData: { createdAt: '2026-01-01T00:00:00Z' },
              properties: {
                displayName: 'One',
                provisioningState: 'Succeeded',
                healthState: 'Healthy',
                discoveredBy: 'rule-1',
                signalGroups: { azureResource: { signals: [{ name: 'keep-me' }] } }
              }
            });
            return 'written';
            """);

        Assert.Null(run.Result.Error);
        var body = run.Calls.Single();

        foreach (var owned in new[] { "\"id\"", "\"type\"", "\"systemData\"", "\"provisioningState\"", "\"healthState\"", "\"discoveredBy\"" })
        {
            Assert.DoesNotContain(owned, body);
        }

        Assert.Contains("\"displayName\":\"One\"", body);
        // `name` below the two anchored levels is caller data, so the signal keeps its own name.
        Assert.Contains("keep-me", body);
    }

    /// <summary>
    /// `healthState` is server-owned on an entity and caller-owned on a health report: the report exists to
    /// carry it. Stripping by name across every write silently deleted the one field that operation is for,
    /// which is the same silent-field-loss failure this work set out to remove.
    /// </summary>
    [Fact]
    public void Run_KeepsHealthState_OnAHealthReport()
    {
        var run = Run("""
            await client.entities.ingestHealthReport('rg', 'model', 'cache',
              { signalName: 'cpu', healthState: 'Degraded', additionalContext: 'spiked' });
            return 'reported';
            """);

        Assert.Null(run.Result.Error);
        var body = run.Calls.Single();
        Assert.Contains("\"healthState\":\"Degraded\"", body);
        Assert.Contains("\"signalName\":\"cpu\"", body);
    }

    [Fact]
    public void Run_KeepsCallerFields_OnADataAnnotation()
    {
        var run = Run("""
            await client.entities.addDataAnnotation('rg', 'model', 'cache',
              { annotationDetails: 'deploy 42', description: 'rollout' });
            return 'annotated';
            """);

        Assert.Null(run.Result.Error);
        var body = run.Calls.Single();
        Assert.Contains("annotationDetails", body);
        Assert.Contains("description", body);
    }

    /// <summary>
    /// A cancelled request must abort rather than resolve into a 200 carrying an error string. Jint reports
    /// an observed cancellation with its own exception type, which is not an OperationCanceledException, so
    /// excluding only that base type would let a cancelled run look like an ordinary script failure.
    /// </summary>
    [Fact]
    public void Run_PropagatesCancellation_RatherThanReportingItAsAScriptError()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var runner = new FakeHealthModelSdkRunner();

        Assert.ThrowsAny<OperationCanceledException>(
            () => HealthModelScriptRuntime.Run("let i = 0; while (i < 1e9) { i++; } return i;", runner, runner, runner, cancelled.Token));
    }

    [Fact]
    public void Run_ReportsHowManyAzureCallsCompleted_WhenAScriptSucceeds()
    {
        var run = Run("""
            await client.entities.listByHealthModel('rg', 'model');
            await client.entities.get('rg', 'model', 'api');
            await client.relationships.listByHealthModel('rg', 'model');
            return 'done';
            """);

        Assert.Equal(run.Calls.Count, run.Result.AzureCalls);
        Assert.Equal(3, run.Result.AzureCalls);
    }

    [Fact]
    public void Run_ReportsProgress_WhenAScriptHitsALimit()
    {
        var run = Run("""
            await client.entities.listByHealthModel('rg', 'model');
            await client.entities.get('rg', 'model', 'api');
            while (true) {}
            """);

        Assert.NotNull(run.Result.Error);
        Assert.Contains("limit", run.Result.Error);
        Assert.Equal(2, run.Result.AzureCalls);
    }

    [Fact]
    public void Run_ReportsTheError_AndKeepsWhatWasLoggedBeforeItFailed()
    {
        var run = Run("""
            console.log('before');
            throw new Error('boom');
            """);

        Assert.Contains("boom", run.Result.Error);
        Assert.Equal(["before"], run.Result.Logs);
        Assert.Null(run.Result.Result);
    }

    [Fact]
    public void Run_TruncatesAnOversizedResult_AndDoesNotReturnTheWholePayload()
    {
        var run = Run($"return 'x'.repeat({HealthModelScriptOutput.MaxChars * 2});");

        Assert.True(run.Result.Truncated);
        var text = run.Result.Result!.GetValue<string>();
        Assert.Contains(HealthModelScriptOutput.TruncationMarker, text);
        Assert.True(
            text.Length < HealthModelScriptOutput.MaxChars * 2,
            "the untruncated payload was returned despite the truncation marker");
    }

    [Fact]
    public void Run_ReturnsNullResult_ForAScriptThatReturnsNothing()
    {
        var run = Run("console.log('quiet');");

        Assert.Null(run.Result.Error);
        Assert.Null(run.Result.Result);
        Assert.Equal(["quiet"], run.Result.Logs);
    }
}
