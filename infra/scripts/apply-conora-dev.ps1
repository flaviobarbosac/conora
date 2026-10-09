#Requires -Version 5.1
<#
.SYNOPSIS
  Bootstrap + terraform apply for Conora DEV. Account gate enforced.
#>
param(
  [string]$ProfileName = "conora-dev",
  [switch]$SkipBootstrap,
  [switch]$AutoApprove
)

$ErrorActionPreference = "Stop"

$infraRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$tfRoot = Join-Path $infraRoot "terraform"
$bootstrap = Join-Path $tfRoot "bootstrap"
$backendConfig = Join-Path $tfRoot "backend.dev.hcl"

$env:AWS_PROFILE = $ProfileName
$env:AWS_REGION = "sa-east-1"
$env:AWS_DEFAULT_REGION = "sa-east-1"

& (Join-Path $PSScriptRoot "assert-aws-account.ps1") -ProfileName $ProfileName

function Invoke-Terraform {
  param([Parameter(Mandatory = $true)][string[]]$TfArgs)
  Write-Host ">> terraform $($TfArgs -join ' ')"
  & terraform @TfArgs
  if ($LASTEXITCODE -ne 0) {
    throw "terraform failed (exit $LASTEXITCODE): $($TfArgs -join ' ')"
  }
}

if (-not $SkipBootstrap) {
  Push-Location $bootstrap
  try {
    Invoke-Terraform -TfArgs @("init", "-input=false")
    if ($AutoApprove) {
      Invoke-Terraform -TfArgs @("apply", "-input=false", "-auto-approve")
    } else {
      Invoke-Terraform -TfArgs @("apply", "-input=false")
    }
  } finally {
    Pop-Location
  }
}

Push-Location $tfRoot
try {
  if (-not (Test-Path $backendConfig)) {
    throw "Missing backend config: $backendConfig"
  }

  Invoke-Terraform -TfArgs @(
    "init",
    "-input=false",
    "-reconfigure",
    "-backend-config=$backendConfig"
  )

  if ($AutoApprove) {
    Invoke-Terraform -TfArgs @("apply", "-input=false", "-auto-approve")
  } else {
    Invoke-Terraform -TfArgs @("apply", "-input=false")
  }

  Write-Host ""
  Write-Host "=== Route53 name servers (paste into Registro.br) ==="
  & terraform output -json route53_name_servers
  Write-Host "=== GitHub Actions role ARN ==="
  & terraform output -raw github_actions_role_arn
  Write-Host ""
} finally {
  Pop-Location
}
