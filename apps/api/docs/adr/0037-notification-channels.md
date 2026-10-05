# 0037. Notification channel abstraction

- Status: Proposed
- Date: 2026-10-04

## Context

The Notifications module decides what to send; how it is delivered changes with clients and providers.

## Decision

The module says "send this notification" without knowing the channel. Channels are pluggable behind an interface:

- **In-app real time:** SignalR. The Redis backplane is enabled by configuration and off by default; it is turned on when running more than one pod. SSE can replace SignalR behind the same interface if needed.
- **Email:** a Resend adapter; in development and tests, a fake channel that writes email to the log.
- **Push:** an interface and a fake channel only, for now. The real provider is chosen when the client is known.

### How it works

- **Email.** Other modules send email through the module's synchronous contract, `INotificationsModule.SendEmailAsync` (0009). The email goes out during the call, so a caller can send what must never sit in a stored message, such as an invitation link (0029).
  - Behind it is the `IEmailChannel` port. Resend is used whenever `Resend:ApiKey` and `Resend:From` are set; in Development without them, the email is written to the log.
  - Outside Development a pod without those settings is not ready (0038), so no email is silently left unsent.
  - The Resend adapter calls Resend's API over HTTP (`POST /emails`), without Resend's SDK. Its client has the standard resilience handler (0041), and each send carries an `Idempotency-Key`, the same on every attempt, so Resend sends it once however often the request reaches it.
  - Emails are plain text; templates are out of scope (0045).
- **Notifications to a user** go through every registered `INotificationChannel`:
  - **In-app:** SignalR. The hub is `/v1/notifications/hub`, for signed-in users only; a user's connections are addressed by the identity provider's user id. A client receives `notification` messages with the notification's id, tenant, title and body.
  - **Push:** a channel that writes the notification's id and recipient to the log, until a provider is chosen (0045).
  - **The Redis backplane** is on when `ConnectionStrings:Redis` is set, and off by default. With it, a notification sent on one pod reaches the user's connections on any pod.
- **User-defined scheduled notifications** (0027) live in the module's `notifications` schema, under row level security.
  - A member schedules one for themselves with `notifications.schedule` (0030), under `/v1/tenants/{slug}/notifications/scheduled`: create, list their own (paginated, 0034), change and cancel until it is sent. Someone else's notification answers 404.
  - A minute's scanner job (0027) finds the due notifications of every tenant through a narrow `SECURITY DEFINER` lookup that returns only `(tenant_id, id)` (0017), and sends one `DispatchScheduledNotification` per notification, under its tenant.
  - The dispatch locks the notification, marks it sent and sends it through every channel before it commits. A second dispatch of the same notification, from a repeated scan or a message that arrives twice, finds it sent and does nothing. A channel that fails rolls the transaction back, and the dispatch is retried after one, two and four seconds; a notification can therefore reach a channel twice, never zero times.

## Alternatives considered

- **Calling providers directly from business logic.** Every provider change touches the module.
- **Resend's .NET SDK.** One endpoint is used; a typed HTTP client with the shared resilience handler is smaller than another dependency, and matches the Clerk adapter (0028).
- **Invitation email as an event through the outbox.** The link carries the invitation token, which must not sit in a stored message (0029).

## Consequences

- Adding a channel does not change the module's business logic.
