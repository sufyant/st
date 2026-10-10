# Provider setup

What the API needs from the providers it runs on, and how to set them up. The architecture does not depend on these providers (fixed rule 6 of `ARCHITECTURE.md`); the notes for each one are here. `configuration.md` lists the settings, and `local-run.md` runs the API on a local machine.

## Database provider

The API needs PostgreSQL 18 and the two roles of R2 and R7: an owner role for the migrations and an application role for running code. `db/bootstrap.sql` creates both.

- `db/bootstrap.sql` runs once, before the first migration, against the API's own database, as a role that may create roles. Locally that is the superuser. On Neon it is a member of `neon_superuser`, such as the role Neon creates with the project; the application account is created with this SQL, not in the console.
- `ConnectionStrings:Messaging` is needed when `ConnectionStrings:Database` goes through a pooler in transaction mode, such as Neon's pooled endpoint. Wolverine's message store holds session-level advisory locks, so the setting then names a direct or session-mode connection of the application role.
- `ConnectionStrings:Migrations` names a direct connection of the owner role. Only the `migrate` command gets it (R3).

## Identity provider

The API needs these from an identity provider:

- OpenID Connect discovery (`/.well-known/openid-configuration`) and its signing keys, served over HTTPS with a certificate the server trusts. The issuer URL is `Authentication:Clerk:Issuer`.
- Session tokens that carry `sub` (the user) and `sid` (the session). A token without `sid` is refused (API2).
- `fva` in the session token for the second factor: the minutes since the first and the second factor were verified, `-1` for never. The system door requires a verified second factor (A6).
- Open sign-up with email verification at sign-up. The Owner of a new tenant signs up like any other user and then accepts the invitation with the invited address verified.
- A Backend API that returns a user's verified email addresses. The API calls it when a user accepts an invitation and when the first system admin enters the system door. Its URL and secret key are `ControlPlane:Clerk:BackendApiUrl` and `ControlPlane:Clerk:SecretKey`.

On Clerk:

- Use the session token. A JWT template's token carries no `sid`, so the API refuses it.
- Enable the second factor. The first system admin enrolls a device before their first request to `/v1/system/...`.
- Keep sign-up open and require email verification at sign-up.
- The Backend API call is `GET users/{user id}`; the API reads the addresses whose verification status is `verified`.

## Email and onboarding timing

- `ControlPlane:InvitationEmailTimeout` must be longer than the `Notifications:InvitationEmailRetryDelays` together. Otherwise the onboarding gives up on an email that is still being tried, cancels the invitation and raises the alarm.
- Outside Development the API does not start without `Notifications:Resend:ApiKey` and `Notifications:Resend:From`. The sender's domain must be verified at Resend.
