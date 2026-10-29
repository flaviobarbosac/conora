<#
.SYNOPSIS
  Prints recent conora-api container logs from the app instance (via SSM), optionally filtered.

.EXAMPLE
  .\infra\scripts\api-logs.ps1 -Since 2h -Filter "Store|plan/store"
#>
param(
    [string]$Environment = 'dev',
    [string]$Profile = 'conora-dev',
    [string]$Region = 'sa-east-1',
    [string]$Since = '2h',
    [string]$Filter = '',
    [int]$Tail = 200
)

$ErrorActionPreference = 'Stop'
if ($Profile) { $env:AWS_PROFILE = $Profile }
$env:AWS_REGION = $Region

$instanceId = aws ec2 describe-instances `
    --filters "Name=tag:Name,Values=conora-$Environment-app" "Name=instance-state-name,Values=running" `
    --query 'Reservations[0].Instances[0].InstanceId' --output text
if (-not $instanceId -or $instanceId -eq 'None') { throw "Running instance conora-$Environment-app not found." }

# Serilog lines start with "[HH:mm:ss LVL]"; this drops the OpenTelemetry console metrics.
$cmd = "docker logs conora-api --since $Since 2>&1 | grep -E '^\[[0-9]{2}:[0-9]{2}:[0-9]{2} [A-Z]{3}\]'"
if ($Filter) { $cmd += " | grep -iE '$Filter'" }
$cmd += " | tail -n $Tail"

$params = @{ commands = @($cmd) } | ConvertTo-Json -Compress
$paramsFile = Join-Path $env:TEMP 'conora-api-logs.json'
Set-Content -Path $paramsFile -Value $params -Encoding ascii

$cid = aws ssm send-command --instance-ids $instanceId --document-name AWS-RunShellScript `
    --parameters "file://$paramsFile" --query 'Command.CommandId' --output text
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    $status = aws ssm get-command-invocation --command-id $cid --instance-id $instanceId --query Status --output text 2>$null
    if ($status -in @('Success', 'Failed', 'Cancelled', 'TimedOut')) { break }
}
aws ssm get-command-invocation --command-id $cid --instance-id $instanceId --query StandardOutputContent --output text
