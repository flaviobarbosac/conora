output "account_id" {
  value = data.aws_caller_identity.current.account_id
}

output "route53_name_servers" {
  description = "Set these 4 NS at Registro.br (full delegation)."
  value       = aws_route53_zone.main.name_servers
}

output "route53_zone_id" {
  value = aws_route53_zone.main.zone_id
}

output "ec2_public_ip" {
  value = aws_eip.app.public_ip
}

output "ec2_instance_id" {
  value = aws_instance.app.id
}

output "api_url" {
  value = "https://${local.api_fqdn}"
}

output "front_url" {
  value = local.front_url
}

output "rds_endpoint" {
  value = aws_db_instance.main.address
}

output "rds_port" {
  value = aws_db_instance.main.port
}

output "ecr_api_url" {
  value = aws_ecr_repository.app["api"].repository_url
}

output "ecr_worker_url" {
  value = aws_ecr_repository.app["worker"].repository_url
}

output "config_bucket" {
  value = aws_s3_bucket.config.bucket
}

output "secrets_prefix" {
  value = "${var.project}/${var.environment}/"
}

output "secrets_app_core_arn" {
  value = aws_secretsmanager_secret.app_core.arn
}

output "secrets_integrations_arn" {
  value = aws_secretsmanager_secret.integrations.arn
}

output "github_actions_role_arn" {
  description = "Set as secret AWS_GITHUB_ACTIONS_ROLE_ARN in both GitHub repos."
  value       = aws_iam_role.github_actions.arn
}

# --- SES (Registro.br records, only needed if the zone is NOT delegated to Route53) ---

output "ses_domain" {
  value = aws_ses_domain_identity.main.domain
}

output "ses_verification_txt" {
  value = {
    name  = "_amazonses.${aws_ses_domain_identity.main.domain}"
    type  = "TXT"
    value = aws_ses_domain_identity.main.verification_token
  }
}

output "ses_dkim_cnames" {
  value = [
    for token in aws_ses_domain_dkim.main.dkim_tokens : {
      name  = "${token}._domainkey.${aws_ses_domain_identity.main.domain}"
      type  = "CNAME"
      value = "${token}.dkim.amazonses.com"
    }
  ]
}

output "ses_spf_txt" {
  value = {
    name  = "@"
    type  = "TXT"
    value = "v=spf1 include:amazonses.com ~all"
  }
}

output "ses_dmarc_txt" {
  value = {
    name  = "_dmarc.${aws_ses_domain_identity.main.domain}"
    type  = "TXT"
    value = "v=DMARC1; p=none; rua=mailto:dmarc@${aws_ses_domain_identity.main.domain}; pct=100"
  }
}

output "ses_smtp_endpoint" {
  value = local.ses_smtp_host
}

output "ses_smtp_iam_user" {
  value = aws_iam_user.ses_smtp.name
}
