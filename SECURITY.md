# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in AgentFlightRecorder.NET, please report it responsibly.

**Do not open a public GitHub issue for security vulnerabilities.**

Instead, please email the maintainers directly or use GitHub's private vulnerability reporting feature on the repository's Security tab.

## Scope

AgentFlightRecorder.NET provides:
- **Redaction** — API key and PII scrubbing before events are persisted
- **Integrity** — SHA-256 hash chain and optional HMAC signing for tamper detection

These features are defense-in-depth measures. They are not a substitute for proper access controls on trace files and infrastructure.

## Important Notes

- Redaction runs **before** events are written to the sink. Raw values are never persisted when redaction is enabled.
- HMAC signing keys should be stored securely (e.g., environment variables, key vaults) and never committed to source control.
- Trace files may contain sensitive data if redaction is not configured. Treat trace files with the same access controls as your application logs.

## Supported Versions

| Version | Supported |
|---------|-----------|
| 0.1.x   | Yes       |
