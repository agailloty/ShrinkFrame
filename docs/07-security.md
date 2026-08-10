# Security

## Trust model

The POC has no authentication and must be deployed only on a trusted LAN or Tailscale network. Anyone who can reach it can consume CPU/disk and publish assets through configured Immich connections. The UI and README must display this limitation. Public exposure is unsupported.

## Secrets

- Encrypt Immich API keys with ASP.NET Core Data Protection.
- Persist the Data Protection key ring under `/data/keys` with restrictive permissions.
- Return only `hasApiKey` and replacement status to the browser; never return encrypted payloads or recognizable key fragments.
- Redact `x-api-key` and multipart bodies from logs.
- Never put secrets in query strings.
- Disable a connection rather than repeatedly using failing credentials.

## HTTP and request safety

- POC listens on HTTP; HTTPS termination is a documented future reverse-proxy concern.
- Enforce configured upload limits in Kestrel/endpoints and stream bodies.
- Validate MIME by probing content, not extension or browser header.
- Apply antiforgery protections to browser-initiated state changes where supported by the selected endpoint style.
- Use same-site cookies/circuit defaults even without authentication.
- Protect against CSRF-style LAN attacks by validating Origin/Host for mutating browser requests and documenting allowed hosts.
- Set security headers appropriate to a self-hosted Blazor application.

## SSRF and Immich URLs

The user intentionally configures LAN destinations, so generic private-address blocking is inappropriate. Still:

- permit only `http` and `https`;
- reject credentials in URLs;
- normalize and display the resolved base URL;
- do not follow redirects to a different origin for authenticated calls;
- bound timeouts and response sizes;
- make invalid-certificate acceptance an explicit per-connection warning.

## Process and filesystem

- Never accept raw FFmpeg arguments.
- Use typed validated options and `ArgumentList`.
- Run the container and FFmpeg non-root.
- Restrict writes to `/data` and temporary subpaths.
- Use opaque artifact keys and safe deletion rules from storage documentation.

## Destructive actions

ShrinkFrame never deletes original Immich assets. Local deletion requires explicit confirmation and affects only artifacts owned by a known job. A forced `NotBeneficial` publication requires a separate explicit confirmation.
