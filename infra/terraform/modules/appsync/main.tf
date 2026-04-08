resource "aws_appsync_graphql_api" "main" {
  name                = "${var.env}-mobile-idle-realtime"
  authentication_type = "AMAZON_COGNITO_USER_POOLS"

  user_pool_config {
    user_pool_id   = var.cognito_user_pool_id
    aws_region     = var.aws_region
    default_action = "ALLOW"
  }

  log_config {
    cloudwatch_logs_role_arn = aws_iam_role.appsync_logs.arn
    field_log_level          = var.env == "prod" ? "ERROR" : "ALL"
  }

  tags = {
    Environment = var.env
    Project     = "mobile-idle-builder"
  }
}

resource "aws_iam_role" "appsync_logs" {
  name = "${var.env}-mobile-idle-appsync-logs"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Action    = "sts:AssumeRole"
      Effect    = "Allow"
      Principal = { Service = "appsync.amazonaws.com" }
    }]
  })
}

resource "aws_iam_role_policy_attachment" "appsync_logs" {
  role       = aws_iam_role.appsync_logs.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSAppSyncPushToCloudWatchLogs"
}

resource "aws_appsync_datasource" "leaderboard" {
  api_id           = aws_appsync_graphql_api.main.id
  name             = "leaderboard"
  type             = "AMAZON_DYNAMODB"
  service_role_arn = aws_iam_role.appsync_dynamodb.arn

  dynamodb_config {
    table_name = var.leaderboard_table_name
    region     = var.aws_region
  }
}

resource "aws_iam_role" "appsync_dynamodb" {
  name = "${var.env}-mobile-idle-appsync-dynamodb"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Action    = "sts:AssumeRole"
      Effect    = "Allow"
      Principal = { Service = "appsync.amazonaws.com" }
    }]
  })
}

resource "aws_iam_role_policy" "appsync_dynamodb" {
  name = "${var.env}-appsync-dynamodb"
  role = aws_iam_role.appsync_dynamodb.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:Query", "dynamodb:Scan"]
      Resource = var.leaderboard_table_arn
    }]
  })
}
