# 0029. Invite-only user creation

- Status: Proposed
- Date: 2026-10-04

## Context

Sign-up is closed (0028). People join a tenant only when invited, and the invitation is a credential that must be protected (OWASP authentication and session management guidance).

## Decision

1. An authorized member or a system admin creates an invitation. The catalog stores the email, tenant, role, a hash of the token (never the token itself), an expiry and a status. An invitation is single-use.
2. If the person has no Clerk account, the API also creates a Clerk invitation through Clerk's backend API, with our invitation id in its metadata.
3. The person signs up or signs in through the link and accepts the invitation.
4. The API validates the invitation. If the person is not yet in the catalog it creates the user record, and adds the membership in the same transaction.

- The catalog user record is created only when an invitation is accepted; no Clerk webhook is needed.
- A user record alone grants nothing; access comes from membership (0030).
- Resend is preferred for the invitation email for a consistent look (see To verify).

## Alternatives considered

- **Create users from a Clerk webhook.** Another integration to secure and keep idempotent, with no need for it.
- **Store invitation tokens in plain text.** A database leak would expose usable invitations.

## Consequences

- Every user enters through a known invitation.
- Expired invitations are closed by a system job (0027).

## To verify

- Whether Clerk or our own Resend channel sends the invitation email (Phase 4).
