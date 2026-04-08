output "save_read_arn" {
  value = aws_lambda_function.save_read.arn
}

output "save_read_invoke_arn" {
  value = aws_lambda_function.save_read.invoke_arn
}

output "save_write_arn" {
  value = aws_lambda_function.save_write.arn
}

output "save_write_invoke_arn" {
  value = aws_lambda_function.save_write.invoke_arn
}

output "prestige_validator_arn" {
  value = aws_lambda_function.prestige_validator.arn
}

output "prestige_validator_invoke_arn" {
  value = aws_lambda_function.prestige_validator.invoke_arn
}

output "offline_calc_arn" {
  value = aws_lambda_function.offline_calc.arn
}

output "offline_calc_invoke_arn" {
  value = aws_lambda_function.offline_calc.invoke_arn
}
