variable "env" {
  type = string
}

variable "identity_providers" {
  description = "Enabled identity providers, e.g. [\"SignInWithApple\", \"Google\"]"
  type        = list(string)
  default     = ["COGNITO"]
}

variable "callback_urls" {
  description = "OAuth callback URLs (deep links)"
  type        = list(string)
  default     = ["mobileidle://callback"]
}

variable "logout_urls" {
  type    = list(string)
  default = ["mobileidle://logout"]
}
