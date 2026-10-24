# Adds the store billing credentials to the storeBilling block of conora/<env>/integrations:
# the App Store In-App Purchase key (.p8), its Issuer ID, an optional Google Play service
# account JSON and the webhook notification key (generated here when absent).
# Key files are read from disk and only live in a short-lived temp file removed right after the upload.
#
# Usage: .\set-store-billing.ps1 -AppleKeyFile $HOME\Downloads\SubscriptionKey_ABC123DEFG.p8 -AppleIssuerId <issuer-uuid>
#        [-GoogleServiceAccountFile play.json] [-Environment dev] [-AwsProfile conora-dev]
param(
    [string]$AppleKeyFile,
    [string]$AppleKeyId,
    [string]$AppleIssuerId,
    [string]$GoogleServiceAccountFile,
    [string]$Environment = "dev",
    [string]$AwsProfile = "conora-dev",
    [string]$Region = "sa-east-1"
)

$ErrorActionPreference = "Stop"
$secretId = "conora/$Environment/integrations"

$raw = aws secretsmanager get-secret-value --secret-id $secretId --query SecretString --output text --profile $AwsProfile --region $Region
if ($LASTEXITCODE -ne 0) { throw "Could not read $secretId." }
$secret = ($raw -join "`n").TrimStart([char]0xFEFF) | ConvertFrom-Json

$store = if ($secret.PSObject.Properties.Name -contains 'storeBilling' -and $secret.storeBilling) { $secret.storeBilling } else { [pscustomobject]@{} }

if ($AppleKeyFile) {
    if (-not (Test-Path $AppleKeyFile)) { throw "Key file not found: $AppleKeyFile" }
    if ([string]::IsNullOrWhiteSpace($AppleIssuerId)) { throw "Pass -AppleIssuerId (Users and Access > Integrations > In-App Purchase)." }
    if ([string]::IsNullOrWhiteSpace($AppleKeyId)) {
        # Apple names the file SubscriptionKey_<KeyId>.p8 (older downloads use AuthKey_<KeyId>.p8).
        if ((Split-Path $AppleKeyFile -Leaf) -match '^(?:SubscriptionKey|AuthKey)_([A-Z0-9]{10})\.p8$') { $AppleKeyId = $Matches[1] }
        else { throw "Pass -AppleKeyId (could not read it from the file name)." }
    }
    $privateKey = [IO.File]::ReadAllText((Resolve-Path $AppleKeyFile)).Trim()
    if ($privateKey -notmatch 'BEGIN PRIVATE KEY') { throw "$AppleKeyFile does not look like a .p8 private key." }
    $store | Add-Member -NotePropertyName appleKeyId -NotePropertyValue $AppleKeyId -Force
    $store | Add-Member -NotePropertyName appleIssuerId -NotePropertyValue $AppleIssuerId -Force
    $store | Add-Member -NotePropertyName applePrivateKey -NotePropertyValue $privateKey -Force
}

if ($GoogleServiceAccountFile) {
    if (-not (Test-Path $GoogleServiceAccountFile)) { throw "Service account file not found: $GoogleServiceAccountFile" }
    $google = [IO.File]::ReadAllText((Resolve-Path $GoogleServiceAccountFile)) | ConvertFrom-Json
    if (-not $google.private_key) { throw "$GoogleServiceAccountFile is not a service account key." }
    $store | Add-Member -NotePropertyName googleServiceAccount -NotePropertyValue $google -Force
}

if (-not ($store.PSObject.Properties.Name -contains 'notificationKey') -or [string]::IsNullOrWhiteSpace($store.notificationKey)) {
    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $store | Add-Member -NotePropertyName notificationKey -NotePropertyValue (($bytes | ForEach-Object { $_.ToString('x2') }) -join '') -Force
}

$secret | Add-Member -NotePropertyName storeBilling -NotePropertyValue $store -Force

# Write without BOM through a temp file so the keys never appear on the command line.
$tmp = [IO.Path]::GetTempFileName()
try {
    [IO.File]::WriteAllText($tmp, ($secret | ConvertTo-Json -Depth 10 -Compress), (New-Object Text.UTF8Encoding($false)))
    aws secretsmanager put-secret-value --secret-id $secretId --secret-string "file://$tmp" --profile $AwsProfile --region $Region | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not update $secretId." }
} finally {
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    $privateKey = $null
    $google = $null
}

Write-Host "Updated $secretId (storeBilling). Google Play push URL: https://api.conora.com.br/webhooks/store/google-play?k=<storeBilling.notificationKey>"
Write-Host "Redeploy so generate-env.sh writes StoreBilling__* and the key files, then restart the API."
