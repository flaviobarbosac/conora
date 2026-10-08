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

# Bounce / complaint feedback → SNS → API webhook (auto-suppression) + ops e-mail.
resource "aws_sns_topic" "ses_feedback" {
  name = "${local.name_prefix}-ses-feedback"
}

resource "aws_sns_topic_policy" "ses_feedback" {
  arn = aws_sns_topic.ses_feedback.arn
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid    = "AllowSesPublish"
        Effect = "Allow"
        Principal = {
          Service = "ses.amazonaws.com"
        }
        Action   = "SNS:Publish"
        Resource = aws_sns_topic.ses_feedback.arn
        Condition = {
          StringEquals = {
            "AWS:SourceAccount" = data.aws_caller_identity.current.account_id
          }
        }
      }
    ]
  })
}

resource "aws_ses_identity_notification_topic" "bounce" {
  topic_arn                = aws_sns_topic.ses_feedback.arn
  notification_type        = "Bounce"
  identity                 = aws_ses_domain_identity.main.domain
  include_original_headers = false
}

resource "aws_ses_identity_notification_topic" "complaint" {
  topic_arn                = aws_sns_topic.ses_feedback.arn
  notification_type        = "Complaint"
  identity                 = aws_ses_domain_identity.main.domain
  include_original_headers = false
}

resource "aws_ses_event_destination" "transactional_feedback" {
  name                   = "sns-feedback"
  configuration_set_name = aws_ses_configuration_set.transactional.name
  enabled                = true
  matching_types         = ["bounce", "complaint"]

  sns_destination {
    topic_arn = aws_sns_topic.ses_feedback.arn
  }

  depends_on = [aws_sns_topic_policy.ses_feedback]
}

resource "aws_sns_topic_subscription" "ses_https" {
  topic_arn = aws_sns_topic.ses_feedback.arn
  protocol  = "https"
  endpoint  = "https://${local.api_fqdn}/webhooks/ses"
}

resource "aws_sns_topic_subscription" "ses_email" {
  topic_arn = aws_sns_topic.ses_feedback.arn
  protocol  = "email"
  endpoint  = var.alert_email
}

resource "aws_sesv2_account_suppression_attributes" "main" {
  suppressed_reasons = ["BOUNCE", "COMPLAINT"]
}

# Reputation alarms → ops e-mail. Separate topic so alarm payloads never hit the SES webhook.
# Thresholds stay well below the SES review limits (5% bounce, 0.1% complaint).
resource "aws_sns_topic" "ses_alarms" {
  name = "${local.name_prefix}-ses-alarms"
}

resource "aws_sns_topic_subscription" "ses_alarms_email" {
  topic_arn = aws_sns_topic.ses_alarms.arn
  protocol  = "email"
  endpoint  = var.alert_email
}

resource "aws_cloudwatch_metric_alarm" "ses_bounce_rate" {
  alarm_name          = "${local.name_prefix}-ses-bounce-rate"
  alarm_description   = "SES bounce rate at or above 3%."
  namespace           = "AWS/SES"
  metric_name         = "Reputation.BounceRate"
  statistic           = "Maximum"
  period              = 3600
  evaluation_periods  = 1
  threshold           = 0.03
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.ses_alarms.arn]
  ok_actions          = [aws_sns_topic.ses_alarms.arn]
}

resource "aws_cloudwatch_metric_alarm" "ses_complaint_rate" {
  alarm_name          = "${local.name_prefix}-ses-complaint-rate"
  alarm_description   = "SES complaint rate at or above 0.05%."
  namespace           = "AWS/SES"
  metric_name         = "Reputation.ComplaintRate"
  statistic           = "Maximum"
  period              = 3600
  evaluation_periods  = 1
  threshold           = 0.0005
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.ses_alarms.arn]
  ok_actions          = [aws_sns_topic.ses_alarms.arn]
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
