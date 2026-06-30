// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.HealthModels.Commands.AuthenticationSetting;
using Azure.Mcp.Tools.HealthModels.Commands.DiscoveryRule;
using Azure.Mcp.Tools.HealthModels.Commands.Entity;
using Azure.Mcp.Tools.HealthModels.Commands.HealthModel;
using Azure.Mcp.Tools.HealthModels.Commands.Identity;
using Azure.Mcp.Tools.HealthModels.Commands.Relationship;
using Azure.Mcp.Tools.HealthModels.Commands.SignalDefinition;
using Azure.Mcp.Tools.HealthModels.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Mcp.Core.Areas;
using Microsoft.Mcp.Core.Commands;

namespace Azure.Mcp.Tools.HealthModels;

public sealed class HealthModelsSetup : IAreaSetup
{
    public string Name => "healthmodels";

    public string Title => "Azure Health Models";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IHealthModelsService, HealthModelsService>();

        services.AddSingleton<HealthModelListCommand>();
        services.AddSingleton<HealthModelGetCommand>();
        services.AddSingleton<HealthModelCreateCommand>();
        services.AddSingleton<HealthModelUpdateCommand>();
        services.AddSingleton<HealthModelDeleteCommand>();

        services.AddSingleton<EntityListCommand>();
        services.AddSingleton<EntityGetCommand>();
        services.AddSingleton<EntityCreateCommand>();
        services.AddSingleton<EntityUpdateCommand>();
        services.AddSingleton<EntityDeleteCommand>();
        services.AddSingleton<EntityGetHistoryCommand>();
        services.AddSingleton<EntityGetSignalHistoryCommand>();
        services.AddSingleton<EntityGetSignalRecommendationCommand>();
        services.AddSingleton<EntityAddDataAnnotationCommand>();
        services.AddSingleton<EntityGetDataAnnotationsCommand>();
        services.AddSingleton<EntityIngestHealthReportCommand>();

        services.AddSingleton<SignalDefinitionListCommand>();
        services.AddSingleton<SignalDefinitionGetCommand>();
        services.AddSingleton<SignalDefinitionCreateCommand>();
        services.AddSingleton<SignalDefinitionUpdateCommand>();
        services.AddSingleton<SignalDefinitionDeleteCommand>();

        services.AddSingleton<RelationshipListCommand>();
        services.AddSingleton<RelationshipGetCommand>();
        services.AddSingleton<RelationshipCreateCommand>();
        services.AddSingleton<RelationshipUpdateCommand>();
        services.AddSingleton<RelationshipDeleteCommand>();

        services.AddSingleton<DiscoveryRuleListCommand>();
        services.AddSingleton<DiscoveryRuleGetCommand>();
        services.AddSingleton<DiscoveryRuleCreateCommand>();
        services.AddSingleton<DiscoveryRuleUpdateCommand>();
        services.AddSingleton<DiscoveryRuleDeleteCommand>();

        services.AddSingleton<AuthenticationSettingListCommand>();
        services.AddSingleton<AuthenticationSettingGetCommand>();
        services.AddSingleton<AuthenticationSettingCreateCommand>();
        services.AddSingleton<AuthenticationSettingUpdateCommand>();
        services.AddSingleton<AuthenticationSettingDeleteCommand>();

