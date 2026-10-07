# Non-secret runtime settings under /conora/dev/. Secrets live in Secrets Manager.

resource "aws_ssm_parameter" "api_base_url" {
  name  = "${local.ssm_prefix}/api/base_url"
  type  = "String"
  value = "https://${local.api_fqdn}"
}

resource "aws_ssm_parameter" "front_base_url" {
  name  = "${local.ssm_prefix}/front/base_url"
  type  = "String"
  value = local.front_url
}

resource "aws_ssm_parameter" "cors_origin" {
  name  = "${local.ssm_prefix}/cors/origin"
  type  = "String"
  value = "https://${var.domain}"
}

resource "aws_ssm_parameter" "config_bucket" {
  name  = "${local.ssm_prefix}/config/bucket"
  type  = "String"
  value = aws_s3_bucket.config.bucket
}
