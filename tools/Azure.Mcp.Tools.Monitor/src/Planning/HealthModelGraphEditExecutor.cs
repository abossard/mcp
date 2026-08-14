// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// Reads the model, plans the change set, and — only when explicitly authorised — writes it. The default
/// path issues no write at all, so the expensive mistake requires the caller to have read the preview and
/// declared the number of targets it contained.
/// </summary>
internal sealed class HealthModelGraphEditExecutor(IHealthModelWriteRunner runner)
{
    private const int MaxPages = 100;

    private static readonly HealthModelResourceKind[] Kinds =
    [
        HealthModelResourceKind.Entity,
        HealthModelResourceKind.Relationship,
        HealthModelResourceKind.SignalDefinition,
    ];

    internal async Task<HealthModelGraphEditResult> ExecuteAsync(
        IReadOnlyList<HealthModelChange> changes,
        HealthModelChangeMode mode,
        HealthModelChangeExpectation? expect,
        CancellationToken cancellationToken)
    {
        var snapshots = await ReadSnapshotsAsync(changes, cancellationToken);
        var plan = HealthModelChangePlanner.Plan(changes, snapshots);
        var affectedCount = plan.AffectedCount;
        var snapshotToken = SnapshotToken(plan, snapshots);
        var results = plan.Changes.Select(ToResult).ToList();

        var result = new HealthModelGraphEditResult
        {
            Mode = mode,
            Success = true,
            AffectedCount = affectedCount,
            Snapshot = snapshotToken,
            Changes = results,
        };

        if (mode != HealthModelChangeMode.Apply)
        {
            return result;
        }

        if (expect?.AffectedCount is not { } declared)
        {
            return Rejected(result,
                $"mode 'apply' requires expect.affectedCount. The change set affects {affectedCount} target(s); " +
                "re-run in whatIf mode, read affectedCount, and echo it back. No writes were issued.");
        }

        if (declared != affectedCount)
        {
            return Rejected(result,
                $"expect.affectedCount is {declared} but the change set affects {affectedCount} target(s). " +
                "No writes were issued.");
        }

        if (expect.Snapshot is { Length: > 0 } expected && !string.Equals(expected, snapshotToken, StringComparison.Ordinal))
        {
            return Rejected(result,
                $"expect.snapshot '{expected}' does not match the current state '{snapshotToken}'; the model " +
                "changed since the what-if. No writes were issued.");
        }

        await ApplyAsync(plan, results, cancellationToken);
        return result;
    }

    private static HealthModelGraphEditResult Rejected(HealthModelGraphEditResult result, string error)
    {
        result.Success = false;
        result.Error = error;
        return result;
    }

    private async Task<IReadOnlyDictionary<PlanScope, HealthModelGraphSnapshot>> ReadSnapshotsAsync(
        IReadOnlyList<HealthModelChange> changes, CancellationToken cancellationToken)
    {
        var scopes = changes
            .Where(change => change is not MalformedChange)
            .Where(change => !string.IsNullOrWhiteSpace(change.ResourceGroup) && !string.IsNullOrWhiteSpace(change.HealthModel))
            .Select(change => new PlanScope(change.ResourceGroup, change.HealthModel))
            .Distinct();

        var snapshots = new Dictionary<PlanScope, HealthModelGraphSnapshot>();
        foreach (var scope in scopes)
        {
            var collections = new Dictionary<HealthModelResourceKind, IReadOnlyList<HealthModelResourceSnapshot>>();
            foreach (var kind in Kinds)
            {
                collections[kind] = await ReadAllAsync(scope, kind, cancellationToken);
            }

            snapshots[scope] = new HealthModelGraphSnapshot(
                scope,
                collections[HealthModelResourceKind.Entity],
                collections[HealthModelResourceKind.Relationship],
                collections[HealthModelResourceKind.SignalDefinition]);
        }

        return snapshots;
    }

    private async Task<IReadOnlyList<HealthModelResourceSnapshot>> ReadAllAsync(
        PlanScope scope, HealthModelResourceKind kind, CancellationToken cancellationToken)
    {
        var all = new List<HealthModelResourceSnapshot>();
        string? continuationToken = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var current = await runner.ListAsync(scope, kind, continuationToken, cancellationToken);
            all.AddRange(current.Items);
            continuationToken = current.ContinuationToken;

            if (string.IsNullOrEmpty(continuationToken))
            {
                return all;
            }
        }