        services.AddSingleton<IdentityShowCommand>();
        services.AddSingleton<IdentityAssignCommand>();
        services.AddSingleton<IdentityRemoveCommand>();
    }

    public CommandGroup RegisterCommands(IServiceProvider serviceProvider)
    {
        var root = new CommandGroup(
            Name,
            "Azure Health Models operations - Commands for managing Azure Health Models (Microsoft.CloudHealth/healthmodels) resources, entities, signal definitions, relationships, discovery rules, authentication settings, and managed identity.",
            Title);

        root.AddCommand<HealthModelListCommand>(serviceProvider);
        root.AddCommand<HealthModelGetCommand>(serviceProvider);
        root.AddCommand<HealthModelCreateCommand>(serviceProvider);
        root.AddCommand<HealthModelUpdateCommand>(serviceProvider);
        root.AddCommand<HealthModelDeleteCommand>(serviceProvider);

        var entity = new CommandGroup("entity", "Entity operations - Commands for managing entities within a health model.");
        root.AddSubGroup(entity);
        entity.AddCommand<EntityListCommand>(serviceProvider);
        entity.AddCommand<EntityGetCommand>(serviceProvider);
        entity.AddCommand<EntityCreateCommand>(serviceProvider);
        entity.AddCommand<EntityUpdateCommand>(serviceProvider);
        entity.AddCommand<EntityDeleteCommand>(serviceProvider);
        entity.AddCommand<EntityGetHistoryCommand>(serviceProvider);
        entity.AddCommand<EntityGetSignalHistoryCommand>(serviceProvider);
        entity.AddCommand<EntityGetSignalRecommendationCommand>(serviceProvider);
        entity.AddCommand<EntityAddDataAnnotationCommand>(serviceProvider);
        entity.AddCommand<EntityGetDataAnnotationsCommand>(serviceProvider);
        entity.AddCommand<EntityIngestHealthReportCommand>(serviceProvider);

        var signalDefinition = new CommandGroup("signal-definition", "Signal definition operations - Commands for managing signal definitions within a health model.");
        root.AddSubGroup(signalDefinition);
        signalDefinition.AddCommand<SignalDefinitionListCommand>(serviceProvider);
        signalDefinition.AddCommand<SignalDefinitionGetCommand>(serviceProvider);
        signalDefinition.AddCommand<SignalDefinitionCreateCommand>(serviceProvider);
        signalDefinition.AddCommand<SignalDefinitionUpdateCommand>(serviceProvider);
        signalDefinition.AddCommand<SignalDefinitionDeleteCommand>(serviceProvider);

        var relationship = new CommandGroup("relationship", "Relationship operations - Commands for managing relationships within a health model.");
        root.AddSubGroup(relationship);
        relationship.AddCommand<RelationshipListCommand>(serviceProvider);
        relationship.AddCommand<RelationshipGetCommand>(serviceProvider);
        relationship.AddCommand<RelationshipCreateCommand>(serviceProvider);
        relationship.AddCommand<RelationshipUpdateCommand>(serviceProvider);
        relationship.AddCommand<RelationshipDeleteCommand>(serviceProvider);

        var discoveryRule = new CommandGroup("discovery-rule", "Discovery rule operations - Commands for managing discovery rules within a health model.");
        root.AddSubGroup(discoveryRule);
        discoveryRule.AddCommand<DiscoveryRuleListCommand>(serviceProvider);
        discoveryRule.AddCommand<DiscoveryRuleGetCommand>(serviceProvider);
        discoveryRule.AddCommand<DiscoveryRuleCreateCommand>(serviceProvider);
        discoveryRule.AddCommand<DiscoveryRuleUpdateCommand>(serviceProvider);
        discoveryRule.AddCommand<DiscoveryRuleDeleteCommand>(serviceProvider);

        var authenticationSetting = new CommandGroup("authentication-setting", "Authentication setting operations - Commands for managing authentication settings within a health model.");
        root.AddSubGroup(authenticationSetting);
        authenticationSetting.AddCommand<AuthenticationSettingListCommand>(serviceProvider);
        authenticationSetting.AddCommand<AuthenticationSettingGetCommand>(serviceProvider);
        authenticationSetting.AddCommand<AuthenticationSettingCreateCommand>(serviceProvider);
        authenticationSetting.AddCommand<AuthenticationSettingUpdateCommand>(serviceProvider);
        authenticationSetting.AddCommand<AuthenticationSettingDeleteCommand>(serviceProvider);

        var identity = new CommandGroup("identity", "Identity operations - Commands for managing the managed identity of a health model.");
        root.AddSubGroup(identity);
        identity.AddCommand<IdentityShowCommand>(serviceProvider);
        identity.AddCommand<IdentityAssignCommand>(serviceProvider);
        identity.AddCommand<IdentityRemoveCommand>(serviceProvider);

        return root;
    }
}
