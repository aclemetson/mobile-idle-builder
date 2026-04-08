output "player_save_table_name" {
  value = aws_dynamodb_table.player_save.name
}

output "player_save_table_arn" {
  value = aws_dynamodb_table.player_save.arn
}

output "leaderboard_table_name" {
  value = aws_dynamodb_table.leaderboard.name
}

output "leaderboard_table_arn" {
  value = aws_dynamodb_table.leaderboard.arn
}
