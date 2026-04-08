ENV ?= dev

TF_DIR := terraform/environments/$(ENV)

.PHONY: plan deploy destroy local-lambda fmt validate

plan:
	cd $(TF_DIR) && terraform init -input=false && terraform plan -var-file=terraform.tfvars

deploy:
	cd $(TF_DIR) && terraform init -input=false && terraform apply -var-file=terraform.tfvars -auto-approve

destroy:
ifeq ($(ENV),prod)
	$(error Cannot destroy prod via Makefile. Run terraform destroy manually.)
endif
	cd $(TF_DIR) && terraform init -input=false && terraform destroy -var-file=terraform.tfvars -auto-approve

local-lambda:
ifndef FUNCTION
	$(error Provide FUNCTION=<name>, e.g. make local-lambda FUNCTION=prestige-validator)
endif
	docker run --rm \
		-e AWS_ACCESS_KEY_ID=local \
		-e AWS_SECRET_ACCESS_KEY=local \
		-p 9000:8080 \
		mobile-idle-$(FUNCTION):latest

fmt:
	terraform fmt -recursive terraform/

validate:
	cd $(TF_DIR) && terraform init -input=false && terraform validate
