# Configuration

Every setting the API reads. Configuration comes from environment variables (section 7 of `ARCHITECTURE.md`): a key becomes a variable with `__` in place of `:`, and a list item gets its index (`Host__Cors__AllowedOrigins__0`). `appsettings.json` holds only the defaults below that are marked as coming from it; `appsettings.Example.json` lists the keys with placeholders.

Each process checks its settings when it starts. If a required setting is missing or wrong, it stops and names the key. A test fails when a key the code binds has no row here.

"Required" means the process does not start without it. **Development** marks what `ASPNETCORE_ENVIRONMENT=Development` relaxes.

| Key | Required | Default | What it does |
| --- | --- | --- | --- |
| `Host:Role` | Required | none | The process type: `web` serves the API, `worker` handles messages, `all` does both in one process (local development and tests) |
| `Host:ShutdownTimeout` | Optional | `00:00:30` | How long a stopping process may take to finish the work it has |
| `Host:Cors:AllowedOrigins` | Optional | none | The browser origins that may call the API, such as `https://app.example.com`. With none, no browser origin may call |
| `ConnectionStrings:Database` | Required | none | The application role's connection. Requests and handlers use it, and Wolverine too unless `ConnectionStrings:Messaging` is set |
| `ConnectionStrings:Messaging` | Optional | `ConnectionStrings:Database` | The application role's direct or session-mode connection for Wolverine's message store. Needed when `ConnectionStrings:Database` goes through a pooler in transaction mode |
| `ConnectionStrings:Migrations` | Required by `migrate` only | none | The owner role's direct connection. Given only to the `migrate` command, never to a running process (R3) |
| `Authentication:Clerk:Issuer` | Required | none | The identity provider's issuer URL: the tokens' issuer and the source of their signing keys |
| `Authentication:Clerk:AuthorizedParties` | Required outside Development. **Development:** may be empty | none | The origins of the clients allowed to use the API (the tokens' `azp`). With none, a token issued to any origin is accepted |
| `ForwardedHeaders:KnownProxies` | Optional | none | IP addresses of trusted reverse proxies. With no proxy and no network, forwarded headers are not read |
| `ForwardedHeaders:KnownNetworks` | Optional | none | Trusted proxy networks in CIDR notation |
| `RateLimiting:PermitLimit` | Optional | `1000` (`appsettings.json`) | Requests each user may make in a window, in each process |
| `RateLimiting:Window` | Optional | `00:01:00` (`appsettings.json`) | The rate limit's window |
| `RateLimiting:InvitationAccept:PermitLimit` | Optional | `10` | Invitation accepts each user may make in a window, on top of the general limit |
| `RateLimiting:InvitationAccept:Window` | Optional | `00:01:00` | The window of the invitation accept limit |
| `ControlPlane:FirstSystemAdminEmail` | Required while the staff list is empty | none | The verified email address of the first system admin, who is granted on their first request to `/v1/system/...` |
| `ControlPlane:ActivationTimeout` | Optional | `00:10:00` | From the start of an onboarding until the tenant is active; then the onboarding needs attention |
| `ControlPlane:InvitationEmailTimeout` | Optional | `02:00:00` | From the tenant's activation until the invitation email is sent. Must be longer than the Notifications retry delays together |
| `ControlPlane:CancellationTimeout` | Optional | `00:10:00` | From the start of a cancellation until the tenant is cancelled; then the onboarding needs attention |
| `ControlPlane:Clerk:SecretKey` | Required | none | The identity provider's Backend API secret key |
| `ControlPlane:Clerk:BackendApiUrl` | Optional | `https://api.clerk.com/v1/` | The identity provider's Backend API URL, ending in `/` |
| `ControlPlane:Clerk:Timeout` | Optional | `00:00:10` | How long a Backend API call may take |
| `ControlPlane:Invitations:AcceptUrl` | Required | none | The frontend page that accepts an invitation. The invitation code is added as the `code` parameter |
| `ControlPlane:Invitations:Lifetime` | Optional | `7.00:00:00` | How long an invitation can be accepted |
| `Notifications:InvitationEmailRetryDelays` | Optional | `00:00:10`, `00:01:00`, `00:05:00`, `00:15:00` | The pauses before each new try of the invitation email. Each is positive and longer than the one before |
| `Notifications:Resend:ApiKey` | Required outside Development. **Development:** may be empty | none | Resend's API key. In Development without it, or without `From`, email is written to the log. With both set, email is really sent, in Development too |
| `Notifications:Resend:From` | Required outside Development. **Development:** may be empty | none | The sender, such as `App <no-reply@example.com>`, on a domain verified at Resend |
| `Notifications:Resend:ApiUrl` | Optional | `https://api.resend.com/` | Resend's API URL |
| `Notifications:Resend:Timeout` | Optional | `00:00:10` | How long a send may take |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Optional | none | The OTLP collector's URL for logs, metrics and traces. Without it no telemetry is sent |
| `Serilog:MinimumLevel:Default` | Optional | `Information` (`appsettings.json`) | The lowest level written to the log |
| `Serilog:MinimumLevel:Override:<category>` | Optional | `Warning` for `Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore`, `Wolverine` and `JasperFx` (`appsettings.json`) | The lowest level for one log category. Each handled message is logged at `Information` under the message type's name |
| `AllowedHosts` | Optional | `*` (`appsettings.json`) | The host names the API answers to |
| `ASPNETCORE_ENVIRONMENT` | Optional | `Production` | The environment. `Development` relaxes the settings marked above |
| `ASPNETCORE_URLS` | Optional | `http://localhost:5000` | The address and port the process listens on. Each process on one machine needs its own port |

## What Development relaxes

- `Authentication:Clerk:AuthorizedParties` may be empty.
- `Notifications:Resend:ApiKey` and `Notifications:Resend:From` may be empty; email then goes to the log.
- The web process serves the OpenAPI document (`/openapi/v1.json`) and Scalar (`/scalar`). Outside Development both answer 404.

Development does not relax the token's signature, issuer and lifetime, the second factor for the system door, or the check of the database role (R10).
