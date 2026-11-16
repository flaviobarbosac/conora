#Requires -Version 5.1
<#
  Shared Segundo Cérebro on the Conora AWS account (371664303999).

  Deploy (build arm64 image -> ECR, upload brain files -> config bucket, run deploy-brain.sh via SSM):
    .\deploy-brain-ssm.ps1 -Profile conora-dev            # image + database + container
    .\deploy-brain-ssm.ps1 -Profile conora-dev -Caddy     # also installs the repo Caddyfile and hot-reloads Caddy
    .\deploy-brain-ssm.ps1 -Profile conora-dev -SkipBuild # reuse the image already in ECR

  Tokens (run inside the brain-mcp container; the token is printed once):
    .\deploy-brain-ssm.ps1 -Profile conora-dev -TokenCommand 'create --name "Leomar" --role suggester'
    .\deploy-brain-ssm.ps1 -Profile conora-dev -TokenCommand 'list'
    .\deploy-brain-ssm.ps1 -Profile conora-dev -TokenCommand 'revoke --id 3'
#>
param(
    [string]$Profile = '',
    [string]$Region = 'sa-east-1',
    [switch]$Caddy,
    [switch]$SkipBuild,
    [string]$TokenCommand = ''
)

$ErrorActionPreference = 'Stop'
$ExpectedAccount = '371664303999'

if ($Profile) { $env:AWS_PROFILE = $Profile }
$env:AWS_REGION = $Region

$Account = aws sts get-caller-identity --query Account --output text
if ($Account -ne $ExpectedAccount) { throw "Wrong AWS account '$Account'. Expected $ExpectedAccount." }

$Bucket = "conora-dev-config-$Account"
$InstanceId = aws ec2 describe-instances `
    --filters "Name=tag:Name,Values=conora-dev-app" "Name=instance-state-name,Values=running" `
    --query 'Reservations[0].Instances[0].InstanceId' --output text
if (-not $InstanceId -or $InstanceId -eq 'None') { throw 'Running instance conora-dev-app not found.' }

function Invoke-Remote([string]$Command, [int]$TimeoutSeconds = 900) {
    $paramsFile = [System.IO.Path]::GetTempFileName()
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($paramsFile, (@{ commands = @($Command) } | ConvertTo-Json -Compress), $utf8NoBom)
    $cid = aws ssm send-command --instance-ids $InstanceId --document-name AWS-RunShellScript `
        --timeout-seconds $TimeoutSeconds --parameters "file://$($paramsFile -replace '\\', '/')" `
        --output text --query Command.CommandId
    Remove-Item $paramsFile -Force

    $status = ''
    for ($i = 0; $i -lt ($TimeoutSeconds / 5); $i++) {
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
}

if ($TokenCommand) {
    if ($TokenCommand -notmatch '^(create|list|revoke|role)\b[\w\s"\-]*$') { throw "Invalid token command: $TokenCommand" }
    Invoke-Remote "docker exec brain-mcp node scripts/tokens.js $TokenCommand" 120
    exit 0
}

$BrainDir = Resolve-Path (Join-Path $PSScriptRoot '..\..\brain')
$Registry = "$Account.dkr.ecr.$Region.amazonaws.com"

if (-not $SkipBuild) {
    Write-Host "Building linux/arm64 image -> $Registry/conora-dev-brain:latest ..." -ForegroundColor Cyan
    # Pipe through cmd: Windows PowerShell 5.1 re-encodes piped text and ECR rejects the password (400).
    cmd /c "aws ecr get-login-password --region $Region | docker login --username AWS --password-stdin $Registry" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'ECR login failed' }
    # buildx writes progress to stderr; judge success by the exit code only.
    $ErrorActionPreference = 'Continue'
    docker buildx build --platform linux/arm64 --progress plain -t "$Registry/conora-dev-brain:latest" --push $BrainDir 2>&1 |
        ForEach-Object { "$_" } | Select-Object -Last 5
    $buildExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($buildExit -ne 0) { throw 'docker buildx failed' }
}

Write-Host "Uploading brain files to s3://$Bucket/brain/ ..." -ForegroundColor Cyan
aws s3 sync (Join-Path $BrainDir 'database') "s3://$Bucket/brain/database/" --delete --exclude '*' --include '*.sql' | Out-Null
aws s3 cp (Join-Path $BrainDir 'deploy\docker-compose.brain.yml') "s3://$Bucket/brain/docker-compose.yml" | Out-Null
aws s3 cp (Join-Path $BrainDir 'deploy\bootstrap-db.sh') "s3://$Bucket/brain/bootstrap-db.sh" | Out-Null
aws s3 cp (Join-Path $BrainDir 'deploy\deploy-brain.sh') "s3://$Bucket/brain/deploy-brain.sh" | Out-Null
if ($Caddy) {
    aws s3 cp (Join-Path $PSScriptRoot '..\docker\Caddyfile') "s3://$Bucket/Caddyfile" | Out-Null
}

$caddyArg = if ($Caddy) { ' --caddy' } else { '' }
Write-Host "Deploying on $InstanceId ..." -ForegroundColor Cyan
Invoke-Remote ("aws s3 cp s3://$Bucket/brain/deploy-brain.sh /tmp/deploy-brain.sh --region $Region && " +
    "sed -i 's/\r$//' /tmp/deploy-brain.sh && AWS_REGION=$Region bash /tmp/deploy-brain.sh $Bucket$caddyArg")

Write-Host ''
Write-Host 'MCP: https://brain.conora.com.br/mcp' -ForegroundColor Cyan
