terraform {
  required_version = ">= 1.5"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.0"
    }
  }

  # Partial backend: values come from backend.dev.hcl after the bootstrap is applied.
  #   terraform init -backend-config=backend.dev.hcl
  # For local validation without state: terraform init -backend=false
  backend "s3" {}
}
