variable "env" {
  description = "Deployment environment (dev | staging | prod)"
  type        = string
}

variable "enable_pitr" {
  description = "Enable point-in-time recovery (recommended for staging and prod)"
  type        = bool
  default     = false
}
