# Customer Staging Forwarded Headers

## Purpose

The Customer and Asset Service will run behind Nginx on the staging VM. Nginx
will terminate TLS and proxy only to the Docker-published loopback endpoint
`127.0.0.1:8080`.

## Observed Proxy Path

The running `assms-customer-service` container uses Docker's default bridge
network `172.17.0.0/16`. A held request from the VM loopback-published port
was observed inside the container as coming from Docker bridge gateway
`172.17.0.1`, represented by the dual-stack socket as `::ffff:172.17.0.1`.

## Trusted Headers

The application trusts only `172.17.0.1` through
`ForwardedHeadersOptions.KnownProxies`, mapped to IPv6 to match the observed
dual-stack connection. It processes only `X-Forwarded-For` and
`X-Forwarded-Proto`.

`app.UseForwardedHeaders()` runs before `app.UseHttpsRedirection()`. This lets
the application recognize the HTTPS scheme that Nginx terminated and prevents
an HTTP-to-HTTPS redirect loop.

## Nginx Requirement

When Nginx is installed in a later approved deployment step, it must proxy to
`http://127.0.0.1:8080` and set `Host`, `X-Forwarded-For`, and
`X-Forwarded-Proto`. Do not enable the unrestricted
`ASPNETCORE_FORWARDEDHEADERS_ENABLED` environment variable.
