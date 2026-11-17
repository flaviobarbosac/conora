# Switches the e-mail block of conora/<env>/integrations from Brevo back to SES SMTP.
# The SMTP password is read from the Terraform state, never echoed, and only lives
# in a short-lived temp file removed right after the upload.
#
# Usage: .\set-email-ses.ps1 [-Environment dev] [-AwsProfile conora-dev]
param(
    [string]$Environment = "dev",
    [string]$AwsProfile = "conora-dev",
    [string]$Region = "sa-east-1",
    [string]$From = "noreply@conora.com.br",
    [string]$ExpectedAccount = "371664303999"
)

$ErrorActionPreference = "Stop"
$secretId = "conora/$Environment/integrations"
$stateBucket = "conora-terraform-state-$ExpectedAccount"
$stateKey = "$Environment/terraform.tfstate"
$smtpHost = "email-smtp.$Region.amazonaws.com"
$configurationSet = "conora-$Environment-transactional"

$account = aws sts get-caller-identity --query Account --output text --profile $AwsProfile --region $Region
if ($account -ne $ExpectedAccount) { throw "Wrong AWS account '$account'. Expected $ExpectedAccount." }

$production = aws sesv2 get-account --profile $AwsProfile --region $Region --query ProductionAccessEnabled --output text
if ($production -ne "True") { throw "SES production access is still off in $Region. The secret was not changed." }

$work = Join-Path ([IO.Path]::GetTempPath()) ("conora-ses-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work | Out-Null
$stateFile = Join-Path $work "state.json"
$currentFile = Join-Path $work "current.json"
$nextFile = Join-Path $work "next.json"

try {
    aws s3 cp "s3://$stateBucket/$stateKey" $stateFile --profile $AwsProfile --region $Region | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not read the Terraform state." }

    $raw = aws secretsmanager get-secret-value --secret-id $secretId --query SecretString --output text --profile $AwsProfile --region $Region
    if ($LASTEXITCODE -ne 0) { throw "Could not read $secretId." }
    [IO.File]::WriteAllText($currentFile, (($raw -join "`n").TrimStart([char]0xFEFF)), (New-Object Text.UTF8Encoding($false)))

    $py = @'
import json, pathlib, sys
state_path, current_path, next_path, host, sender, config_set = sys.argv[1:]
state = json.loads(pathlib.Path(state_path).read_text(encoding="utf-8-sig"))
user = password = None
for resource in state.get("resources", []):
    if resource.get("type") == "aws_iam_access_key" and resource.get("name") == "ses_smtp":
        attrs = resource["instances"][0]["attributes"]
        user = attrs.get("id")
        password = attrs.get("ses_smtp_password_v4")
        break
if not user or not password:
    raise SystemExit("SES SMTP credentials are not in the Terraform state.")
raw = pathlib.Path(current_path).read_text(encoding="utf-8-sig").lstrip("\ufeff")
secret = json.loads(raw)
secret["email"] = {
    "from": sender,
    "smtpHost": host,
    "smtpPort": 587,
    "user": user,
    "password": password,
    "useSsl": True,
    "configurationSet": config_set,
}
pathlib.Path(next_path).write_text(json.dumps(secret, separators=(",", ":")), encoding="utf-8")
print("ready")
'@
    $py | python - $stateFile $currentFile $nextFile $smtpHost $From $configurationSet
    if ($LASTEXITCODE -ne 0) { throw "Could not build the SES e-mail block." }

    aws secretsmanager put-secret-value --secret-id $secretId --secret-string "file://$($nextFile -replace '\\','/')" --profile $AwsProfile --region $Region | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not update $secretId." }
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Updated $secretId (email -> SES, host $smtpHost, set $configurationSet). Regenerate .env.app on the host and restart the API."
