# Adds the Sign in with Apple key (used to revoke tokens on account deletion) to the
# apple block of conora/<env>/integrations. The .p8 contents are read from disk and
# only live in a short-lived temp file removed right after the upload.
#
# Usage: .\set-apple-key.ps1 -KeyFile $HOME\Downloads\AuthKey_64CQMUR6HR.p8 [-Environment dev] [-AwsProfile conora-dev]
param(
    [Parameter(Mandatory = $true)][string]$KeyFile,
    [string]$KeyId,
    [string]$TeamId = "N2JF8644LJ",
    [string]$Environment = "dev",
    [string]$AwsProfile = "conora-dev",
    [string]$Region = "sa-east-1"
)

$ErrorActionPreference = "Stop"
$secretId = "conora/$Environment/integrations"

if (-not (Test-Path $KeyFile)) { throw "Key file not found: $KeyFile" }
if ([string]::IsNullOrWhiteSpace($KeyId)) {
    # Apple names the file AuthKey_<KeyId>.p8.
    if ((Split-Path $KeyFile -Leaf) -match '^AuthKey_([A-Z0-9]{10})\.p8$') { $KeyId = $Matches[1] }
    else { throw "Pass -KeyId (could not read it from the file name)." }
}

$privateKey = [IO.File]::ReadAllText((Resolve-Path $KeyFile)).Trim()
if ($privateKey -notmatch 'BEGIN PRIVATE KEY') { throw "$KeyFile does not look like a .p8 private key." }

$raw = aws secretsmanager get-secret-value --secret-id $secretId --query SecretString --output text --profile $AwsProfile --region $Region
if ($LASTEXITCODE -ne 0) { throw "Could not read $secretId." }
$secret = ($raw -join "`n").TrimStart([char]0xFEFF) | ConvertFrom-Json

$apple = if ($secret.PSObject.Properties.Name -contains 'apple' -and $secret.apple) { $secret.apple } else { [pscustomobject]@{} }
$apple | Add-Member -NotePropertyName teamId -NotePropertyValue $TeamId -Force
$apple | Add-Member -NotePropertyName keyId -NotePropertyValue $KeyId -Force
$apple | Add-Member -NotePropertyName privateKey -NotePropertyValue $privateKey -Force
$secret | Add-Member -NotePropertyName apple -NotePropertyValue $apple -Force

# Write without BOM through a temp file so the key never appears on the command line.
$tmp = [IO.Path]::GetTempFileName()
try {
    [IO.File]::WriteAllText($tmp, ($secret | ConvertTo-Json -Depth 10 -Compress), (New-Object Text.UTF8Encoding($false)))
    aws secretsmanager put-secret-value --secret-id $secretId --secret-string "file://$tmp" --profile $AwsProfile --region $Region | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not update $secretId." }
} finally {
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    $privateKey = $null
}

Write-Host "Updated $secretId (apple.keyId $KeyId, teamId $TeamId). Redeploy so generate-env.sh writes Apple__KeyId/Apple__PrivateKey, then restart the API."
