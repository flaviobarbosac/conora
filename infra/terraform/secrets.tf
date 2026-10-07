resource "random_password" "jwt" {
  length  = 48
  special = false
}

resource "random_password" "rabbitmq" {
  length  = 24
  special = false
}

resource "random_password" "redis" {
  length  = 24
  special = false
}

locals {
  # Npgsql connection string (ConnectionStrings:Postgres)
  db_connection_string = join("", [
    "Host=", aws_db_instance.main.address,
    ";Port=", aws_db_instance.main.port,
    ";Database=", var.db_name,
    ";Username=", var.db_username,
    ";Password=", random_password.db.result,
    ";SSL Mode=Require;Trust Server Certificate=true"
  ])

  app_core_secret_json = jsonencode({
    connectionString = local.db_connection_string
    jwtKey           = var.jwt_key != "" ? var.jwt_key : random_password.jwt.result
  })

  integrations_secret_json = jsonencode({
    google = {
      clientId = var.google_client_id
    }
    gemini = {
      apiKey = var.gemini_api_key
      model  = var.gemini_model
    }
    email = {
      from     = var.ses_from
      smtpHost = local.ses_smtp_host
      smtpPort = 587
      user     = aws_iam_access_key.ses_smtp.id
      password = aws_iam_access_key.ses_smtp.ses_smtp_password_v4
      useSsl   = true
    }
    whatsapp = {
      verifyToken   = var.whatsapp_verify_token
      appSecret     = var.whatsapp_app_secret
      accessToken   = var.whatsapp_access_token
      phoneNumberId = var.whatsapp_phone_number_id
    }
    rabbitmq = {
      user     = "conora"
      password = random_password.rabbitmq.result
    }
    redis = {
      password = random_password.redis.result
    }
  })
}

resource "aws_secretsmanager_secret" "app_core" {
  name                    = "${var.project}/${var.environment}/app-core"
  description             = "Conora ${var.environment} - connection string and JWT key"
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "app_core" {
  secret_id     = aws_secretsmanager_secret.app_core.id
  secret_string = local.app_core_secret_json

  lifecycle {
    ignore_changes = [secret_string]
  }
}

resource "aws_secretsmanager_secret" "integrations" {
  name                    = "${var.project}/${var.environment}/integrations"
  description             = "Conora ${var.environment} - Google, Gemini, e-mail, WhatsApp, RabbitMQ, Redis"
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "integrations" {
  secret_id     = aws_secretsmanager_secret.integrations.id
  secret_string = local.integrations_secret_json

  lifecycle {
    ignore_changes = [secret_string]
  }
}
