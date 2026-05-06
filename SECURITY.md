# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| Latest (`main`) | Yes |

## Reporting a Vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Email: [eng.anasmohad@gmail.com](mailto:eng.anasmohad@gmail.com)

Include:
- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

You will receive a response within 48 hours. If confirmed, a fix will be prioritised and released as soon as possible.

## Security Model

- **Authentication:** Session-cookie auth (BCrypt-hashed password). No JWT.
- **IDOR protection:** All request-scoped queries include `WHERE TokenId = @tokenId`.
- **Webhook receiver:** Intentionally unauthenticated (`[AllowAnonymous]`) — external callers post here.
- **Secret management:** All credentials are supplied via environment variables; no secrets are committed.
- **Nginx headers:** `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff` are set.
- **SQL Server:** Port 1433 is not exposed publicly in production.
- **SEQ:** Ports 5341/5342 are bound to `127.0.0.1` only.

## Dependency Scanning

Dependencies are reviewed manually before each release. Automated scanning via GitHub Dependabot is planned.

## Known Limitations

- Rate limiting is applied per-token but not globally at the IP level in the default configuration.
- The custom-response `Headers` field accepts arbitrary JSON strings — consumers are responsible for not injecting malicious response headers.
