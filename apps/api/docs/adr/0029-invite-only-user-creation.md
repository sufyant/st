# 0029. Invite-only user creation

- Status: Proposed
- Date: 2026-10-04

## Context

Sign-up is closed (0028). People join a tenant only when invited, and the invitation is a credential that must be protected (OWASP authentication and session management guidance).

## Decision

1. An authorized member or a system admin creates an invitation. The catalog stores the email, tenant, role, a hash of the token (never the token itself), an expiry and a status. An invitation is single-use.
2. If the person has no Clerk account, the API also creates a Clerk invitation through Clerk's backend API, with our invitation id in its metadata.
3. The person signs up or signs in through the link and accepts the invitation.
4. The API validates the invitation: the token must be valid, and the verified email of the Clerk user must match the invitation's email. The token alone is not enough, so a forwarded link does not let someone else in. If the person is not yet in the catalog it creates the user record, and adds the membership in the same transaction.

- The catalog user record is created only when an invitation is accepted; no Clerk webhook is needed.
- A user record alone grants nothing; access comes from membership (0030).
- We send the invitation email ourselves, through Resend, for a consistent look (see Verified).

### How it works

- **The record.** `catalog.invitations` holds:
  - The tenant, the email address and the role.
  - The SHA-256 hash of the token. The token is 256 random bits, so a fast hash suffices. The hash is written when the invitation is delivered and stays empty until then.
  - Who invited, when, the expiry, and the status: `Pending`, `Accepted` or `Expired`.
  - The lifetime comes from `Invitations:Lifetime` (seven days by default). Until the system job closes expired invitations (0027), the expiry date decides.
- **Who invites.** A member with `members.invite` may invite with a role no greater than their own (0030):
  - Route: `POST /v1/tenants/{slug}/invitations`.
  - A system admin invites through `POST /v1/admin/tenants/{slug}/invitations` with `system.members.invite`, with any role; this is how a tenant gets its first owner (0031).
- **The link.** The accept link is `Invitations:AcceptUrl` with the token as its `token` parameter.
  - **No Clerk account.** The API asks Clerk's Backend API for an invitation:
    - `notify: false`, so Clerk sends no email.
    - `ignore_existing: true`, so the person can be invited again while an earlier Clerk invitation is pending.
    - Our invitation id in `public_metadata`.
    - The accept link as `redirect_url`.
    - We send the invitation `url` Clerk returns. It signs the person up and lands them on the accept link.
  - **A Clerk account exists.** We send the accept link itself.
  - **How "has an account" is decided.** By listing Clerk users with that email address and matching the address exactly, because some of Clerk's email filters match partially.
- **Accepting.** A signed-in user calls `POST /v1/invitations/accept` with the token. The route is outside any tenant.
  1. The token leads to the invitation's tenant. This is the second catalog reader that is not bound to a tenant (0021), and it reveals only the tenant id.
  2. The user's verified email addresses are read from Clerk, before any transaction begins.
  3. The acceptance then runs as one command for that tenant, in its transaction (0016), with those addresses:
     - The invitation row is locked (`FOR UPDATE`).
     - The invitation must be pending and unexpired, and one of the user's verified email addresses at Clerk must match (case-insensitive).
     - The user is created if new, the membership is added, and the invitation is marked accepted.
     - Two concurrent attempts cannot both use one token.
- **Failures.**

  | Case | Response |
  | --- | --- |
  | Unknown token | 404 `invitation.not_found` |
  | Already used | 409 `invitation.not_pending` |
  | Expired | 409 `invitation.expired` |
  | Email does not match | 403 `invitation.email_mismatch` |
  | Already a member | 409 `membership.exists` |

- **Delivery.** Creating an invitation saves the record and, in the same transaction, sends a `DeliverInvitation` message through the outbox (0024). Nothing in the command calls out of the process. The delivery runs after the commit, in the invitation's tenant:
  1. It locks the invitation. One that already has a token was delivered, so a delivery that arrives again does nothing.
  2. It generates the token and writes the token's hash to the invitation.
  3. It asks Clerk whether the person has an account, and creates the Clerk invitation if not.
  4. It sends the email last. If sending fails, the hash rolls back with the delivery's transaction. The delivery is tried again after pauses of one, two and four seconds, each time with a new token, and then goes to the dead letter queue; it is not compensated (0026).
  - The token is born in the delivery, so no stored message ever carries it; only the link that is sent does. A test checks the stored messages for it.
  - An email sent by a delivery whose transaction then fails to commit carries a link that does not work; the retry sends a new one.
- **Delivery until Notifications exists.** The email goes through an `IInvitationSender` port. Until the Notifications module sends it through Resend (Phase 6, 0037), the sender depends on the environment:
  - In Development it writes the link to the log.
  - Anywhere else, sending fails with an explicit error and the delivery ends in the dead letter queue. The invitation is kept, because its delivery comes after its transaction, but it has no token, so nobody can accept it. Before Phase 5 the command itself failed and nothing was kept.
- **Clerk calls and open transactions.** The acceptance reads the verified addresses before its transaction begins, so it holds no lock while it waits on Clerk. The delivery deliberately keeps its transaction open while it calls Clerk and sends the email:
  - **Why.** The token must be born in the step that sends it, and its hash may be kept only once the link has gone out. Committing the hash first would keep a token nobody received whenever sending fails. Handing the token to a later step would put it in a stored message.
  - **What it costs.** For the length of the calls, the delivery holds one pooled connection and a lock on its own invitation row, nothing else. The calls are bounded by their clients' timeouts, and resilience policies come with 0041. The lock also keeps a second copy of the message from issuing a second token at the same time.
  - **What can go wrong.** An email sent by a delivery whose transaction then fails to commit carries a link that does not work; the retry sends a new one.

## Alternatives considered

- **Create users from a Clerk webhook.** Another integration to secure and keep idempotent, with no need for it.
- **Store invitation tokens in plain text.** A database leak would expose usable invitations.
- **Accept on the token alone.** Whoever holds a forwarded or leaked link would join the tenant.
- **Let Clerk send the email to people without an account.** People with and without an account would get different-looking emails, and the people with an account would still need ours.
- **Leave our token out of Clerk's redirect URL.** The person who signs up through Clerk would then arrive without the token and would need a second link.

## Consequences

- Every user enters through a known invitation.
- Someone who wants to join with a different email address needs a new invitation.
- Expired invitations are closed by a system job (0027).
- Our token appears in plain text in the redirect URL stored at Clerk. A leak there gives no access on its own, because accepting also needs the verified email address.
- Clerk limits invitation creation to 100 per hour per instance.
- The token never sits in a stored message: it is generated when the invitation is delivered (Phase 5).
- An invitation is visible as pending before its email is sent, and outside Development it stays without a token until the Notifications module sends emails (Phase 6).

## Verified

- Phase 4: Clerk's `POST /v1/invitations` sends no email when `notify` is `false` and returns the invitation's `url`. So our own channel (Resend) sends every invitation email. A person without an account signs up through that `url`, because sign-up is invite-only (0028).
- Phase 4: Clerk's session token does not carry the email address. The verified addresses are read from `GET /v1/users/{id}`: those whose `verification.status` is `verified`.
