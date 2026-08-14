// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Nodes;
using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Changes;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// Turns change elements into the exact calls they imply, against a snapshot and nothing else. Being pure
/// is what makes a what-if trustworthy: the preview a caller reads is produced by the same code that
/// produces the calls, so it cannot describe one thing and do another.
/// </summary>
internal static class HealthModelChangePlanner
{
    private static readonly string[] RelationshipEndpoints = ["parentEntityName", "childEntityName"];

    private const string SignalDefinitionReference = "signalDefinitionName";

    /// <summary>
    /// Plans elements in input order. Each element is planned against the state the elements before it
    /// would leave behind, so a batch that creates an entity and then patches it is coherent.
    /// </summary>
    internal static ChangePlan Plan(
        IReadOnlyList<HealthModelChange> changes,
        IReadOnlyDictionary<PlanScope, HealthModelGraphSnapshot> snapshots)
    {
        var working = new Dictionary<PlanScope, HealthModelGraphSnapshot>(snapshots);
        var planned = new List<PlannedChange>(changes.Count);

        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];

            if (change is MalformedChange malformed)
            {
                planned.Add(Failure(index, change, null, malformed.Error));
                continue;
            }

            var scope = new PlanScope(change.ResourceGroup, change.HealthModel);
            if (string.IsNullOrWhiteSpace(scope.ResourceGroup) || string.IsNullOrWhiteSpace(scope.HealthModel))
            {
                planned.Add(Failure(index, change, change.ChangeKind, "resourceGroup and healthModel are required."));
                continue;
            }

            if (!working.TryGetValue(scope, out var snapshot))
            {
                planned.Add(Failure(index, change, change.ChangeKind,
                    $"No snapshot was read for health model '{scope.HealthModel}' in resource group '{scope.ResourceGroup}'."));
                continue;
            }

            var result = PlanChange(index, change, scope, snapshot, snapshots[scope]);
            planned.Add(result);

