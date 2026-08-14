// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Sandbox;

/// <summary>
/// The TypeScript declaration for the sandbox globals, published as the <c>--code</c> option description.
/// It is documentation for the caller and is never executed: the option value is a JavaScript string, so
/// the generated MCP input schema can say no more than <c>"type": "string"</c> and this is the only
/// contract a client sees. Names and positional argument order mirror
/// <c>@azure/arm-cloudhealth</c> 1.0.0-beta.3's <c>CloudHealthClient</c>.
/// </summary>
/// <remarks>
/// Response shapes carry the fields a script reads rather than a transcription of the SDK model, and say
/// so. A type name with no shape behind it is what makes a caller invent a field: reading
/// <c>point.value</c> on a signal-history point yields undefined for every point and reports no error.
/// </remarks>
internal static class HealthModelSdkDeclaration
{
    public const string Declaration = """
        Write JavaScript that runs in a sandbox where the authenticated Azure CloudHealth SDK is already
        bound to your subscription. Return a value; it becomes the result. console.log is captured.
        There is NO network, NO filesystem, NO require/import, and no access to credentials. Prefer
        filtering and aggregating inside the script over returning whole pages.

        declare const client: {
          healthModels: {
            get(resourceGroupName: string, healthModelName: string): Promise<HealthModel>;
            listByResourceGroup(resourceGroupName: string, options?: PageOptions): Promise<Page<HealthModel>>;
            listBySubscription(options?: PageOptions): Promise<Page<HealthModel>>;
          };
          entities: {
            get(resourceGroupName: string, healthModelName: string, entityName: string): Promise<Entity>;
            listByHealthModel(resourceGroupName: string, healthModelName: string, options?: PageOptions): Promise<Page<Entity>>;
            getHistory(resourceGroupName: string, healthModelName: string, entityName: string, body?: HistoryRequest): Promise<HistoryResponse>;
            getSignalHistory(resourceGroupName: string, healthModelName: string, entityName: string, body: SignalHistoryRequest): Promise<SignalHistoryResponse>;
            getSignalRecommendations(resourceGroupName: string, healthModelName: string, entityName: string): Promise<SignalRecommendations>;
            getDataAnnotations(resourceGroupName: string, healthModelName: string, entityName: string, body?: HistoryRequest): Promise<DataAnnotations>;
            addDataAnnotation(resourceGroupName: string, healthModelName: string, entityName: string, body: DataAnnotationWrite): Promise<DataAnnotation>;
            ingestHealthReport(resourceGroupName: string, healthModelName: string, entityName: string, body: HealthReportWrite): Promise<void>;
            createOrUpdate(resourceGroupName: string, healthModelName: string, entityName: string, resource: EntityWrite): Promise<void>;
            delete(resourceGroupName: string, healthModelName: string, entityName: string): Promise<void>;
          };
          relationships: {
            listByHealthModel(resourceGroupName: string, healthModelName: string, options?: PageOptions): Promise<Page<Relationship>>;
            createOrUpdate(resourceGroupName: string, healthModelName: string, relationshipName: string, resource: RelationshipWrite): Promise<void>;
            delete(resourceGroupName: string, healthModelName: string, relationshipName: string): Promise<void>;
          };
          signalDefinitions: {
            listByHealthModel(resourceGroupName: string, healthModelName: string, options?: PageOptions): Promise<Page<SignalDefinition>>;
            createOrUpdate(resourceGroupName: string, healthModelName: string, signalDefinitionName: string, resource: SignalDefinitionWrite): Promise<void>;
            delete(resourceGroupName: string, healthModelName: string, signalDefinitionName: string): Promise<void>;
          };
        };

        /** One Azure page. Nothing is aggregated silently: echo nextLink as options.cursor to continue. */
        type Page<T> = { value: T[]; nextLink: string | null };
        type PageOptions = { asOf?: string; cursor?: string };
        type HistoryRequest = { startTime?: string; endTime?: string; top?: number; nextMarker?: string };
        type SignalHistoryRequest = { signalName: string; startTime?: string; endTime?: string; top?: number; nextMarker?: string };

        type HealthState = 'Healthy' | 'Degraded' | 'Unhealthy' | 'Unknown';
        type Impact = 'Standard' | 'Limited' | 'Suppressed';
        type Aggregation = 'WorstOf' | 'MinHealthy' | 'MaxNotHealthy';
        type Operator = 'Dynamic' | 'Equal' | 'GreaterThan' | 'GreaterThanOrEqual' | 'LessThan' | 'LessThanOrEqual' | 'NotEqual';

        // Read shapes list the fields scripts use. A payload carries further service fields; log one to see them.
        type HealthModel = { name: string; location: string; properties: { provisioningState: string } };
        type Entity = { name: string; properties: EntityProperties };
        type EntityProperties = {
          displayName?: string; impact?: Impact; healthState?: HealthState; provisioningState?: string;
          canvasPosition?: { x: number; y: number }; signalGroups?: SignalGroups; discoveredBy?: string;
        };
        type SignalGroups = {
          dependencies?: { aggregationType: Aggregation; ignoreUnknown?: boolean; unhealthyThreshold?: number; degradedThreshold?: number };
          azureResource?: { azureResourceId?: string; signals?: { name: string }[]; resourceHealth?: { signalName?: string } };
        };
        type Relationship = { name: string; properties: { parentEntityName: string; childEntityName: string; displayName?: string } };
        type SignalDefinition = { name: string; properties: SignalDefinitionProperties };
        type SignalDefinitionProperties = {
          displayName?: string; signalKind: string; metricNamespace?: string; metricName?: string;
          aggregationType?: string; dataUnit?: string; timeGrain?: string; refreshInterval?: string;
          provisioningState?: string; evaluationRules: EvaluationRules;
        };
        type EvaluationRules = { degradedRule?: { operator: Operator; threshold: number }; unhealthyRule: { operator: Operator; threshold: number } };

        /** Entity state transitions. These carry no numeric field. */
        type HistoryResponse = { entityName: string; history: Transition[]; nextMarker?: string };
        type Transition = { previousState: HealthState; newState: HealthState; occurredAt: string; reason: string };
        /** Signal evaluations. Each point is a STATE, not a metric value: there is no value field. */
        type SignalHistoryResponse = { entityName: string; signalName: string; history: SignalPoint[]; nextMarker?: string };
        type SignalPoint = { occurredAt: string; healthState: HealthState; additionalContext: string };
        type SignalRecommendations = { recommendedSignals: { name?: string; signalKind?: string }[] };
        type DataAnnotations = { entityName: string; annotations: { occurredAt?: string; reason?: string }[]; nextMarker?: string };
        type DataAnnotation = { name?: string; properties?: { reason?: string } };

        // Resource writes: server-owned fields (id, name, type, systemData, provisioningState, healthState,
        // discoveredBy) are stripped before the call, so a body read back can be edited and written again.
        // The two action payloads below are NOT resources and are sent as written; a health report carries
        // its own healthState.
        type EntityWrite = { properties: { displayName?: string; impact?: Impact; healthObjective?: number; canvasPosition?: { x: number; y: number }; signalGroups?: SignalGroups } };
        type RelationshipWrite = { properties: { parentEntityName: string; childEntityName: string; displayName?: string } };
        type SignalDefinitionWrite = { properties: SignalDefinitionProperties };
        type DataAnnotationWrite = { annotationDetails: string; description?: string };
        type HealthReportWrite = { signalName: string; healthState: HealthState; value?: number; additionalContext?: string; expiresInMinutes?: number };

        Entity and health-model names must match ^[a-zA-Z0-9][a-zA-Z0-9-]{1,258}[a-zA-Z0-9]$, so at least
        three characters. Writes take effect immediately; there is no whatIf preview and no rollback, so a
        script that fails midway leaves earlier writes applied. The result reports azureCalls, the number of
        calls that completed, which tells you how far a partial run got.

        Budget: a script is bounded by a 30s wall clock, a 5,000,000 statement ceiling and a recursion cap.
        Host calls run one at a time, so Promise.all is accepted but does NOT run them concurrently. Plan
        for roughly 25 sequential Azure calls per run, and page across invocations for anything larger.
        Exceeding a limit returns an error plus the logs and azureCalls from before it stopped.

        Example — count unhealthy entities without returning them:
          const page = await client.entities.listByHealthModel('rg', 'model');
          const bad = page.value.filter(e => e.properties.healthState !== 'Healthy');
          console.log('scanned', page.value.length);
          return { unhealthy: bad.length, names: bad.map(e => e.name) };
        """;
}
