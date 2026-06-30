targetScope = 'resourceGroup'

@minLength(3)
@maxLength(44)
@description('The base resource name. Must start with a letter for Microsoft.CloudHealth/healthmodels.')
param baseName string = resourceGroup().name

@description('The location of the resource. By default, this is the same as the resource group.')
param location string = resourceGroup().location

@description('The client OID to grant access to test resources.')
param testApplicationOid string = deployer().objectId

resource healthModel 'Microsoft.CloudHealth/healthmodels@2026-05-01-preview' = {
  name: baseName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {}
}

// Grant the test application Contributor on the health model so it can read and manage child resources.
resource contributorRoleDefinition 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  scope: subscription()
  name: 'b24988ac-6180-42a0-ab88-20f7382dd24c'
}

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(healthModel.id, testApplicationOid, contributorRoleDefinition.id)
  scope: healthModel
  properties: {
    principalId: testApplicationOid
    roleDefinitionId: contributorRoleDefinition.id
    principalType: 'ServicePrincipal'
  }
}

output healthModelName string = healthModel.name
