# Dedicated VPC, two public subnets, no NAT gateway (ADR-0023, R-21). Tasks run with a public IP
# only for egress (Docker Hub, Neon, SMTP, AWS APIs); the security groups below are the only
# inbound control: nothing but CloudFront reaches the ALB, nothing but the ALB reaches web and
# Keycloak, nothing but the web tasks reaches the API.

locals {
  vpc_cidr     = "10.40.0.0/16"
  subnet_count = 2
}

data "aws_availability_zones" "available" {
  state = "available"
}

# CloudFront's origin-facing addresses, maintained by AWS.
data "aws_ec2_managed_prefix_list" "cloudfront" {
  name = "com.amazonaws.global.cloudfront.origin-facing"
}

resource "aws_vpc" "main" {
  cidr_block = local.vpc_cidr
  # Both are needed by the Cloud Map private namespace and the VPC resolver (nginx, tasks).
  enable_dns_support   = true
  enable_dns_hostnames = true

  tags = { Name = local.names.vpc }
}

resource "aws_internet_gateway" "main" {
  vpc_id = aws_vpc.main.id

  tags = { Name = local.names.igw }
}

resource "aws_subnet" "public" {
  count = local.subnet_count

  vpc_id            = aws_vpc.main.id
  cidr_block        = cidrsubnet(local.vpc_cidr, 8, count.index)
  availability_zone = data.aws_availability_zones.available.names[count.index]
  # Public IPs are requested per task (assign_public_ip in ecs.tf), not for every ENI.
  map_public_ip_on_launch = false

  tags = { Name = "${local.name_prefix}-public-${count.index + 1}" }
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.main.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.main.id
  }

  tags = { Name = "${local.name_prefix}-public-rt" }
}

resource "aws_route_table_association" "public" {
  count = local.subnet_count

  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

# --- Security groups ----------------------------------------------------------------------------
# Rules are separate resources (no inline blocks), so groups can reference each other without a
# cycle.

resource "aws_security_group" "alb" {
  name        = "${local.name_prefix}-alb-sg"
  description = "BirraPoint ALB: HTTP only from CloudFront"
  vpc_id      = aws_vpc.main.id

  tags = { Name = "${local.name_prefix}-alb-sg" }
}

resource "aws_security_group" "web" {
  name        = "${local.name_prefix}-web-sg"
  description = "BirraPoint web tasks: 8080 only from the ALB"
  vpc_id      = aws_vpc.main.id

  tags = { Name = "${local.name_prefix}-web-sg" }
}

resource "aws_security_group" "api" {
  name        = "${local.name_prefix}-api-sg"
  description = "BirraPoint API tasks: 8080 only from the web tasks"
  vpc_id      = aws_vpc.main.id

  tags = { Name = "${local.name_prefix}-api-sg" }
}

resource "aws_security_group" "keycloak" {
  name        = "${local.name_prefix}-kc-sg"
  description = "BirraPoint Keycloak tasks: 8080 and 9000 only from the ALB"
  vpc_id      = aws_vpc.main.id

  tags = { Name = "${local.name_prefix}-kc-sg" }
}

# TLS ends at CloudFront; CloudFront reaches the ALB over HTTP, from its own address ranges only.
# Note: a rule on a managed prefix list counts as one rule per prefix list entry against the
# security group's rules quota (default 60); CloudFront's list is large, see the README.
resource "aws_vpc_security_group_ingress_rule" "alb_from_cloudfront" {
  security_group_id = aws_security_group.alb.id
  description       = "HTTP from CloudFront origin-facing addresses"
  prefix_list_id    = data.aws_ec2_managed_prefix_list.cloudfront.id
  from_port         = 80
  to_port           = 80
  ip_protocol       = "tcp"
}

resource "aws_vpc_security_group_ingress_rule" "web_from_alb" {
  security_group_id            = aws_security_group.web.id
  description                  = "nginx from the ALB"
  referenced_security_group_id = aws_security_group.alb.id
  from_port                    = 8080
  to_port                      = 8080
  ip_protocol                  = "tcp"
}

resource "aws_vpc_security_group_ingress_rule" "keycloak_from_alb" {
  security_group_id            = aws_security_group.keycloak.id
  description                  = "Keycloak HTTP from the ALB"
  referenced_security_group_id = aws_security_group.alb.id
  from_port                    = 8080
  to_port                      = 8080
  ip_protocol                  = "tcp"
}

# Keycloak's management interface (/health/ready), used by the ALB health check.
resource "aws_vpc_security_group_ingress_rule" "keycloak_health_from_alb" {
  security_group_id            = aws_security_group.keycloak.id
  description                  = "Keycloak health checks from the ALB"
  referenced_security_group_id = aws_security_group.alb.id
  from_port                    = 9000
  to_port                      = 9000
  ip_protocol                  = "tcp"
}

resource "aws_vpc_security_group_ingress_rule" "api_from_web" {
  security_group_id            = aws_security_group.api.id
  description                  = "API from the web tasks (nginx reverse proxy)"
  referenced_security_group_id = aws_security_group.web.id
  from_port                    = 8080
  to_port                      = 8080
  ip_protocol                  = "tcp"
}

# The ALB only talks to the targets it forwards to.
resource "aws_vpc_security_group_egress_rule" "alb_to_web" {
  security_group_id            = aws_security_group.alb.id
  description                  = "To the web tasks"
  referenced_security_group_id = aws_security_group.web.id
  from_port                    = 8080
  to_port                      = 8080
  ip_protocol                  = "tcp"
}

resource "aws_vpc_security_group_egress_rule" "alb_to_keycloak" {
  security_group_id            = aws_security_group.alb.id
  description                  = "To the Keycloak tasks"
  referenced_security_group_id = aws_security_group.keycloak.id
  from_port                    = 8080
  to_port                      = 8080
  ip_protocol                  = "tcp"
}

resource "aws_vpc_security_group_egress_rule" "alb_to_keycloak_health" {
  security_group_id            = aws_security_group.alb.id
  description                  = "Keycloak health checks"
  referenced_security_group_id = aws_security_group.keycloak.id
  from_port                    = 9000
  to_port                      = 9000
  ip_protocol                  = "tcp"
}

# Tasks have no NAT: all their outbound traffic (images, Neon, SMTP, AWS APIs, Keycloak's public
# URL for the API's token validation) leaves through their own public IP.
resource "aws_vpc_security_group_egress_rule" "tasks_all" {
  for_each = {
    web      = aws_security_group.web.id
    api      = aws_security_group.api.id
    keycloak = aws_security_group.keycloak.id
  }

  security_group_id = each.value
  description       = "All outbound (no NAT gateway)"
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "-1"
}
