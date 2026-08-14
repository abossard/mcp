// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Monitor.Models.HealthModels;
using Azure.Mcp.Tools.Monitor.Models.HealthModels.Queries;

namespace Azure.Mcp.Tools.Monitor.Planning;

/// <summary>
/// Flattens a typed <see cref="HealthModelQuery"/> into the inputs the planner works with, and maps each
/// kind's own selection enum onto the internal field groups the result converters read.
/// </summary>
/// <remarks>
/// This is the only place that knows how a concrete query type maps onto call inputs. Because each kind
/// carries only the inputs it accepts, there is nothing here to validate beyond the two rules the type
/// system cannot express: a target must name exactly one thing, and a cursor resumes a page rather than
/// re-running a window.
/// </remarks>
internal static class HealthModelQueryNormalizer
{
    internal static bool TryNormalize(HealthModelQuery query, out QueryInputs inputs, out string error)
    {
        inputs = default!;
        error = string.Empty;

        if (query is MalformedQuery malformed)
        {
            error = malformed.Error;
            return false;
        }

        if (string.IsNullOrWhiteSpace(query.ResourceGroup) || string.IsNullOrWhiteSpace(query.HealthModel))
        {
            error = "resourceGroup and healthModel are required.";
            return false;
        }

        inputs = query switch
        {
            EntityListQuery q => new QueryInputs(
                AsOf: q.AsOf, WhereHealth: q.WhereHealth, Cursor: q.Page?.Cursor,
                Shape: Shape(q.Select, MapEntity)),

            EntityGetQuery q => new QueryInputs(
                EntityName: q.Entity, Shape: Shape(q.Select, MapEntity)),

            EntityHistoryQuery q => new QueryInputs(
                EntityName: q.Target?.Entity, WhereHealth: q.Target?.WhereHealth,
                From: q.Window?.From, To: q.Window?.To, Size: q.Page?.Size, Cursor: q.Page?.Cursor,
                Shape: Shape(q.Select, MapFull)),

            SignalHistoryQuery q => new QueryInputs(
                EntityName: q.Target?.Entity, WhereHealth: q.Target?.WhereHealth, SignalName: q.Signal,
                From: q.Window?.From, To: q.Window?.To, Size: q.Page?.Size, Cursor: q.Page?.Cursor,
                Shape: Shape(q.Select, MapSignalHistory)),

            SignalRecommendationsQuery q => new QueryInputs(
                EntityName: q.Target?.Entity, WhereHealth: q.Target?.WhereHealth,
                Shape: Shape(q.Select, MapRecommendation)),

            DataAnnotationsQuery q => new QueryInputs(
                EntityName: q.Target?.Entity, WhereHealth: q.Target?.WhereHealth,
                From: q.Window?.From, To: q.Window?.To, Size: q.Page?.Size, Cursor: q.Page?.Cursor,
                Shape: Shape(q.Select, MapAnnotation)),

            RelationshipListQuery q => new QueryInputs(
                AsOf: q.AsOf, Cursor: q.Page?.Cursor, Shape: Shape(q.Select, MapFull)),

            SignalDefinitionListQuery q => new QueryInputs(
                AsOf: q.AsOf, Cursor: q.Page?.Cursor, Shape: Shape(q.Select, MapFull)),

            _ => throw new ArgumentOutOfRangeException(nameof(query), query.GetType(), "Unsupported query type."),
        };

        return TryValidate(query, inputs, out error);
    }

    /// <summary>
    /// The two rules the shape cannot enforce. Everything else a caller could previously get wrong is now
    /// either impossible to express or rejected by name at deserialization.
    /// </summary>
    private static bool TryValidate(HealthModelQuery query, QueryInputs inputs, out string error)
    {
        var kind = query.QueryKind;

        if (kind == HealthModelQueryKind.EntityGet)
        {
            if (string.IsNullOrWhiteSpace(inputs.EntityName))
            {
                error = "entity is required for entityGet.";
                return false;
            }
        }
        else if (RequiresTarget(kind))
        {
            var hasEntity = !string.IsNullOrWhiteSpace(inputs.EntityName);
            var hasFilter = inputs.WhereHealth is not null;
            if (hasEntity == hasFilter)
            {
                error = "target must name exactly one of entity or whereHealth.";
                return false;
            }
        }

        if (kind == HealthModelQueryKind.SignalHistory && string.IsNullOrWhiteSpace(inputs.SignalName))
        {
            error = "signal is required for signalHistory.";
            return false;
        }

        if (inputs.Cursor is not null && (inputs.From is not null || inputs.To is not null))
        {
            error = "page.cursor resumes a page and cannot be combined with window; " +
                "send the cursor alone to continue where the previous response stopped.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool RequiresTarget(HealthModelQueryKind kind) =>
        kind is HealthModelQueryKind.EntityHistory or HealthModelQueryKind.SignalHistory
            or HealthModelQueryKind.SignalRecommendations or HealthModelQueryKind.DataAnnotations;

    private static HealthModelResultShape Shape<T>(IReadOnlyList<T>? selection, Func<T, HealthModelFieldGroup> map) =>
        selection is null ? HealthModelResultShape.Compact : HealthModelResultShape.From(selection.Select(map));

    private static HealthModelFieldGroup MapEntity(EntitySelection selection) => selection switch
    {
        EntitySelection.Identity => HealthModelFieldGroup.Identity,
        EntitySelection.Audit => HealthModelFieldGroup.Audit,
        EntitySelection.Signals => HealthModelFieldGroup.Signals,
        EntitySelection.Layout => HealthModelFieldGroup.Layout,
        _ => HealthModelFieldGroup.Full,
    };

    private static HealthModelFieldGroup MapSignalHistory(SignalHistorySelection selection) =>
        selection == SignalHistorySelection.Context ? HealthModelFieldGroup.Context : HealthModelFieldGroup.Full;

    private static HealthModelFieldGroup MapRecommendation(RecommendationSelection selection) =>
        selection == RecommendationSelection.Configurations
            ? HealthModelFieldGroup.Configurations
            : HealthModelFieldGroup.Full;

    private static HealthModelFieldGroup MapAnnotation(AnnotationSelection selection) =>
        selection == AnnotationSelection.Details ? HealthModelFieldGroup.Details : HealthModelFieldGroup.Full;

    private static HealthModelFieldGroup MapFull(FullSelection selection) => HealthModelFieldGroup.Full;
}
