#Requires -Version 5.1
<#
.SYNOPSIS
  Sets AWS_GITHUB_ACTIONS_ROLE_ARN on conora and conora-front from terraform output.
#>
param(
  [string]$ProfileName = "conora-dev"
)

$ErrorActionPreference = "Stop"
$env:AWS_PROFILE = $ProfileName
& (Join-Path $PSScriptRoot "assert-aws-account.ps1") -ProfileName $ProfileName

$tfRoot = Resolve-Path (Join-Path $PSScriptRoot "..\terraform")
Push-Location $tfRoot
try {
  $arn = terraform output -raw github_actions_role_arn
} finally {
  Pop-Location
}

if ([string]::IsNullOrWhiteSpace($arn)) {
  throw "Empty github_actions_role_arn — run terraform apply first."
}

Write-Host "Role ARN: $arn"
gh secret set AWS_GITHUB_ACTIONS_ROLE_ARN --repo flaviobarbosac/conora --body $arn
gh secret set AWS_GITHUB_ACTIONS_ROLE_ARN --repo flaviobarbosac/conora-front --body $arn
Write-Host "Secrets set on both repos."
