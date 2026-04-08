terraform {
  required_version = ">= 1.7"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  backend "s3" {
    bucket = "mobile-idle-tfstate-prod"
    key    = "prod/terraform.tfstate"
    region = "us-east-1"
  }
}

provider "aws" {
  region = var.aws_region
}

module "dynamodb" {
  source      = "../../modules/dynamodb"
  env         = "prod"
  enable_pitr = true
}

module "cognito" {
  source             = "../../modules/cognito"
  env                = "prod"
  identity_providers = ["COGNITO", "SignInWithApple", "Google"]
}

module "lambda" {
  source                 = "../../modules/lambda"
  env                    = "prod"
  ecr_repo_url           = var.ecr_repo_url
  image_tag              = var.image_tag
  dynamodb_table_arns    = [module.dynamodb.player_save_table_arn, module.dynamodb.leaderboard_table_arn]
  player_save_table_name = module.dynamodb.player_save_table_name
}

module "api_gateway" {
  source                               = "../../modules/api-gateway"
  env                                  = "prod"
  aws_region                           = var.aws_region
  cognito_user_pool_id                 = module.cognito.user_pool_id
  cognito_client_id                    = module.cognito.client_id
  lambda_save_read_arn                 = module.lambda.save_read_arn
  lambda_save_read_invoke_arn          = module.lambda.save_read_invoke_arn
  lambda_save_write_arn                = module.lambda.save_write_arn
  lambda_save_write_invoke_arn         = module.lambda.save_write_invoke_arn
  lambda_prestige_validator_arn        = module.lambda.prestige_validator_arn
  lambda_prestige_validator_invoke_arn = module.lambda.prestige_validator_invoke_arn
  lambda_offline_calc_arn              = module.lambda.offline_calc_arn
  lambda_offline_calc_invoke_arn       = module.lambda.offline_calc_invoke_arn
}

module "appsync" {
  source                 = "../../modules/appsync"
  env                    = "prod"
  aws_region             = var.aws_region
  cognito_user_pool_id   = module.cognito.user_pool_id
  leaderboard_table_name = module.dynamodb.leaderboard_table_name
  leaderboard_table_arn  = module.dynamodb.leaderboard_table_arn
}

module "sns" {
  source = "../../modules/sns"
  env    = "prod"
  # apns_certificate and fcm_server_key set via TF_VAR_ env vars in CI — never committed
}

output "api_endpoint" {
  value = module.api_gateway.api_endpoint
}

output "appsync_realtime_url" {
  value = module.appsync.realtime_url
}

output "cognito_user_pool_id" {
  value = module.cognito.user_pool_id
}

output "cognito_client_id" {
  value = module.cognito.client_id
}
