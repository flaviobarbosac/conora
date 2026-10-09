resource "aws_s3_bucket" "config" {
  bucket = "${local.name_prefix}-config-${data.aws_caller_identity.current.account_id}"
}

resource "aws_s3_bucket_public_access_block" "config" {
  bucket                  = aws_s3_bucket.config.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "config" {
  bucket = aws_s3_bucket.config.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_object" "docker_compose" {
  bucket  = aws_s3_bucket.config.bucket
  key     = "docker-compose.yml"
  content = file("${path.module}/../docker/docker-compose.aws.yml")
  etag    = filemd5("${path.module}/../docker/docker-compose.aws.yml")
}

resource "aws_s3_object" "caddyfile" {
  bucket  = aws_s3_bucket.config.bucket
  key     = "Caddyfile"
  content = file("${path.module}/../docker/Caddyfile")
  etag    = filemd5("${path.module}/../docker/Caddyfile")
}

resource "aws_s3_object" "generate_env" {
  bucket  = aws_s3_bucket.config.bucket
  key     = "generate-env.sh"
  content = file("${path.module}/../scripts/generate-env.sh")
  etag    = filemd5("${path.module}/../scripts/generate-env.sh")
}

resource "aws_s3_object" "deploy_script" {
  bucket = aws_s3_bucket.config.bucket
  key    = "deploy.sh"
  content = templatefile("${path.module}/templates/deploy.sh", {
    aws_region    = var.aws_region
    project       = var.project
    environment   = var.environment
    account_id    = data.aws_caller_identity.current.account_id
    config_bucket = aws_s3_bucket.config.bucket
    api_fqdn      = local.api_fqdn
    domain        = var.domain
  })

  depends_on = [
    aws_s3_object.docker_compose,
    aws_s3_object.caddyfile,
    aws_s3_object.generate_env,
  ]
}

resource "aws_iam_role_policy" "ec2_s3_config" {
  name = "${local.name_prefix}-s3-config"
  role = aws_iam_role.ec2.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["s3:GetObject"]
        Resource = "${aws_s3_bucket.config.arn}/*"
      },
      {
        Effect   = "Allow"
        Action   = ["s3:ListBucket"]
        Resource = aws_s3_bucket.config.arn
      }
    ]
  })
}
