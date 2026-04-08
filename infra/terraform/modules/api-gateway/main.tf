resource "aws_apigatewayv2_api" "main" {
  name          = "${var.env}-mobile-idle-api"
  protocol_type = "HTTP"
  cors_configuration {
    allow_origins = ["*"]
    allow_methods = ["GET", "PUT", "POST", "OPTIONS"]
    allow_headers = ["Content-Type", "Authorization"]
  }
}

resource "aws_apigatewayv2_authorizer" "cognito" {
  api_id           = aws_apigatewayv2_api.main.id
  authorizer_type  = "JWT"
  identity_sources = ["$request.header.Authorization"]
  name             = "cognito-jwt"

  jwt_configuration {
    audience = [var.cognito_client_id]
    issuer   = "https://cognito-idp.${var.aws_region}.amazonaws.com/${var.cognito_user_pool_id}"
  }
}

resource "aws_apigatewayv2_stage" "default" {
  api_id      = aws_apigatewayv2_api.main.id
  name        = "$default"
  auto_deploy = true
}

# ── Save routes ───────────────────────────────────────────────────────────────

resource "aws_apigatewayv2_integration" "save_read" {
  api_id                 = aws_apigatewayv2_api.main.id
  integration_type       = "AWS_PROXY"
  integration_uri        = var.lambda_save_read_invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "get_save" {
  api_id             = aws_apigatewayv2_api.main.id
  route_key          = "GET /save/{playerId}"
  target             = "integrations/${aws_apigatewayv2_integration.save_read.id}"
  authorization_type = "JWT"
  authorizer_id      = aws_apigatewayv2_authorizer.cognito.id
}

resource "aws_apigatewayv2_integration" "save_write" {
  api_id                 = aws_apigatewayv2_api.main.id
  integration_type       = "AWS_PROXY"
  integration_uri        = var.lambda_save_write_invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "put_save" {
  api_id             = aws_apigatewayv2_api.main.id
  route_key          = "PUT /save/{playerId}"
  target             = "integrations/${aws_apigatewayv2_integration.save_write.id}"
  authorization_type = "JWT"
  authorizer_id      = aws_apigatewayv2_authorizer.cognito.id
}

# ── Game logic routes ─────────────────────────────────────────────────────────

resource "aws_apigatewayv2_integration" "prestige_validator" {
  api_id                 = aws_apigatewayv2_api.main.id
  integration_type       = "AWS_PROXY"
  integration_uri        = var.lambda_prestige_validator_invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "post_prestige" {
  api_id             = aws_apigatewayv2_api.main.id
  route_key          = "POST /prestige/validate"
  target             = "integrations/${aws_apigatewayv2_integration.prestige_validator.id}"
  authorization_type = "JWT"
  authorizer_id      = aws_apigatewayv2_authorizer.cognito.id
}

resource "aws_apigatewayv2_integration" "offline_calc" {
  api_id                 = aws_apigatewayv2_api.main.id
  integration_type       = "AWS_PROXY"
  integration_uri        = var.lambda_offline_calc_invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "post_offline_sync" {
  api_id             = aws_apigatewayv2_api.main.id
  route_key          = "POST /sync/offline"
  target             = "integrations/${aws_apigatewayv2_integration.offline_calc.id}"
  authorization_type = "JWT"
  authorizer_id      = aws_apigatewayv2_authorizer.cognito.id
}

# ── Lambda permissions ────────────────────────────────────────────────────────

locals {
  lambda_permissions = {
    save_read          = var.lambda_save_read_arn
    save_write         = var.lambda_save_write_arn
    prestige_validator = var.lambda_prestige_validator_arn
    offline_calc       = var.lambda_offline_calc_arn
  }
}

resource "aws_lambda_permission" "api_invoke" {
  for_each      = local.lambda_permissions
  statement_id  = "AllowAPIGatewayInvoke-${each.key}"
  action        = "lambda:InvokeFunction"
  function_name = each.value
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.main.execution_arn}/*/*"
}
