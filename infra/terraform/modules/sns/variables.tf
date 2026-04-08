variable "env" {
  type = string
}

variable "apns_certificate" {
  description = "APNs certificate for iOS push notifications (leave empty to skip)"
  type        = string
  default     = ""
  sensitive   = true
}

variable "apns_private_key" {
  description = "APNs private key"
  type        = string
  default     = ""
  sensitive   = true
}

variable "fcm_server_key" {
  description = "FCM server key for Android push notifications (leave empty to skip)"
  type        = string
  default     = ""
  sensitive   = true
}
