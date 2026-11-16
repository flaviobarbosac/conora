# Shared Segundo Cérebro: isolated "brain" database on the Conora RDS instance,
# remote MCP server (container brain-mcp) behind Caddy at brain.<domain>.
# Database and roles are created by brain/deploy/bootstrap-db.sh (not by Terraform),
# because the RDS instance is private and only the EC2 host reaches it.
#
# The host IP and the RDS endpoint are read through data sources on purpose:
# referencing aws_eip.app / aws_db_instance.main would pull the EC2 instance
# (AMI drift => replacement) into any targeted plan of these resources.

data "aws_eip" "brain_host" {
  tags = {
    Name = "${local.name_prefix}-eip"
  }
}

data "aws_db_instance" "brain" {
  db_instance_identifier = "${local.name_prefix}-postgres"
}

resource "random_password" "brain_agent" {
  length  = 32
  special = false
}

resource "random_password" "brain_writer" {
  length  = 32
  special = false
}

resource "aws_secretsmanager_secret" "brain" {
  name                    = "${var.project}/${var.environment}/brain"
  description             = "Shared Segundo Cerebro - database roles for the brain database"
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "brain" {
  secret_id = aws_secretsmanager_secret.brain.id
  secret_string = jsonencode({
    host           = data.aws_db_instance.brain.address
    port           = data.aws_db_instance.brain.port
    database       = "brain"
    agentUser      = "brain_agent"
    agentPassword  = random_password.brain_agent.result
    writerUser     = "brain_writer"
    writerPassword = random_password.brain_writer.result
  })
}

# Separate policy so changes here never touch the Conora secrets policy.
resource "aws_iam_role_policy" "ec2_brain_secret" {
  name = "${local.name_prefix}-brain-secret-read"
  role = aws_iam_role.ec2.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["secretsmanager:GetSecretValue", "secretsmanager:DescribeSecret"]
      Resource = [aws_secretsmanager_secret.brain.arn]
    }]
  })
}

resource "aws_route53_record" "brain" {
  zone_id = aws_route53_zone.main.zone_id
  name    = "brain.${var.domain}"
  type    = "A"
  ttl     = 300
  records = [data.aws_eip.brain_host.public_ip]
}

output "brain_url" {
  value = "https://brain.${var.domain}/mcp"
}

output "ecr_brain_url" {
  value = aws_ecr_repository.app["brain"].repository_url
}
