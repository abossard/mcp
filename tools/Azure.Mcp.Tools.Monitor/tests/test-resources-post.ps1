param(
    [string] $TenantId,
    [string] $TestApplicationId,
    [string] $ResourceGroupName,
    [string] $BaseName,
    [hashtable] $DeploymentOutputs,
    [hashtable] $AdditionalParameters
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot/../../../eng/common/scripts/common.ps1"
. "$PSScriptRoot/../../../eng/scripts/helpers/TestResourcesHelpers.ps1"

$testSettings = New-TestSettings @PSBoundParameters -OutputPath $PSScriptRoot

# Add static deployment outputs
$staticDeploymentOutputs = @{
    "staticStorageAccountName" = "azuresdktrainingdatatme"
    "staticResourceGroup" = "static-test-resources"
    "staticWorkspace" = "monitor-query-ws"
}

# Merge with existing deployment outputs if they exist
if ($testSettings.DeploymentOutputs) {
    foreach ($key in $staticDeploymentOutputs.Keys) {
        $testSettings.DeploymentOutputs[$key] = $staticDeploymentOutputs[$key]
    }
} else {
    # If DeploymentOutputs doesn't exist, add it as a property
    $testSettings | Add-Member -MemberType NoteProperty -Name "DeploymentOutputs" -Value $staticDeploymentOutputs
}

$storageAccountName = "$($BaseName)mon"
$containerName = 'foo'
$context = New-AzStorageContext -StorageAccountName $storageAccountName -UseConnectedAccount
Write-Host "Uploading sample files to blob storage: $storageAccountName/$containerName" -ForegroundColor Yellow
$files = Get-ChildItem -Path "$PSScriptRoot/samples" -Filter '*.md'
foreach ($file in $files) {
    Set-AzStorageBlobContent -File $file.FullName -Container $containerName -Blob $file.Name -Context $context -Force -ProgressAction SilentlyContinue | Out-Null
}

# ---------------------------------------------------------------------------
# Seed deterministic CloudHealth health state + transition history for the
# health-model query recorded test
# (Should_Query_HealthModel_PagesHistoryAcrossMarkers_AndFansOutByRealHealthState).
#
# Uses the official Entities_IngestHealthReport ARM action (a manual "push" signal,
# worst-of'd with each entity's Resource Health) so the recording deterministically has:
#   * Model B root entity -> Degraded, then Unhealthy  => >= 2 health-state transitions
#     (so getHistory top=1 returns a real NextMarker + a second page) AND a non-Healthy
#     final state. The 35s gap ensures the two reports land as two distinct transitions.
#   * Model B leaf-degraded entity -> Degraded         => a second, distinct non-Healthy
#     entity so a health-filtered (notHealthy) fan-out resolves to > 1 real entity.
#   * Model B leaf-healthy entity  -> Healthy          => a Healthy control entity the
#     notHealthy fan-out must skip (the leaves are signal-less, so their state comes only
#     from the manual report).
# expires-in-minutes is set high so the reported state persists through recording.
# These writes are test-setup only; the production query tool stays read-only.
# ---------------------------------------------------------------------------
$subscriptionId = $testSettings.SubscriptionId
$healthModelB   = "$($BaseName)-hm-b"
$manualSignal   = 'mcp-fixture-manual'
$apiVersion     = '2026-05-01-preview'
$expiresMinutes = 1440

function Invoke-HealthReport {
    param([string] $EntityName, [string] $HealthState)
    $path = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.CloudHealth/healthmodels/$healthModelB/entities/$EntityName/ingestHealthReport?api-version=$apiVersion"
    $payload = @{ signalName = $manualSignal; healthState = $HealthState; expiresInMinutes = $expiresMinutes } | ConvertTo-Json -Compress
    Write-Host "IngestHealthReport: $EntityName -> $HealthState" -ForegroundColor Yellow
    $response = Invoke-AzRestMethod -Method POST -Path $path -Payload $payload
    if ($response.StatusCode -ge 300) {
        throw "IngestHealthReport failed for $EntityName ($HealthState): HTTP $($response.StatusCode) $($response.Content)"
    }
}

Invoke-HealthReport -EntityName $healthModelB -HealthState 'Degraded'
Start-Sleep -Seconds 35
Invoke-HealthReport -EntityName $healthModelB -HealthState 'Unhealthy'
Invoke-HealthReport -EntityName "$healthModelB-leaf-degraded" -HealthState 'Degraded'
Invoke-HealthReport -EntityName "$healthModelB-leaf-healthy" -HealthState 'Healthy'
# Let the reported states settle into the entity rollup + history before recording.
Start-Sleep -Seconds 45
