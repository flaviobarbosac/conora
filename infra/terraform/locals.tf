locals {
  name_prefix = "${var.project}-${var.environment}"

  api_fqdn   = "${var.api_subdomain}.${var.domain}"
  front_fqdn = var.domain
  front_url  = "https://${var.domain}/app/"

  ses_domain    = var.domain
  ses_smtp_host = "email-smtp.${var.aws_region}.amazonaws.com"
}
