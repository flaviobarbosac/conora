#Requires -Version 5.1
<#
  Builds linux/arm64 images (EC2 is t4g/Graviton) and pushes API + Worker to ECR.
  Requires Docker with buildx.
#>
param(
    [ValidateSet('dev')]
    [string]$Environment = 'dev',
    [string]$Profile = '',
    [string]$Region = 'sa-east-1'
)

$ErrorActionPreference = 'Stop'
$ExpectedAccount = '371664303999'

if ($Profile) { $env:AWS_PROFILE = $Profile }
$env:AWS_REGION = $Region

$Account = aws sts get-caller-identity --query Account --output text
if ($Account -ne $ExpectedAccount) {
    throw "Wrong AWS account '$Account'. Expected $ExpectedAccount."
}

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$EcrHost = "${Account}.dkr.ecr.${Region}.amazonaws.com"
$EcrApi = "${EcrHost}/conora-${Environment}-api"
$EcrWorker = "${EcrHost}/conora-${Environment}-worker"

Write-Host "Login ECR ($Environment)..." -ForegroundColor Cyan
aws ecr get-login-password --region $Region | docker login --username AWS --password-stdin $EcrHost
if ($LASTEXITCODE -ne 0) { throw 'ECR login failed' }

Push-Location $RepoRoot
try {
    Write-Host 'Build + push API (arm64)...' -ForegroundColor Cyan
    docker buildx build --platform linux/arm64 -f Dockerfile -t "${EcrApi}:latest" --push .
    if ($LASTEXITCODE -ne 0) { throw 'API build/push failed' }

    Write-Host 'Build + push Worker (arm64)...' -ForegroundColor Cyan
    docker buildx build --platform linux/arm64 -f Dockerfile.worker -t "${EcrWorker}:latest" --push .
    if ($LASTEXITCODE -ne 0) { throw 'Worker build/push failed' }
}
finally {
    Pop-Location
}

Write-Host "OK: ${EcrApi}:latest" -ForegroundColor Green
Write-Host "OK: ${EcrWorker}:latest" -ForegroundColor Green
