# Amazon SES in sa-east-1 (same provider as the rest of the stack)

resource "aws_ses_domain_identity" "main" {
  domain = local.ses_domain
}

resource "aws_ses_domain_dkim" "main" {
  domain = aws_ses_domain_identity.main.domain
}

resource "aws_ses_configuration_set" "transactional" {
  name = "${local.name_prefix}-transactional"
}

resource "aws_iam_user" "ses_smtp" {
  name = "${local.name_prefix}-ses-smtp"
  path = "/system/"
}

resource "aws_iam_user_policy" "ses_smtp_send" {
  name = "${local.name_prefix}-ses-send"
  user = aws_iam_user.ses_smtp.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["ses:SendRawEmail", "ses:SendEmail"]
        Resource = "*"
        Condition = {
          StringLike = {
            "ses:FromAddress" = "*@${local.ses_domain}"
          }
        }
      }
    ]
  })
}

resource "aws_iam_access_key" "ses_smtp" {
  user = aws_iam_user.ses_smtp.name
}

# DNS records in the Route53 zone (effective once the registrar delegates to Route53 NS)
resource "aws_route53_record" "ses_verification" {
  zone_id = aws_route53_zone.main.zone_id
  name    = "_amazonses.${aws_ses_domain_identity.main.domain}"
  type    = "TXT"
  ttl     = 3600
  records = [aws_ses_domain_identity.main.verification_token]
}

resource "aws_route53_record" "ses_dkim" {
  count   = 3
  zone_id = aws_route53_zone.main.zone_id
  name    = "${aws_ses_domain_dkim.main.dkim_tokens[count.index]}._domainkey.${aws_ses_domain_identity.main.domain}"
  type    = "CNAME"
  ttl     = 3600
  records = ["${aws_ses_domain_dkim.main.dkim_tokens[count.index]}.dkim.amazonses.com"]
}

resource "aws_route53_record" "ses_spf" {
  zone_id = aws_route53_zone.main.zone_id
  name    = aws_ses_domain_identity.main.domain
  type    = "TXT"
  ttl     = 3600
  records = ["v=spf1 include:amazonses.com ~all"]
}

resource "aws_route53_record" "ses_dmarc" {
  zone_id = aws_route53_zone.main.zone_id
  name    = "_dmarc.${aws_ses_domain_identity.main.domain}"
  type    = "TXT"
  ttl     = 3600
  records = ["v=DMARC1; p=none; rua=mailto:dmarc@${aws_ses_domain_identity.main.domain}; pct=100"]
}
