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

## Alternatives considered

- **Calling providers directly from business logic.** Every provider change touches the module.

## Consequences

- Adding a channel does not change the module's business logic.
