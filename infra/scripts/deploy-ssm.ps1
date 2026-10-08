#Requires -Version 5.1
<#
  Deploys the Conora stack to the EC2 host through SSM.
  1. Uploads compose/Caddyfile/generate-env.sh (and optionally the front build) to the config bucket.
  2. Runs deploy.sh (rendered by Terraform, stored in the bucket) on the instance.
  Secrets are materialized only on the instance, never uploaded.

  Usage:
    .\deploy-ssm.ps1 -Environment dev
    .\deploy-ssm.ps1 -Environment dev -FrontDist ..\..\..\Conora-frontEnd\dist
#>
param(
    [ValidateSet('dev', 'prod')]
    [string]$Environment = 'dev',
    [string]$Profile = '',
    [string]$Region = 'sa-east-1',
    [string]$InstanceId = '',
    [string]$FrontDist = '',
    [string]$PublicSite = ''
)

$ErrorActionPreference = 'Stop'
$ExpectedAccount = '371664303999'

if ($Environment -ne 'dev') {
    throw "Environment '$Environment' is not provisioned. Only 'dev' exists in account $ExpectedAccount."
}

# Locally a named profile may be used; in GitHub Actions credentials come from OIDC.
if ($Profile) { $env:AWS_PROFILE = $Profile }
$env:AWS_REGION = $Region

$Account = aws sts get-caller-identity --query Account --output text
if ($Account -ne $ExpectedAccount) {
    throw "Wrong AWS account '$Account'. Expected $ExpectedAccount."
}

$Bucket = "conora-$Environment-config-$Account"

if (-not $InstanceId) {
    $InstanceId = aws ec2 describe-instances `
        --filters "Name=tag:Name,Values=conora-$Environment-app" "Name=instance-state-name,Values=running" `
        --query 'Reservations[0].Instances[0].InstanceId' --output text
    if (-not $InstanceId -or $InstanceId -eq 'None') { throw "Running instance conora-$Environment-app not found." }
}

$DockerDir = Join-Path $PSScriptRoot '..\docker'
Write-Host "Uploading config to s3://$Bucket ..." -ForegroundColor Cyan
aws s3 cp (Join-Path $DockerDir 'docker-compose.aws.yml') "s3://$Bucket/docker-compose.yml" | Out-Null
aws s3 cp (Join-Path $DockerDir 'Caddyfile') "s3://$Bucket/Caddyfile" | Out-Null
aws s3 cp (Join-Path $PSScriptRoot 'generate-env.sh') "s3://$Bucket/generate-env.sh" | Out-Null

if ($FrontDist) {
    Write-Host "Syncing front build $FrontDist -> s3://$Bucket/front/ ..." -ForegroundColor Cyan
    aws s3 sync $FrontDist "s3://$Bucket/front/" --delete | Out-Null
}

if (-not $PublicSite) {
    $PublicSite = Join-Path $PSScriptRoot '..\public-site'
}
if (Test-Path $PublicSite) {
    Write-Host "Syncing public site $PublicSite -> s3://$Bucket/public/ ..." -ForegroundColor Cyan
    aws s3 sync $PublicSite "s3://$Bucket/public/" --delete | Out-Null
}

Write-Host "Deploying on $InstanceId ..." -ForegroundColor Cyan
$remote = "aws s3 cp s3://$Bucket/deploy.sh /tmp/conora-deploy.sh --region $Region && bash /tmp/conora-deploy.sh && mkdir -p /opt/conora/public && aws s3 sync s3://$Bucket/public/ /opt/conora/public/ --delete --region $Region && aws s3 cp s3://$Bucket/front/index.html /opt/conora/app/index.html --region $Region 2>/dev/null; cd /opt/conora && docker compose up -d --force-recreate caddy"
$paramsFile = [System.IO.Path]::GetTempFileName()
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($paramsFile, (@{ commands = @($remote) } | ConvertTo-Json -Compress), $utf8NoBom)
$paramsPath = $paramsFile -replace '\\', '/'

$cid = aws ssm send-command --instance-ids $InstanceId --document-name AWS-RunShellScript `
    --timeout-seconds 900 --parameters "file://$paramsPath" --output text --query Command.CommandId
Remove-Item $paramsFile -Force

$status = ''
for ($i = 0; $i -lt 180; $i++) {
    Start-Sleep -Seconds 5
    $status = aws ssm get-command-invocation --command-id $cid --instance-id $InstanceId --output text --query Status 2>$null
    if ($status -in @('Success', 'Failed', 'Cancelled', 'TimedOut')) { break }
}

$out = aws ssm get-command-invocation --command-id $cid --instance-id $InstanceId --output text --query StandardOutputContent
$err = aws ssm get-command-invocation --command-id $cid --instance-id $InstanceId --output text --query StandardErrorContent
Write-Host "Status: $status" -ForegroundColor $(if ($status -eq 'Success') { 'Green' } else { 'Red' })
if ($out) { Write-Host $out }
if ($err) { Write-Host $err -ForegroundColor Yellow }
if ($status -ne 'Success') { exit 1 }

Write-Host ''
Write-Host 'API:      https://api.conora.com.br/health/live' -ForegroundColor Cyan
Write-Host 'Site:     https://conora.com.br/' -ForegroundColor Cyan
Write-Host 'Privacy:  https://conora.com.br/privacidade' -ForegroundColor Cyan
Write-Host 'Terms:    https://conora.com.br/termos' -ForegroundColor Cyan
Write-Host 'Front:    https://conora.com.br/app/' -ForegroundColor Cyan
