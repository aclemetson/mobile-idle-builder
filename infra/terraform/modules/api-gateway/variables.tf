variable "env" {
  type = string
}

variable "aws_region" {
  type = string
}

variable "cognito_user_pool_id" {
  type = string
}

variable "cognito_client_id" {
  type = string
}

variable "lambda_save_read_arn" {
  type = string
}

variable "lambda_save_read_invoke_arn" {
  type = string
}

variable "lambda_save_write_arn" {
  type = string
}

variable "lambda_save_write_invoke_arn" {
  type = string
}

variable "lambda_prestige_validator_arn" {
  type = string
}

variable "lambda_prestige_validator_invoke_arn" {
  type = string
}

variable "lambda_offline_calc_arn" {
  type = string
}

variable "lambda_offline_calc_invoke_arn" {
  type = string
}
