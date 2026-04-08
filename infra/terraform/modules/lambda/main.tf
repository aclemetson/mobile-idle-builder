resource "aws_iam_role" "lambda_exec" {
  name = "${var.env}-mobile-idle-lambda-exec"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Action    = "sts:AssumeRole"
      Effect    = "Allow"
      Principal = { Service = "lambda.amazonaws.com" }
    }]
  })
}

resource "aws_iam_role_policy_attachment" "basic_exec" {
  role       = aws_iam_role.lambda_exec.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_iam_role_policy" "dynamodb_access" {
  name = "${var.env}-lambda-dynamodb"
  role = aws_iam_role.lambda_exec.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:UpdateItem", "dynamodb:Query"]
      Resource = var.dynamodb_table_arns
    }]
  })
}

# Save read Lambda
resource "aws_lambda_function" "save_read" {
  function_name = "${var.env}-mobile-idle-save-read"
  role          = aws_iam_role.lambda_exec.arn
  package_type  = "Image"
  image_uri     = "${var.ecr_repo_url}/save-read:${var.image_tag}"
  timeout       = 10
  memory_size   = 256

  environment {
    variables = {
      ENV                = var.env
      PLAYER_SAVE_TABLE  = var.player_save_table_name
    }
  }

  tags = { Environment = var.env, Project = "mobile-idle-builder" }
}

# Save write Lambda
resource "aws_lambda_function" "save_write" {
  function_name = "${var.env}-mobile-idle-save-write"
  role          = aws_iam_role.lambda_exec.arn
  package_type  = "Image"
  image_uri     = "${var.ecr_repo_url}/save-write:${var.image_tag}"
  timeout       = 10
  memory_size   = 256

  environment {
    variables = {
      ENV                = var.env
      PLAYER_SAVE_TABLE  = var.player_save_table_name
    }
  }

  tags = { Environment = var.env, Project = "mobile-idle-builder" }
}

# Prestige validator Lambda (server-side net worth check)
resource "aws_lambda_function" "prestige_validator" {
  function_name = "${var.env}-mobile-idle-prestige-validator"
  role          = aws_iam_role.lambda_exec.arn
  package_type  = "Image"
  image_uri     = "${var.ecr_repo_url}/prestige-validator:${var.image_tag}"
  timeout       = 15
  memory_size   = 512

  environment {
    variables = {
      ENV               = var.env
      PLAYER_SAVE_TABLE = var.player_save_table_name
    }
  }

  tags = { Environment = var.env, Project = "mobile-idle-builder" }
}

# Offline production calculator Lambda (prevents client-side cheating)
resource "aws_lambda_function" "offline_calc" {
  function_name = "${var.env}-mobile-idle-offline-calc"
  role          = aws_iam_role.lambda_exec.arn
  package_type  = "Image"
  image_uri     = "${var.ecr_repo_url}/offline-calc:${var.image_tag}"
  timeout       = 30
  memory_size   = 512

  environment {
    variables = {
      ENV               = var.env
      PLAYER_SAVE_TABLE = var.player_save_table_name
    }
  }

  tags = { Environment = var.env, Project = "mobile-idle-builder" }
}
