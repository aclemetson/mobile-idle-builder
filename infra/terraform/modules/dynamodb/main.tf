resource "aws_dynamodb_table" "player_save" {
  name           = "${var.env}-mobile-idle-player-save"
  billing_mode   = "PAY_PER_REQUEST"
  hash_key       = "playerId"
  range_key      = "saveVersion"

  attribute {
    name = "playerId"
    type = "S"
  }

  attribute {
    name = "saveVersion"
    type = "N"
  }

  ttl {
    attribute_name = "expiresAt"
    enabled        = true
  }

  point_in_time_recovery {
    enabled = var.enable_pitr
  }

  tags = {
    Environment = var.env
    Project     = "mobile-idle-builder"
  }
}

resource "aws_dynamodb_table" "leaderboard" {
  name         = "${var.env}-mobile-idle-leaderboard"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "boardId"
  range_key    = "score"

  attribute {
    name = "boardId"
    type = "S"
  }

  attribute {
    name = "score"
    type = "N"
  }

  tags = {
    Environment = var.env
    Project     = "mobile-idle-builder"
  }
}
