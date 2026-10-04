# Internet-facing ALB in front of web and Keycloak (the API has no target group). It speaks HTTP
# only and admits only CloudFront (network.tf); on top of that, a request is forwarded only with
# the secret X-Origin-Verify header a distribution adds (cloudfront.tf), so the listener's
# default is a fixed 403.

locals {
  origin_verify_header = "X-Origin-Verify"
}

resource "aws_lb" "main" {
  name               = local.names.alb
  load_balancer_type = "application"
  internal           = false
  subnets            = aws_subnet.public[*].id
  security_groups    = [aws_security_group.alb.id]

  # SignalR WebSockets and long polls must outlive the 60 s default.
  idle_timeout               = 3600
  drop_invalid_header_fields = true

  tags = { Name = local.names.alb }
}

resource "aws_lb_target_group" "web" {
  name        = "${local.name_prefix}-web-tg"
  vpc_id      = aws_vpc.main.id
  target_type = "ip"
  protocol    = "HTTP"
  port        = 8080

  deregistration_delay = 30

  health_check {
    path     = "/"
    matcher  = "200"
    interval = 30
  }

  tags = { Name = "${local.name_prefix}-web-tg" }
}

resource "aws_lb_target_group" "keycloak" {
  name        = "${local.name_prefix}-kc-tg"
  vpc_id      = aws_vpc.main.id
  target_type = "ip"
  protocol    = "HTTP"
  port        = 8080

  deregistration_delay = 30

  # Keycloak serves its health endpoints on the management port, not the HTTP one.
  health_check {
    path     = "/health/ready"
    port     = "9000"
    matcher  = "200"
    interval = 30
  }

  tags = { Name = "${local.name_prefix}-kc-tg" }
}

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.main.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type = "fixed-response"

    fixed_response {
      content_type = "text/plain"
      message_body = "Forbidden"
      status_code  = "403"
    }
  }
}

resource "aws_lb_listener_rule" "web" {
  listener_arn = aws_lb_listener.http.arn
  priority     = 10

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.web.arn
  }

  condition {
    http_header {
      http_header_name = local.origin_verify_header
      values           = [random_password.origin_verify_web.result]
    }
  }
}

resource "aws_lb_listener_rule" "keycloak" {
  listener_arn = aws_lb_listener.http.arn
  priority     = 20

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.keycloak.arn
  }

  condition {
    http_header {
      http_header_name = local.origin_verify_header
      values           = [random_password.origin_verify_keycloak.result]
    }
  }
}
