# Brevo domain authentication (transactional e-mail). DMARC is shared with SES (ses.tf).
resource "aws_route53_record" "brevo_dkim" {
  count   = 2
  zone_id = aws_route53_zone.main.zone_id
  name    = "brevo${count.index + 1}._domainkey.${var.domain}"
  type    = "CNAME"
  ttl     = 3600
  records = ["b${count.index + 1}.${replace(var.domain, ".", "-")}.dkim.brevo.com"]
}
