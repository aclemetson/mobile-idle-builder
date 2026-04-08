variable "env" {
  type = string
}

variable "ecr_repo_url" {
  description = "ECR repository base URL for Lambda container images"
  type        = string
}

variable "image_tag" {
  description = "Docker image tag to deploy"
  type        = string
  default     = "latest"
}

variable "dynamodb_table_arns" {
  description = "List of DynamoDB table ARNs the Lambdas may access"
  type        = list(string)
}

variable "player_save_table_name" {
  description = "DynamoDB table name for player save data"
  type        = string
}
