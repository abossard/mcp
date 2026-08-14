// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Sandbox;

/// <summary>
/// The JavaScript evaluated before the caller's code. It builds the <c>client</c> object out of the flat
/// host functions the bridge registers, and replaces <c>console</c> with a capturing shim. Assembling the
/// object graph in JavaScript rather than handing the engine a CLR object is what keeps the sandbox off
/// Jint's reflection-based interop path, which is not NativeAOT-safe.
/// </summary>
internal static class HealthModelSdkPrelude
{
    public const string Source = """
        const console = {
          log: (...a) => __ch_log('', a),
          info: (...a) => __ch_log('', a),
          warn: (...a) => __ch_log('[warn] ', a),
          error: (...a) => __ch_log('[error] ', a),
        };

        const __json = (v) => (v === undefined || v === null) ? null : JSON.stringify(v);

        const __page = (raw) => {
          const p = JSON.parse(raw);
          return { value: p.value, nextLink: p.nextLink };
        };

        const client = {
          healthModels: {
            get: (resourceGroupName, healthModelName) =>
              JSON.parse(__ch_healthModels_get(resourceGroupName, healthModelName)),
            listByResourceGroup: (resourceGroupName, options) =>
              __page(__ch_healthModels_list(resourceGroupName, __json(options))),
            listBySubscription: (options) =>
              __page(__ch_healthModels_list(null, __json(options))),
          },
          entities: {
            get: (resourceGroupName, healthModelName, entityName) =>
              JSON.parse(__ch_entities_get(resourceGroupName, healthModelName, entityName)),
            listByHealthModel: (resourceGroupName, healthModelName, options) =>
              __page(__ch_entities_list(resourceGroupName, healthModelName, __json(options))),
            getHistory: (resourceGroupName, healthModelName, entityName, body) =>
              JSON.parse(__ch_entities_getHistory(resourceGroupName, healthModelName, entityName, __json(body))),
            getSignalHistory: (resourceGroupName, healthModelName, entityName, body) =>
              JSON.parse(__ch_entities_getSignalHistory(resourceGroupName, healthModelName, entityName, __json(body))),
            getSignalRecommendations: (resourceGroupName, healthModelName, entityName) =>
              JSON.parse(__ch_entities_getSignalRecommendations(resourceGroupName, healthModelName, entityName)),
            getDataAnnotations: (resourceGroupName, healthModelName, entityName, body) =>
              JSON.parse(__ch_entities_getDataAnnotations(resourceGroupName, healthModelName, entityName, __json(body))),
            addDataAnnotation: (resourceGroupName, healthModelName, entityName, body) =>
              JSON.parse(__ch_entities_addDataAnnotation(resourceGroupName, healthModelName, entityName, __json(body))),
            ingestHealthReport: (resourceGroupName, healthModelName, entityName, body) =>
              __ch_entities_ingestHealthReport(resourceGroupName, healthModelName, entityName, __json(body)),
            createOrUpdate: (resourceGroupName, healthModelName, entityName, resource) =>
              __ch_write_put(resourceGroupName, healthModelName, 'entity', entityName, __json(resource)),
            delete: (resourceGroupName, healthModelName, entityName) =>
              __ch_write_delete(resourceGroupName, healthModelName, 'entity', entityName),
          },
          relationships: {
            listByHealthModel: (resourceGroupName, healthModelName, options) =>
              __page(__ch_relationships_list(resourceGroupName, healthModelName, __json(options))),
            createOrUpdate: (resourceGroupName, healthModelName, relationshipName, resource) =>
              __ch_write_put(resourceGroupName, healthModelName, 'relationship', relationshipName, __json(resource)),
            delete: (resourceGroupName, healthModelName, relationshipName) =>
              __ch_write_delete(resourceGroupName, healthModelName, 'relationship', relationshipName),
          },
          signalDefinitions: {
            listByHealthModel: (resourceGroupName, healthModelName, options) =>
              __page(__ch_signalDefinitions_list(resourceGroupName, healthModelName, __json(options))),
            createOrUpdate: (resourceGroupName, healthModelName, signalDefinitionName, resource) =>
              __ch_write_put(resourceGroupName, healthModelName, 'signalDefinition', signalDefinitionName, __json(resource)),
            delete: (resourceGroupName, healthModelName, signalDefinitionName) =>
              __ch_write_delete(resourceGroupName, healthModelName, 'signalDefinition', signalDefinitionName),
          },
        };
        """;
}
