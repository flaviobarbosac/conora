#Requires -Version 5.1
<#
.SYNOPSIS
  Gate: Conora AWS work only on account 371664303999.
#>
param(
  [string]$ProfileName = "conora-dev",
  [string]$ExpectedAccountId = "371664303999"
)

$ErrorActionPreference = "Stop"

if ($env:AWS_PROFILE) {
  $ProfileName = $env:AWS_PROFILE
}

$env:AWS_PROFILE = $ProfileName
$env:AWS_REGION = if ($env:AWS_REGION) { $env:AWS_REGION } else { "sa-east-1" }

Write-Host "Checking AWS identity (profile=$ProfileName)..."
$identityJson = aws sts get-caller-identity --output json 2>&1
if ($LASTEXITCODE -ne 0) {
  throw "AWS credentials missing or invalid for profile '$ProfileName'. Conta esperada: $ExpectedAccountId. Se aws login der TOKEN_EXPIRED: apague %USERPROFILE%\.aws\cli\cache e tente de novo; ou configure Access Key no profile conora-dev."
}

$identity = $identityJson | ConvertFrom-Json
$account = [string]$identity.Account
Write-Host "Account=$account Arn=$($identity.Arn)"

if ($account -ne $ExpectedAccountId) {
  throw "WRONG AWS ACCOUNT: got $account, expected $ExpectedAccountId. Aborting. Do not run Conora Terraform against this account."
}

Write-Host "OK: Conora account gate passed ($ExpectedAccountId)."
