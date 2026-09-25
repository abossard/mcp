// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Sandbox;

internal static class HealthModelReadDeclaration
{
    public const string Declaration = """
        Write JavaScript that runs in a read-only sandbox for Azure Monitor Health Models.
        The API surface below is the complete allowlist. Every method returns a real Promise.
        Use await for dependencies and Promise.all / Promise.allSettled for overlap and per-call handling.

        declare const client: {
          healthModels: {
            get(resourceGroup: string, healthModel: string): Promise<HealthModel>;
            listByResourceGroup(resourceGroup: string, options?: CursorOptions): Promise<Page<HealthModel>>;
            listBySubscription(options?: CursorOptions): Promise<Page<HealthModel>>;
          };
          entities: {
            get(resourceGroup: string, healthModel: string, entity: string): Promise<Entity>;
            listByHealthModel(resourceGroup: string, healthModel: string, options?: AsOfPageOptions): Promise<Page<Entity>>;
            getHistory(resourceGroup: string, healthModel: string, entity: string, body?: HistoryRequest): Promise<HistoryResponse>;
            getSignalHistory(resourceGroup: string, healthModel: string, entity: string, body: SignalHistoryRequest): Promise<SignalHistoryResponse>;
            getSignalRecommendations(resourceGroup: string, healthModel: string, entity: string): Promise<SignalRecommendations>;
            getDataAnnotations(resourceGroup: string, healthModel: string, entity: string, body?: HistoryRequest): Promise<DataAnnotations>;
          };
          relationships: {
            get(resourceGroup: string, healthModel: string, relationship: string): Promise<Relationship>;
            listByHealthModel(resourceGroup: string, healthModel: string, options?: AsOfPageOptions): Promise<Page<Relationship>>;
          };
          signalDefinitions: {
            get(resourceGroup: string, healthModel: string, signalDefinition: string): Promise<SignalDefinition>;
            listByHealthModel(resourceGroup: string, healthModel: string, options?: AsOfPageOptions): Promise<Page<SignalDefinition>>;
          };
          authenticationSettings: {
            get(resourceGroup: string, healthModel: string, authenticationSetting: string): Promise<AuthenticationSetting>;
            listByHealthModel(resourceGroup: string, healthModel: string, options?: CursorOptions): Promise<Page<AuthenticationSetting>>;
          };
          discoveryRules: {
            get(resourceGroup: string, healthModel: string, discoveryRule: string): Promise<DiscoveryRule>;
            listByHealthModel(resourceGroup: string, healthModel: string, options?: AsOfPageOptions): Promise<Page<DiscoveryRule>>;
          };
          operations: {
            list(): Promise<OperationList>;
          };
        };

        type Page<T> = { value: T[]; nextLink: string | null };
        type CursorOptions = { cursor?: string };
        type AsOfPageOptions = { asOf?: string; cursor?: string };
        type HistoryRequest = { startTime?: string; endTime?: string; top?: number; nextMarker?: string };
        type SignalHistoryRequest = { signalName: string; startTime?: string; endTime?: string; top?: number; nextMarker?: string };

        type HealthModel = {
          id: string;
          name: string;
          type: string;
          location: string;
          properties?: { provisioningState?: string };
        };
        type Entity = { name?: string; properties?: { healthState?: string } };
        type Relationship = { name?: string; properties?: { parentEntityName?: string; childEntityName?: string } };
        type SignalDefinition = { name?: string; properties?: { signalKind?: string } };
        type AuthenticationSetting = { name?: string };
        type DiscoveryRule = { name?: string };
        type OperationList = { value?: { name?: string }[]; nextLink?: string | null };

        type HistoryResponse = {
          entityName?: string;
          history?: { previousState?: string; newState?: string; occurredAt?: string; reason?: string }[];
          nextMarker?: string;
        };
        type SignalHistoryResponse = {
          entityName?: string;
          signalName?: string;
          history?: { occurredAt?: string; healthState?: string; additionalContext?: string; value?: number }[];
          nextMarker?: string;
        };
        type SignalRecommendations = {
          recommendedSignals?: unknown[];
          recommendedConfigurations?: unknown[];
        };
        type DataAnnotations = {
          entityName?: string;
          annotations?: { annotationId?: string; annotationDetails?: Record<string, unknown>; description?: string; createdAt?: string }[];
          nextMarker?: string;
        };

        No write APIs are exposed in this mode.
        healthModels.listByResourceGroup and healthModels.listBySubscription accept options.cursor only (no options.asOf).
        options.cursor must stay on the same ARM collection and api-version.
        options.cursor cannot be combined with options.asOf.
        body.nextMarker cannot be combined with body.startTime/body.endTime.
        Budget: 30s execution timeout, max depth 64, max 5,000,000 statements, max result size 24,000 chars.
        Read calls run with a four-request concurrency gate.
        """;
}
