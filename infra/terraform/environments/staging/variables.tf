variable "aws_region" {
  type    = string
  default = "us-east-1"
}

variable "ecr_repo_url" {
  type = string
}

variable "image_tag" {
  type    = string
  default = "latest"
}
