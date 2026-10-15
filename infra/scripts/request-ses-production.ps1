#Requires -Version 5.1
<#
  SES production access helper for Conora (sa-east-1).

  After a denial (case 179137300900718), do NOT resubmit via API — reply in the
  Support Center with the draft printed below. This script only attempts
  put-account-details when there is no denied/pending conflict.

  Usage:
    .\request-ses-production.ps1
    .\request-ses-production.ps1 -Profile conora-dev
#>
param(
    [string]$Profile = '',
    [string]$Region = 'sa-east-1',
    [string]$WebsiteUrl = 'https://conora.com.br',
    [string]$ContactEmail = 'flavio@redfivesistemas.com.br',
    [string]$SupportCaseId = '179137300900718'
)

$ErrorActionPreference = 'Stop'
if ($Profile) { $env:AWS_PROFILE = $Profile }
$env:AWS_REGION = $Region

$UseCase = @"
Conora (https://conora.com.br) is a personal-finance web app for families in Brazil. Mail is TRANSACTIONAL only. We do not send newsletters, promotions, or purchased lists.

Concrete messages, each triggered by a user action:
- Welcome after the person creates an account (form or Google) at https://conora.com.br/app/register.
- Family-group invite, sent only to an address an existing member typed.
- Notice to the inviter when that invite is accepted.
- Notice when the family-budget owner asks another member to take over the budget.
- Confirmation when the account holder requests LGPD data deletion.

Recipients are the account e-mail or an address the member entered. There is no bulk send. In Settings the user can turn system e-mail off; that address is then skipped. Expected volume: 2,000 to 5,000 messages per month.

Authentication on conora.com.br (Route 53, sa-east-1): DKIM (SUCCESS), SPF (include:amazonses.com), DMARC (p=none), and custom MAIL FROM mail.conora.com.br.

Bounce and complaint: configuration set conora-dev-transactional publishes Bounce and Complaint to SNS topic conora-dev-ses-feedback. HTTPS subscriber https://api.conora.com.br/webhooks/ses writes the address to email_suppressions, and the SMTP sender skips that address. Account-level SES suppression is on for BOUNCE and COMPLAINT.

Public pages: https://conora.com.br/ https://conora.com.br/privacidade https://conora.com.br/termos
"@.Trim()

$SupportReply = @"
Hello,

Please re-open the review on case $SupportCaseId (account 371664303999, sa-east-1). This is not a new request. Production access is still disabled and the last review status is DENIED. Since the earlier reply we finished domain authentication and confirmed the bounce path in the account.

What we send
Transactional mail only, for the personal-finance app at https://conora.com.br. No marketing and no purchased lists. Each message is caused by something the user did:
- welcome after signup (https://conora.com.br/app/register, form or Google);
- family-group invite, only to an address an existing member typed;
- notice to the inviter when the invite is accepted;
- notice when the budget owner asks another member to take over the family budget;
- confirmation of an LGPD deletion request made by the account holder.
Volume we expect: 2,000 to 5,000 messages per month. The user can turn system e-mail off in Settings; that address is then not mailed.

Domain authentication (verified in this account today)
- Identity conora.com.br: VerificationStatus SUCCESS, DKIM SUCCESS (Easy DKIM, RSA 2048).
- SPF on conora.com.br: v=spf1 include:amazonses.com ~all.
- DMARC: v=DMARC1; p=none; rua=mailto:dmarc@conora.com.br.
- Custom MAIL FROM: mail.conora.com.br (MX to feedback-smtp.sa-east-1.amazonses.com, SPF on that host).

Bounce and complaint
- Configuration set conora-dev-transactional sends Bounce and Complaint to SNS topic conora-dev-ses-feedback. Reputation metrics on that set are enabled.
- The HTTPS subscription https://api.conora.com.br/webhooks/ses is confirmed. It stores the address in email_suppressions. The sender checks that table before every message.
- Account suppression attributes: BOUNCE and COMPLAINT.
- A separate topic, conora-dev-ses-alarms, alerts us before SES review thresholds (bounce 3%, complaint 0.05%).

Public pages
- https://conora.com.br/
- https://conora.com.br/privacidade
- https://conora.com.br/termos
- The app itself is at https://conora.com.br/app/

Please tell us if anything else is missing for production access.

Thank you,
Flavio Barbosa
flavio@elroy.com.br
flavio@redfivesistemas.com.br
"@.Trim()

Write-Host ''
Write-Host '=== SES production access - Conora ===' -ForegroundColor Cyan

$account = aws sesv2 get-account --region $Region | ConvertFrom-Json
$review = $account.Details.ReviewDetails

if ($account.ProductionAccessEnabled) {
    Write-Host 'Production access: YES. Nothing to do.' -ForegroundColor Green
    exit 0
}

Write-Host 'Production access: NO (sandbox)'
if ($review) { Write-Host ("Last review: {0} | case {1}" -f $review.Status, $review.CaseId) }

$deniedOrConflict = $review -and ($review.Status -eq 'DENIED' -or $review.CaseId -eq $SupportCaseId)

if ($deniedOrConflict) {
    Write-Host ''
    Write-Host 'Do NOT resubmit via API after a denial.' -ForegroundColor Yellow
    Write-Host ("Reply in Support Center to case {0}:" -f $SupportCaseId) -ForegroundColor Cyan
    Write-Host ("https://{0}.console.aws.amazon.com/support/home?region={0}#/case/?displayId={1}" -f $Region, $SupportCaseId) -ForegroundColor Cyan
    Write-Host ''
    Write-Host '--- Support reply (copy) ---'
    Write-Host $SupportReply
    Write-Host '--- end ---'
    Write-Host ''
    Write-Host '--- Use case (reference) ---'
    Write-Host $UseCase
    Write-Host '--- end ---'
    exit 2
}

Write-Host 'Sending put-account-details...' -ForegroundColor Cyan
$prevEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$awsOut = aws sesv2 put-account-details `
    --region $Region `
    --mail-type TRANSACTIONAL `
    --website-url $WebsiteUrl `
    --contact-language EN `
    --use-case-description $UseCase `
    --additional-contact-email-addresses $ContactEmail `
    --production-access-enabled 2>&1
$awsExit = $LASTEXITCODE
$ErrorActionPreference = $prevEap

if ($awsExit -eq 0) {
    Write-Host 'Request sent. AWS reviews within ~24-72 hours.' -ForegroundColor Green
    exit 0
}

$awsText = ($awsOut | Out-String)
Write-Host $awsText -ForegroundColor Yellow
Write-Host ''
Write-Host 'Conflict or error: paste the Support reply below in the console case.' -ForegroundColor Yellow
Write-Host ("Console case: https://{0}.console.aws.amazon.com/support/home?region={0}#/case/?displayId={1}" -f $Region, $SupportCaseId) -ForegroundColor Cyan
Write-Host ''
Write-Host '--- Support reply (copy) ---'
Write-Host $SupportReply
Write-Host '--- end ---'
exit 2
