resource "aws_cognito_user_pool" "players" {
  name = "${var.env}-mobile-idle-players"

  username_attributes      = ["email"]
  auto_verified_attributes = ["email"]

  password_policy {
    minimum_length    = 8
    require_uppercase = false
    require_lowercase = false
    require_numbers   = false
    require_symbols   = false
  }

  schema {
    name                = "playerId"
    attribute_data_type = "String"
    mutable             = false
    required            = false

    string_attribute_constraints {
      min_length = 1
      max_length = 64
    }
  }

  tags = {
    Environment = var.env
    Project     = "mobile-idle-builder"
  }
}

resource "aws_cognito_user_pool_client" "mobile_client" {
  name         = "${var.env}-mobile-client"
  user_pool_id = aws_cognito_user_pool.players.id

  generate_secret                      = false
  allowed_oauth_flows_user_pool_client = true
  allowed_oauth_flows                  = ["code", "implicit"]
  allowed_oauth_scopes                 = ["openid", "email", "profile"]

  supported_identity_providers = var.identity_providers

  callback_urls = var.callback_urls
  logout_urls   = var.logout_urls

  explicit_auth_flows = [
    "ALLOW_USER_SRP_AUTH",
    "ALLOW_REFRESH_TOKEN_AUTH",
  ]
}

resource "aws_cognito_identity_pool" "main" {
  identity_pool_name               = "${var.env}-mobile-idle"
  allow_unauthenticated_identities = false

  cognito_identity_providers {
    client_id               = aws_cognito_user_pool_client.mobile_client.id
    provider_name           = aws_cognito_user_pool.players.endpoint
    server_side_token_check = true
  }
}
