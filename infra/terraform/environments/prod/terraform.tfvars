aws_region   = "us-east-1"
ecr_repo_url = "123456789012.dkr.ecr.us-east-1.amazonaws.com/mobile-idle"
# image_tag intentionally omitted — must be set explicitly on deploy:
#   make deploy ENV=prod image_tag=abc1234
