# Security

## Trust model

Version 1.0 has no authentication and must be deployed only on a trusted LAN or Tailscale network. Anyone who can reach it can consume CPU/disk and publish assets through configured Immich connections. The UI and README must display this limitation. Public exposure is unsupported.

## Secrets

- Encrypt Immich API keys with ASP.NET Core Data Protection.
- Persist the Data Protection key ring under `/data/keys` with restrictive permissions.
- Return only `hasApiKey` and replacement status to the browser; never return encrypted payloads or recognizable key fragments.
- Redact `x-api-key` and multipart bodies from logs.
- Never put secrets in query strings.
- Disable a connection rather than repeatedly using failing credentials.
- Keep `DataProtection:KeyRingPath` on the same persistent volume as the database lifecycle. If that
  key ring is lost or replaced, saved credentials fail with `connection.api_key.unavailable`; restore
  the original key ring or use the replace-key field. ShrinkFrame never includes the encrypted envelope
  or plaintext in that error.

## HTTP and request safety

- Version 1.0 listens on HTTP; HTTPS termination belongs at the documented reverse proxy.
- Enforce configured upload limits in Kestrel/endpoints and stream bodies.
- Validate MIME by probing content, not extension or browser header.
- Apply antiforgery protections to browser-initiated state changes where supported by the selected endpoint style.
- Use same-site cookies/circuit defaults even without authentication.
- Protect against CSRF-style LAN attacks by validating Origin/Host for mutating browser requests and documenting allowed hosts.
- Set security headers appropriate to a self-hosted Blazor application.

### Browser upload request policy

Browser acquisition uses one raw-body `POST` per file under `/api/browser-batches/{batchId}/files`.
The browser first obtains an ASP.NET Core antiforgery token and sends its request half in the
`RequestVerificationToken` header; the cookie half remains same-site. Both batch creation and file
upload carry `RequireAntiforgeryTokenAttribute` endpoint metadata. The upload never uses form or
`IFormFile` binding.

All mutating browser-batch requests must also provide an `Origin` that exactly matches an entry in
`BrowserUploads:AllowedOrigins`. Its authority must equal the request `Host`. Empty, malformed,
foreign, or Host-mismatched origins receive `request.origin.rejected`. Deployments using a hostname,
IP address, port, or reverse-proxy origin other than the checked-in local defaults must configure the
complete public HTTP(S) origin explicitly. ASP.NET Core host filtering remains an additional deployment
control through `AllowedHosts`.

The per-file limit is `BrowserUploads:MaximumFileSizeBytes` (20 GiB by default). The endpoint raises
Kestrel's per-request body limit before reading and independently counts streamed bytes, so both known
and unknown content lengths are bounded. `BrowserUploads:BufferSizeBytes` bounds the pooled copy buffer.

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
