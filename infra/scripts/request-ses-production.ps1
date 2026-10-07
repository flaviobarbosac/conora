#Requires -Version 5.1
<#
  Requests SES production access (leave the sandbox) for Conora in sa-east-1.

  Usage:
    .\request-ses-production.ps1
    .\request-ses-production.ps1 -ContactEmail "flavio@redfivesistemas.com.br"

  On ConflictException (previous request denied or under review) it prints the text
  to paste in the console: SES > Account dashboard > Request production access.
#>
param(
    [string]$Profile = '',
    [string]$Region = 'sa-east-1',
    [string]$WebsiteUrl = 'https://conora.com.br',
    [string]$ContactEmail = 'flavio@redfivesistemas.com.br'
)

$ErrorActionPreference = 'Stop'
if ($Profile) { $env:AWS_PROFILE = $Profile }
$env:AWS_REGION = $Region

$UseCase = @"
Conora is a personal finance web application for users in Brazil. We send transactional email only:
account and security notifications, and confirmations of data-privacy (LGPD) requests made by the user.
Recipients are users who registered voluntarily in the platform. We do not send marketing or bulk campaigns.
Expected volume: 100-500 emails per day initially.
SPF, DKIM and DMARC are configured for conora.com.br. Bounces and complaints are monitored in the SES console.
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
if ($awsText -notmatch 'ConflictException') {
    Write-Host $awsText -ForegroundColor Red
    exit 1
}

Write-Host 'ConflictException: cannot resubmit through the API.' -ForegroundColor Yellow
Write-Host ''
Write-Host "Console: https://$Region.console.aws.amazon.com/ses/home?region=$Region#/account" -ForegroundColor Cyan
Write-Host "  Mail type     : Transactional"
Write-Host "  Website URL   : $WebsiteUrl"
Write-Host "  Contact email : $ContactEmail"
Write-Host '  Volume        : 100-500 emails/day'
Write-Host ''
Write-Host '--- Use case (copy) ---'
Write-Host $UseCase
Write-Host '--- end ---'
exit 2
