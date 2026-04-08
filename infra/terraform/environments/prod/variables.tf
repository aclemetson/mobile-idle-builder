variable "aws_region" {
  type    = string
  default = "us-east-1"
}

variable "ecr_repo_url" {
  type = string
}

variable "image_tag" {
  description = "Image tag to deploy — must be a pinned SHA for prod, not 'latest'"
  type        = string
}
