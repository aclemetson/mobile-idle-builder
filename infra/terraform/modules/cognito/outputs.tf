output "user_pool_id" {
  value = aws_cognito_user_pool.players.id
}

output "user_pool_arn" {
  value = aws_cognito_user_pool.players.arn
}

output "client_id" {
  value = aws_cognito_user_pool_client.mobile_client.id
}

output "identity_pool_id" {
  value = aws_cognito_identity_pool.main.id
}
