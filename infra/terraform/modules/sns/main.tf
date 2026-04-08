resource "aws_sns_topic" "game_notifications" {
  name = "${var.env}-mobile-idle-notifications"

  tags = {
    Environment = var.env
    Project     = "mobile-idle-builder"
  }
}

# Platform application for iOS (APNs)
resource "aws_sns_platform_application" "ios" {
  count    = var.apns_certificate != "" ? 1 : 0
  name     = "${var.env}-mobile-idle-ios"
  platform = "APNS"

  platform_credential = var.apns_private_key
  platform_principal  = var.apns_certificate
}

# Platform application for Android (FCM)
resource "aws_sns_platform_application" "android" {
  count    = var.fcm_server_key != "" ? 1 : 0
  name     = "${var.env}-mobile-idle-android"
  platform = "GCM"

  platform_credential = var.fcm_server_key
}
