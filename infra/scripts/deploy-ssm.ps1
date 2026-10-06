param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dev', 'prod')]
    [string]$Environment
)

Write-Host "Conora deploy-ssm placeholder for environment '$Environment'."
Write-Host "Implement SSM deploy when AWS account/ECR/EC2 are provisioned (same pattern as ClampFY)."
exit 1
