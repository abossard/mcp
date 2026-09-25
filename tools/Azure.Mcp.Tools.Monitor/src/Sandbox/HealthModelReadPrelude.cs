// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.Monitor.Sandbox;

internal static class HealthModelReadPrelude
{
    public const string Source = """
        const console = {
          log: (...a) => __ch_log('', a),
          info: (...a) => __ch_log('', a),
          warn: (...a) => __ch_log('[warn] ', a),
          error: (...a) => __ch_log('[error] ', a),
        };

        const client = {
          healthModels: {
            get: async (resourceGroupName, healthModelName) =>
              JSON.parse(await __ch_healthModels_get(resourceGroupName, healthModelName)),
            listByResourceGroup: async (resourceGroupName, options) =>
              JSON.parse(await __ch_healthModels_listByResourceGroup(resourceGroupName, JSON.stringify(options ?? null))),
            listBySubscription: async (options) =>
              JSON.parse(await __ch_healthModels_listBySubscription(JSON.stringify(options ?? null))),
          },
          entities: {
            get: async (resourceGroupName, healthModelName, entityName) =>
              JSON.parse(await __ch_entities_get(resourceGroupName, healthModelName, entityName)),
            listByHealthModel: async (resourceGroupName, healthModelName, options) =>
              JSON.parse(await __ch_entities_listByHealthModel(resourceGroupName, healthModelName, JSON.stringify(options ?? null))),
            getHistory: async (resourceGroupName, healthModelName, entityName, body) =>
              JSON.parse(await __ch_entities_getHistory(resourceGroupName, healthModelName, entityName, JSON.stringify(body ?? null))),
            getSignalHistory: async (resourceGroupName, healthModelName, entityName, body) =>
              JSON.parse(await __ch_entities_getSignalHistory(resourceGroupName, healthModelName, entityName, JSON.stringify(body ?? null))),
            getSignalRecommendations: async (resourceGroupName, healthModelName, entityName) =>
              JSON.parse(await __ch_entities_getSignalRecommendations(resourceGroupName, healthModelName, entityName)),
            getDataAnnotations: async (resourceGroupName, healthModelName, entityName, body) =>
              JSON.parse(await __ch_entities_getDataAnnotations(resourceGroupName, healthModelName, entityName, JSON.stringify(body ?? null))),
          },
          relationships: {
            get: async (resourceGroupName, healthModelName, relationshipName) =>
              JSON.parse(await __ch_relationships_get(resourceGroupName, healthModelName, relationshipName)),
            listByHealthModel: async (resourceGroupName, healthModelName, options) =>
              JSON.parse(await __ch_relationships_listByHealthModel(resourceGroupName, healthModelName, JSON.stringify(options ?? null))),
          },
          signalDefinitions: {
            get: async (resourceGroupName, healthModelName, signalDefinitionName) =>
              JSON.parse(await __ch_signalDefinitions_get(resourceGroupName, healthModelName, signalDefinitionName)),
            listByHealthModel: async (resourceGroupName, healthModelName, options) =>
              JSON.parse(await __ch_signalDefinitions_listByHealthModel(resourceGroupName, healthModelName, JSON.stringify(options ?? null))),
          },
          authenticationSettings: {
            get: async (resourceGroupName, healthModelName, authenticationSettingName) =>
              JSON.parse(await __ch_authenticationSettings_get(resourceGroupName, healthModelName, authenticationSettingName)),
            listByHealthModel: async (resourceGroupName, healthModelName, options) =>
              JSON.parse(await __ch_authenticationSettings_listByHealthModel(resourceGroupName, healthModelName, JSON.stringify(options ?? null))),
          },
          discoveryRules: {
            get: async (resourceGroupName, healthModelName, discoveryRuleName) =>
              JSON.parse(await __ch_discoveryRules_get(resourceGroupName, healthModelName, discoveryRuleName)),
            listByHealthModel: async (resourceGroupName, healthModelName, options) =>
              JSON.parse(await __ch_discoveryRules_listByHealthModel(resourceGroupName, healthModelName, JSON.stringify(options ?? null))),
          },
          operations: {
            list: async () =>
              JSON.parse(await __ch_operations_list()),
          },
        };
        """;
}
