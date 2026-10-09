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
Conora (https://conora.com.br) is a personal finance web application for families in Brazil.

Mail type: TRANSACTIONAL only. No marketing, newsletters, or bulk campaigns.

How users opt in:
- A person creates an account voluntarily at https://conora.com.br/app/register (name, e-mail, CPF, password) or signs in with Google/Apple.
- Family-group invites are sent only to an e-mail address typed by an existing member.
- We send: welcome after signup, family invite, invite-accepted notice to the inviter, and LGPD deletion confirmation. Recipients are always addresses the user provided.

Website and compliance pages (public):
- https://conora.com.br/
- https://conora.com.br/privacidade
- https://conora.com.br/termos

Bounce and complaint handling (automatic):
- SES identity notifications and the transactional configuration set publish Bounce and Complaint to SNS topic conora-dev-ses-feedback.
- HTTPS subscription https://api.conora.com.br/webhooks/ses confirms the SNS subscription and writes the recipient to table email_suppressions.
- SmtpEmailSender checks that table before every send and skips suppressed addresses.
- Account-level SES suppression is enabled for BOUNCE and COMPLAINT.
- Ops also receive the SNS notifications by e-mail.

Authentication / reputation:
- SPF, DKIM and DMARC are configured for conora.com.br in Route 53.
- Expected volume: 100-500 e-mails per day initially.
"@.Trim()

$SupportReply = @"
Hello,

This is a follow-up to support case $SupportCaseId regarding Amazon SES production access for account 371664303999 (region sa-east-1). The previous request was denied while we were still in the sandbox.

We have completed the remediation items and kindly ask you to re-review production access.

1) Public website and privacy policy
- Landing: https://conora.com.br/
- Privacy (LGPD): https://conora.com.br/privacidade
- Terms: https://conora.com.br/termos
- The application SPA remains at https://conora.com.br/app/

2) Automatic bounce and complaint handling
- SES Bounce and Complaint notifications for the verified domain conora.com.br go to SNS topic conora-dev-ses-feedback.
- An HTTPS subscriber at https://api.conora.com.br/webhooks/ses processes those events and adds the recipient to our application suppression list (email_suppressions).
- Our SMTP sender refuses to send to suppressed addresses.
- SES account-level suppression is enabled for BOUNCE and COMPLAINT.
- The transactional configuration set also forwards bounce/complaint events to the same SNS topic.

3) Use case / opt-in
- Conora is a personal finance app for families in Brazil.
- Users register voluntarily (form or Google/Apple). We send only transactional mail (welcome, family invites, LGPD confirmation) to addresses they supplied. No marketing.
- Expected volume: 100-500 messages/day. SPF, DKIM and DMARC are configured for conora.com.br.

Please let us know if any additional detail is required.

Thank you,
Flavio Barbosa
flavio@redfivesistemas.com.br / flavio@elroy.com.br
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
