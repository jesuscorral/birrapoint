# Two CloudFront distributions give the apps HTTPS on free *.cloudfront.net certificates
# (ADR-0023). Caching is disabled (dynamic app, SignalR WebSockets pass through), every viewer
# header, cookie and query string reaches the origin, and the origin is the ALB over HTTP.

data "aws_cloudfront_cache_policy" "caching_disabled" {
  name = "Managed-CachingDisabled"
}

data "aws_cloudfront_origin_request_policy" "all_viewer" {
  name = "Managed-AllViewer"
}

locals {
  distributions = {
    web = {
      comment       = local.names.web
      origin_secret = random_password.origin_verify_web.result
    }
    keycloak = {
      comment       = local.names.keycloak
      origin_secret = random_password.origin_verify_keycloak.result
    }
  }
}

resource "aws_cloudfront_distribution" "web" {
  comment             = local.distributions.web.comment
  enabled             = true
  price_class         = "PriceClass_100"
  default_root_object = ""

  origin {
    origin_id   = "alb"
    domain_name = aws_lb.main.dns_name

    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "http-only"
      origin_ssl_protocols   = ["TLSv1.2"]
    }

    custom_header {
      name  = local.origin_verify_header
      value = local.distributions.web.origin_secret
    }

    # The ALB overwrites X-Forwarded-Proto with "http". The standard Forwarded header survives and
    # tells the apps the viewer used HTTPS (Keycloak: KC_PROXY_HEADERS=forwarded).
    custom_header {
      name  = "Forwarded"
      value = "proto=https"
    }
  }

  default_cache_behavior {
    target_origin_id         = "alb"
    viewer_protocol_policy   = "redirect-to-https"
    allowed_methods          = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
    cached_methods           = ["GET", "HEAD"]
    cache_policy_id          = data.aws_cloudfront_cache_policy.caching_disabled.id
    origin_request_policy_id = data.aws_cloudfront_origin_request_policy.all_viewer.id
    compress                 = true
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = true
  }

  tags = { Name = local.distributions.web.comment }
}

resource "aws_cloudfront_distribution" "keycloak" {
  comment             = local.distributions.keycloak.comment
  enabled             = true
  price_class         = "PriceClass_100"
  default_root_object = ""

  origin {
    origin_id   = "alb"
    domain_name = aws_lb.main.dns_name

    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "http-only"
      origin_ssl_protocols   = ["TLSv1.2"]
    }

    custom_header {
      name  = local.origin_verify_header
      value = local.distributions.keycloak.origin_secret
    }

    custom_header {
      name  = "Forwarded"
      value = "proto=https"
    }
  }

  default_cache_behavior {
    target_origin_id         = "alb"
    viewer_protocol_policy   = "redirect-to-https"
    allowed_methods          = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
    cached_methods           = ["GET", "HEAD"]
    cache_policy_id          = data.aws_cloudfront_cache_policy.caching_disabled.id
    origin_request_policy_id = data.aws_cloudfront_origin_request_policy.all_viewer.id
    compress                 = true
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = true
  }

  tags = { Name = local.distributions.keycloak.comment }
}