            if (result.Success && result.Targets.Count > 0)
            {
                working[scope] = Fold(snapshot, result.Targets);
            }
        }

        return new ChangePlan(planned);
    }

    /// <param name="snapshot">
    /// The working state: what the elements before this one would leave behind, which is what the element
    /// has to be planned against.
    /// </param>
    /// <param name="original">
    /// The state the batch started from, which is the only state the service is known to have actually
    /// held. A restore body is taken from here, never from <paramref name="snapshot"/>: the working state
    /// carries every earlier element's <em>desired</em> body, so restoring from it would PUT a body the
    /// service may never have had.
    /// </param>
    private static PlannedChange PlanChange(
        int index,
        HealthModelChange change,
        PlanScope scope,
        HealthModelGraphSnapshot snapshot,
        HealthModelGraphSnapshot original) => change switch
        {
            CreateChange create => PlanCreate(index, create, scope, snapshot),
            PatchChange patch => PlanPatch(index, patch, scope, snapshot, original),
            RenameChange rename => PlanRename(index, rename, scope, snapshot, original),
            DeleteChange delete => PlanDelete(index, delete, scope, snapshot),
            _ => Failure(index, change, null, $"Unsupported change kind '{change.Kind}'."),
        };

    private static PlannedChange PlanCreate(
        int index, CreateChange change, PlanScope scope, HealthModelGraphSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(change.Name))
        {
            return Failure(index, change, change.ChangeKind, "name is required.", snapshot);
        }

        if (!TryResourceBody(change.Resource, out var kind, out var properties, out var bodyError))
        {
            return Failure(index, change, change.ChangeKind, bodyError, snapshot);
        }

        var declared = new JsonObject { ["properties"] = properties };
        if (!HealthModelMergePatch.TryValidateWritable(declared, out var writableError))
        {
            return Failure(index, change, change.ChangeKind, writableError, snapshot);
        }

        var existing = snapshot.Find(kind, change.Name);

        // A create means "ensure this exists with at least these properties", so the declared body is folded
        // into what is already there rather than replacing it. A property the caller did not name is
        // don't-care, which is what keeps a re-apply a no-op against a service that fills its own defaults.
        // The fold is a merge patch, so an EXPLICIT null removes the property; only omission is don't-care.
        var desired = existing is null
            ? declared
            : HealthModelMergePatch.Apply(existing.Body, declared)!.AsObject();
        var diff = HealthModelMergePatch.Diff(existing?.Body, desired);
        var action = existing is null
            ? HealthModelChangeAction.Create
            : diff.Count == 0 ? HealthModelChangeAction.NoOp : HealthModelChangeAction.Update;

        var target = new PlannedTarget(
            scope,
            kind,
            change.Name,
            action,
            diff,
            action == HealthModelChangeAction.NoOp
                ? []
                : [new PlannedWrite(kind, change.Name, PlannedOperation.Put, desired)],
            Dependencies(kind, desired));

        return Success(index, change, [target], snapshot);
    }

    private static PlannedChange PlanPatch(
        int index,
        PatchChange change,
        PlanScope scope,
        HealthModelGraphSnapshot snapshot,
        HealthModelGraphSnapshot original)
    {
        if (change.Patch?.Properties is null)
        {
            return Failure(index, change, change.ChangeKind, "patch.properties is required.", snapshot);
        }

        var patch = new JsonObject { ["properties"] = change.Patch.Properties.DeepClone() };
        if (!HealthModelMergePatch.TryValidateWritable(patch, out var writableError))
        {
            return Failure(index, change, change.ChangeKind, writableError, snapshot);
        }

        if (!TryResolve(change.Select, change.AllowEmptyMatch, snapshot, out var match, out var selectError))
        {
            return Failure(index, change, change.ChangeKind, selectError, snapshot);
        }

        var targets = new List<PlannedTarget>(match.Matches.Count);
        foreach (var resource in match.Matches)
        {
            var desired = HealthModelMergePatch.Apply(resource.Body, patch)!.AsObject();
            var diff = HealthModelMergePatch.Diff(resource.Body, desired);

            var action = diff.Count == 0
                ? HealthModelChangeAction.NoOp
                : RepointsRelationship(match.Kind, diff)
                    ? HealthModelChangeAction.Replace
                    : HealthModelChangeAction.Update;

            targets.Add(new PlannedTarget(
                scope,
                match.Kind,
                resource.Name,
                action,
                diff,
                Writes(action, match.Kind, resource.Name, desired),
                Dependencies(match.Kind, desired),
                original.Find(match.Kind, resource.Name)?.Body));
        }

        return Success(index, change, targets, snapshot);
    }

    private static PlannedChange PlanRename(
        int index,
        RenameChange change,
        PlanScope scope,
        HealthModelGraphSnapshot snapshot,
        HealthModelGraphSnapshot original)
    {
        if (string.IsNullOrWhiteSpace(change.NewName))
        {
            return Failure(index, change, change.ChangeKind, "newName is required.", snapshot);
        }

        if (!TryResolve(change.Select, change.AllowEmptyMatch, snapshot, out var match, out var selectError))
        {
            return Failure(index, change, change.ChangeKind, selectError, snapshot);
        }

        if (match.Matches.Count == 0)
        {
            return Success(index, change, [], snapshot);
        }

        if (match.Matches.Count > 1)
        {
            return Failure(index, change, change.ChangeKind,
                $"selector {match.Description} matched {match.Matches.Count} targets; a rename names exactly one.",
                snapshot);
        }

        var old = match.Matches[0];
        if (snapshot.Find(match.Kind, change.NewName) is not null)
        {
            return Failure(index, change, change.ChangeKind,
                $"'{change.NewName}' already exists; rename it or delete it first.", snapshot);
        }

        var created = new JsonObject { ["properties"] = old.Body["properties"]?.DeepClone() ?? new JsonObject() };
        var repoints = new List<PlannedDependency>();
        var targets = new List<PlannedTarget>
        {
            new(scope, match.Kind, change.NewName, HealthModelChangeAction.Create,
                HealthModelMergePatch.Diff(null, created),
                [new PlannedWrite(match.Kind, change.NewName, PlannedOperation.Put, created)],
                Dependencies(match.Kind, created)),
        };

        if (match.Kind == HealthModelResourceKind.Entity)
        {
            foreach (var relationship in snapshot.Relationships)
            {
                var repointed = relationship.Body.DeepClone().AsObject();
                var properties = repointed["properties"] as JsonObject;
                var touched = false;
                foreach (var endpoint in RelationshipEndpoints)
                {
                    if (properties?[endpoint]?.GetValue<string>() == old.Name)
                    {
                        properties[endpoint] = change.NewName;
                        touched = true;
                    }
                }

                if (!touched)
                {
                    continue;
                }

                repoints.Add(new PlannedDependency(HealthModelResourceKind.Relationship, relationship.Name));
                targets.Add(new PlannedTarget(
                    scope,
                    HealthModelResourceKind.Relationship,
                    relationship.Name,
                    HealthModelChangeAction.Replace,
                    HealthModelMergePatch.Diff(relationship.Body, repointed),
                    Writes(HealthModelChangeAction.Replace, HealthModelResourceKind.Relationship, relationship.Name, repointed),
                    Dependencies(HealthModelResourceKind.Relationship, repointed),
                    original.Find(HealthModelResourceKind.Relationship, relationship.Name)?.Body));
            }
        }

        // The order of the writes already puts the repoints first, but order alone is not a guarantee: if a
        // repoint fails, deleting the old name anyway strands the edge that still points at it. The delete
        // therefore depends on every repoint, so a failed one skips it.
        targets.Add(new PlannedTarget(
            scope, match.Kind, old.Name, HealthModelChangeAction.Delete, [],
            [new PlannedWrite(match.Kind, old.Name, PlannedOperation.Delete, null)], repoints));

        return Success(index, change, targets, snapshot);
    }

    private static PlannedChange PlanDelete(
        int index, DeleteChange change, PlanScope scope, HealthModelGraphSnapshot snapshot)
    {
        if (!TryResolve(change.Select, change.AllowEmptyMatch, snapshot, out var match, out var selectError))
        {
            return Failure(index, change, change.ChangeKind, selectError, snapshot);
        }

        if (match.Kind == HealthModelResourceKind.Entity)
        {
            var names = match.Matches.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            var stranded = snapshot.Relationships
                .Where(relationship => RelationshipEndpoints.Any(endpoint =>
                    relationship.Body["properties"]?[endpoint]?.GetValue<string>() is { } value && names.Contains(value)))
                .Select(relationship => relationship.Name)
                .ToList();

            if (stranded.Count > 0)
            {
                return Failure(index, change, change.ChangeKind,
                    $"relationship(s) {string.Join(", ", stranded)} still reference the selected entities; " +
                    "delete them in an earlier element rather than relying on a cascade.",
                    snapshot);
            }
        }

        var targets = match.Matches
            .Select(resource => new PlannedTarget(
                scope, match.Kind, resource.Name, HealthModelChangeAction.Delete, [],
                [new PlannedWrite(match.Kind, resource.Name, PlannedOperation.Delete, null)], []))
            .ToList();

        return Success(index, change, targets, snapshot);
    }

    private static IReadOnlyList<PlannedWrite> Writes(
        HealthModelChangeAction action, HealthModelResourceKind kind, string name, JsonObject desired) => action switch
        {
            HealthModelChangeAction.NoOp => [],
            HealthModelChangeAction.Replace =>
            [
                new PlannedWrite(kind, name, PlannedOperation.Delete, null),
                new PlannedWrite(kind, name, PlannedOperation.Put, desired),
            ],
            _ => [new PlannedWrite(kind, name, PlannedOperation.Put, desired)],
        };

    private static bool RepointsRelationship(
        HealthModelResourceKind kind, IReadOnlyList<HealthModelPropertyChange> diff) =>
        kind == HealthModelResourceKind.Relationship &&
        diff.Any(change => RelationshipEndpoints.Any(endpoint => change.Path == $"properties.{endpoint}"));

    private static IReadOnlyList<PlannedDependency> Dependencies(HealthModelResourceKind kind, JsonObject desired)
    {
        var properties = desired["properties"] as JsonObject;

        return kind switch
        {
            HealthModelResourceKind.Relationship =>
            [
                .. RelationshipEndpoints
                    .Select(endpoint => properties?[endpoint]?.GetValue<string>())
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .Select(name => new PlannedDependency(HealthModelResourceKind.Entity, name)),
            ],
            // A signal instance may borrow its defaults from a model-scope signal definition, so writing
            // the entity before that definition exists is the MissingSignalDefinition failure.
            HealthModelResourceKind.Entity =>
            [
                .. SignalDefinitionNames(properties?["signalGroups"])
                    .Distinct(StringComparer.Ordinal)
                    .Select(name => new PlannedDependency(HealthModelResourceKind.SignalDefinition, name)),
            ],
            _ => [],
        };
    }

    /// <summary>
    /// Collects every <c>signalDefinitionName</c> under a signal-group subtree. The walk is shape-agnostic
    /// because the groups differ per signal kind and newer API versions may add more of them.
    /// </summary>
    private static IEnumerable<string> SignalDefinitionNames(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var name in array.SelectMany(SignalDefinitionNames))
                {
                    yield return name;
                }
                break;

            case JsonObject jsonObject:
                foreach (var (key, value) in jsonObject)
                {
                    if (key == SignalDefinitionReference && value?.GetValue<string>() is { Length: > 0 } reference)
                    {
                        yield return reference;
                        continue;
                    }

                    foreach (var name in SignalDefinitionNames(value))
                    {
                        yield return name;
                    }
                }
                break;
        }
    }

    private static bool TryResolve(
        HealthModelSelector? selector,
        bool? allowEmptyMatch,
        HealthModelGraphSnapshot snapshot,
        out SelectorMatch match,
        out string error)
    {
        if (!HealthModelSelectorMatcher.TryMatch(selector, snapshot, out match, out error))
        {
            return false;
        }

        if (match.Matches.Count == 0 && allowEmptyMatch != true)
        {
            error = $"selector {match.Description} matched 0 targets; " +
                "set allowEmptyMatch:true to accept an empty match.";
            return false;
        }

        return true;
    }

    private static bool TryResourceBody(
        HealthModelResourceBody? body,
        out HealthModelResourceKind kind,
        out JsonNode properties,
        out string error)
    {
        kind = default;
        properties = null!;
        error = string.Empty;

        var declared = new List<(HealthModelResourceKind Kind, string Name, HealthModelResourceDocument Document)>(3);
        if (body?.Entity is { } entity)
        {
            declared.Add((HealthModelResourceKind.Entity, "entity", entity));
        }
        if (body?.Relationship is { } relationship)
        {
            declared.Add((HealthModelResourceKind.Relationship, "relationship", relationship));
        }
        if (body?.SignalDefinition is { } signalDefinition)
        {
            declared.Add((HealthModelResourceKind.SignalDefinition, "signalDefinition", signalDefinition));
        }

        if (declared.Count != 1)
        {
            error = declared.Count == 0
                ? "resource must name exactly one of 'entity', 'relationship' or 'signalDefinition'."
                : $"resource names {declared.Count} kinds; it must name exactly one.";
            return false;
        }

        if (declared[0].Document.Properties is not { } declaredProperties)
        {
            error = $"resource.{declared[0].Name}.properties is required.";
            return false;
        }

        kind = declared[0].Kind;
        properties = declaredProperties.DeepClone();
        return true;
    }

    private static HealthModelGraphSnapshot Fold(
        HealthModelGraphSnapshot snapshot, IReadOnlyList<PlannedTarget> targets)
    {
        var collections = new Dictionary<HealthModelResourceKind, List<HealthModelResourceSnapshot>>
        {
            [HealthModelResourceKind.Entity] = [.. snapshot.Entities],
            [HealthModelResourceKind.Relationship] = [.. snapshot.Relationships],
            [HealthModelResourceKind.SignalDefinition] = [.. snapshot.SignalDefinitions],
        };

        foreach (var write in targets.SelectMany(target => target.Writes))
        {
            var collection = collections[write.Kind];
            var position = collection.FindIndex(r => string.Equals(r.Name, write.Name, StringComparison.Ordinal));

            if (write.Operation == PlannedOperation.Delete)
            {
                if (position >= 0)
                {
                    collection.RemoveAt(position);
                }
                continue;
            }

            var updated = new HealthModelResourceSnapshot(write.Kind, write.Name, write.Body!.DeepClone().AsObject());
            if (position >= 0)
            {
                collection[position] = updated;
            }
            else
            {
                collection.Add(updated);
            }
        }

        return snapshot with
        {
            Entities = collections[HealthModelResourceKind.Entity],
            Relationships = collections[HealthModelResourceKind.Relationship],
            SignalDefinitions = collections[HealthModelResourceKind.SignalDefinition],
        };
    }

    private static PlannedChange Success(
        int index, HealthModelChange change, IReadOnlyList<PlannedTarget> targets, HealthModelGraphSnapshot snapshot) =>
        new(index, change.Label, change.Kind, change.ChangeKind, true, null, targets, snapshot.Counts);

    private static PlannedChange Failure(
        int index,
        HealthModelChange change,
        HealthModelChangeKind? kind,
        string error,
        HealthModelGraphSnapshot? snapshot = null) =>
        new(index, change.Label, change.Kind, kind, false, error, [], snapshot?.Counts ?? new HealthModelScanCounts(0, 0, 0));
}