        throw new InvalidOperationException(
            $"Reading {kind} in '{scope.HealthModel}' did not finish within {MaxPages} pages; " +
            "the selector would have run against a partial view.");
    }

    /// <summary>
    /// Hashes the current state of the targets the plan touches — not the whole model — so unrelated drift
    /// does not block an apply while drift under the caller's feet does.
    /// </summary>
    private static string SnapshotToken(
        ChangePlan plan, IReadOnlyDictionary<PlanScope, HealthModelGraphSnapshot> snapshots)
    {
        var entries = plan.Changes
            .Where(change => change.Success)
            .SelectMany(change => change.Targets)
            .Select(target =>
            {
                var key = $"{target.Scope.ResourceGroup}/{target.Scope.HealthModel}/{target.Kind}/{target.Name}";
                var body = snapshots.TryGetValue(target.Scope, out var snapshot)
                    ? snapshot.Find(target.Kind, target.Name)?.Body.ToJsonString()
                    : null;
                return $"{key}={body ?? "<absent>"}";
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(entry => entry, StringComparer.Ordinal);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", entries)));
        return Convert.ToHexString(digest).ToLower(CultureInfo.InvariantCulture);
    }

    private async Task ApplyAsync(
        ChangePlan plan, List<HealthModelChangeResult> results, CancellationToken cancellationToken)
    {
        var ordered = plan.Changes
            .Where(change => change.Success)
            .SelectMany(change => change.Targets.Select((target, position) => (Change: change, Target: target, Position: position)))
            .Where(item => item.Target.Action != HealthModelChangeAction.NoOp)
            .OrderBy(item => item.Target.Phase)
            .ThenBy(item => item.Target.KindRank)
            .ToList();

        // Both are keyed by scope as well as name: a batch can span health models, and an entity called
        // 'api' in one model says nothing about an entity called 'api' in another. 'earlier' below means
        // earlier in this execution order, which is not the input order — deletes run last, and within a
        // phase signal definitions run before entities before relationships.
        var failed = new Dictionary<(PlanScope Scope, HealthModelResourceKind Kind, string Name), BlockedWrite>();

        // Every element that wrote the resource is kept, not only the most recent one. Each of them was
        // told its write succeeded, and a delete or a failed replace takes all of their content away in one
        // go, so keying this by the last writer alone would leave every earlier one reporting a success that
        // no longer stands — with nothing in the response to say otherwise.
        var committed = new Dictionary<(PlanScope Scope, HealthModelResourceKind Kind, string Name), List<CommittedWrite>>();

        foreach (var (change, target, position) in ordered)
        {
            var reported = results[change.ChangeIndex].Targets![position];
            var self = (target.Scope, target.Kind, target.Name);
            var resource = new PlannedDependency(target.Kind, target.Name).Description;
            var element = Element(change);

            // A resource whose earlier write failed is in an unknown state, so the body this element
            // computed was folded from a state the service may never have reached. Naming the earlier
            // element is what lets the caller fix the cause rather than the symptom.
            if (failed.TryGetValue(self, out var earlier))
            {
                Skip(reported, $"{earlier.ByElement(resource)}, so this target was not attempted.");
                continue;
            }

            var blocking = target.DependsOn
                .Select(dependency => (Dependency: dependency,
                    Blocker: failed.GetValueOrDefault((target.Scope, dependency.Kind, dependency.Name))))
                .FirstOrDefault(candidate => candidate.Blocker is not null);

            if (blocking.Blocker is { } blocker)
            {
                Skip(reported, $"{blocker.ByDependency(blocking.Dependency)}, so this target was not attempted.");
                // A target that was never written is itself a missing dependency for anything below it.
                failed[self] = new BlockedWrite(element, Attempted: false);
                continue;
            }

            var removed = false;
            var wrote = false;
            try
            {
                foreach (var write in target.Writes)
                {
                    if (write.Operation == PlannedOperation.Delete)
                    {
                        await runner.DeleteAsync(target.Scope, write.Kind, write.Name, cancellationToken);
                        removed = true;
                    }
                    else
                    {
                        await runner.PutAsync(target.Scope, write.Kind, write.Name, write.Body!, cancellationToken);
                        wrote = true;
                    }
                }

                if (wrote)
                {
                    // A successful write supersedes an earlier one without losing anything: the plan for
                    // this element was folded over that write, so its content is carried in this body. That
                    // is why the earlier writers are appended to rather than replaced — their content is
                    // still on the resource, and whatever removes this write removes theirs with it.
                    if (!committed.TryGetValue(self, out var writers))
                    {
                        committed[self] = writers = [];
                    }

                    writers.Add(new CommittedWrite(element, reported));
                }
                else if (committed.Remove(self, out var deleted))
                {
                    // A delete the caller asked for is allowed to remove the writes earlier elements made —
                    // but each of them was told its write succeeded, so each of them is told this too.
                    Supersede(deleted, $"{element} deleted {resource}");
                }
            }
            catch (Exception ex)
            {
                reported.Success = false;

                if (!removed)
                {
                    reported.Error = ex.Message;
                }
                else if (committed.Remove(self, out var superseded))
                {
                    // The write got as far as deleting the resource and then failed. Putting the pre-batch
                    // body back here would overwrite writes the caller was already told had succeeded, so
                    // the restore is skipped and the resource is left in the state this element left it.
                    // The error names the most recent writer, the one whose body was actually on the
                    // resource; every writer, including that one, is told on its own node.
                    var writer = superseded[^1].Element;
                    Supersede(superseded, $"{element} deleted {resource} and failed to re-create it");
                    reported.Error = SupersededRestore(target, ex, writer);
                }
                else
                {
                    reported.Error = await RestoreAsync(target, ex, cancellationToken);
                }

                failed[self] = new BlockedWrite(element, Attempted: true);
            }
        }
    }

    private static void Skip(HealthModelTargetResult reported, string reason)
    {
        reported.Action = HealthModelChangeAction.Skipped;
        reported.Success = false;
        reported.SkipReason = reason;
    }

    /// <summary>
    /// Tells every element that wrote the resource that its write is gone. All of them are annotated, not
    /// just the last: the earlier writes were never lost while the chain of writes stood, but whatever ends
    /// that chain — a delete, or a replace that failed after its delete half — takes all of their content
    /// away together.
    /// </summary>
    private static void Supersede(List<CommittedWrite> writers, string cause)
    {
        foreach (var writer in writers)
        {
            writer.Supersede(cause);
        }
    }

    private static string Element(PlannedChange change) =>
        change.Label is { Length: > 0 } label
            ? $"change {change.ChangeIndex} ('{label}')"
            : $"change {change.ChangeIndex}";

    /// <summary>
    /// Puts a resource back after a write sequence that had already removed it. A <c>replace</c> is a
    /// delete followed by a create, so a create that fails leaves the caller with less than they started
    /// with — from a change set that only asked to modify something. The body put back is the one the
    /// batch started from, and the caller of this method has already established that no other element
    /// committed a write over it, so a restore can only ever undo this element's own delete.
    /// </summary>
    /// <returns>
    /// The error to report: the original failure alone when the target had nothing to restore, the failure
    /// plus the restore when the resource is back, and an unmissable resource-lost error naming the
    /// resource when the restore failed too.
    /// </returns>
    private async Task<string> RestoreAsync(
        PlannedTarget target, Exception failure, CancellationToken cancellationToken)
    {
        if (target.Restore is not { } original)
        {
            return failure.Message;
        }

        var resource = $"{target.Kind} '{target.Name}'";
        try
        {
            await runner.PutAsync(target.Scope, target.Kind, target.Name, original, cancellationToken);
            return $"{failure.Message} The previous body of {resource} was restored, " +
                "so the target is unchanged.";
        }
        catch (Exception restoreFailure)
        {
            return $"RESOURCE LOST: {resource} was deleted, re-creating it failed ({failure.Message}), " +
                $"and restoring its previous body failed as well ({restoreFailure.Message}). " +
                "It no longer exists and its previous body is only in this response.";
        }
    }

    /// <summary>
    /// The error for a failed replace whose restore was skipped: an earlier element had already written the
    /// resource, so putting the pre-batch body back would have discarded that write. The write is allowed to
    /// proceed and only the restore is withheld, because blocking the write instead breaks the change sets
    /// that are correct — a rename repointing a relationship an earlier element also touched, say — to
    /// protect the ones that fail.
    /// </summary>
    /// <returns>
    /// The failure alone when there was nothing to restore anyway, and otherwise an unmissable error naming
    /// the write that was protected and stating that the resource is gone.
    /// </returns>
    private static string SupersededRestore(PlannedTarget target, Exception failure, string writer)
    {
        if (target.Restore is null)
        {
            return failure.Message;
        }

        var resource = $"{target.Kind} '{target.Name}'";
        return $"RESTORE SKIPPED: {resource} was deleted, re-creating it failed ({failure.Message}), and " +
            $"its pre-batch body was NOT put back because {writer} had already written {resource} — " +
            $"putting it back would have discarded that write. {resource} no longer exists, in the state " +
            "this element left it.";
    }

    private static HealthModelChangeResult ToResult(PlannedChange change) => new()
    {
        ChangeIndex = change.ChangeIndex,
        Label = change.Label,
        Kind = change.RawKind,
        Success = change.Success,
        Error = change.Error,
        Scanned = change.Scanned,
        Targets = change.Targets
            .Select(target => new HealthModelTargetResult
            {
                Name = target.Name,
                ResourceKind = target.Kind,
                Action = target.Action,
                Changes = target.Changes.Count == 0 ? null : target.Changes,
                Success = true,
            })
            .ToList(),
    };
}
