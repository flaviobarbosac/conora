# Switches the e-mail block of conora/<env>/integrations from SES to Brevo SMTP.
# The SMTP key is read from a hidden prompt, never echoed, and only lives in a
# short-lived temp file removed right after the upload.
#
# Usage: .\set-email-brevo.ps1 -Login <id>@smtp-brevo.com [-Environment dev] [-AwsProfile conora-dev]
param(
    [Parameter(Mandatory = $true)][string]$Login,
    [string]$Environment = "dev",
    [string]$AwsProfile = "conora-dev",
    [string]$Region = "sa-east-1",
    [string]$From = "noreply@conora.com.br"
)

$ErrorActionPreference = "Stop"
$secretId = "conora/$Environment/integrations"

$secure = Read-Host -AsSecureString "Brevo SMTP key"
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}
if ([string]::IsNullOrWhiteSpace($key)) { throw "Empty SMTP key." }

$raw = aws secretsmanager get-secret-value --secret-id $secretId --query SecretString --output text --profile $AwsProfile --region $Region
if ($LASTEXITCODE -ne 0) { throw "Could not read $secretId." }
$secret = ($raw -join "`n").TrimStart([char]0xFEFF) | ConvertFrom-Json

$email = [ordered]@{
    from             = $From
    smtpHost         = "smtp-relay.brevo.com"
    smtpPort         = 587
    user             = $Login
    password         = $key
    useSsl           = $true
    configurationSet = ""
}
$secret | Add-Member -NotePropertyName email -NotePropertyValue ([pscustomobject]$email) -Force

# Write without BOM through a temp file so the key never appears on the command line.
$tmp = [IO.Path]::GetTempFileName()
try {
    [IO.File]::WriteAllText($tmp, ($secret | ConvertTo-Json -Depth 10 -Compress), (New-Object Text.UTF8Encoding($false)))
    aws secretsmanager put-secret-value --secret-id $secretId --secret-string "file://$tmp" --profile $AwsProfile --region $Region | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not update $secretId." }
} finally {
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    $key = $null
}

Write-Host "Updated $secretId (email -> Brevo, user $Login). Regenerate .env.app on the host and restart the API."
