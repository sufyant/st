# 0045. Template scope and deferred topics

- Status: Proposed
- Date: 2026-10-04

## Context

Building capabilities before a real need appears adds complexity (Hunt and Thomas; Ousterhout). Some topics do not change the code skeleton and can be decided later.

## Decision

The following are out of the template for now:

- Billing and subscriptions
- Self-serve sign-up (the onboarding saga is ready for it, 0026)
- Slug changes and redirects from old URLs
- A real push provider
- A tenant settings model
- Client type generation and the internals of clients; the system admin console UI
- CI/CD and Kubernetes deployment
- Secret management (principle: configuration is read from the environment)
- The tool for viewing observability data (Grafana stack or Seq)
- File and media management (principle: object storage, separated per tenant)
- Email templates
- Search

## Alternatives considered

- **Build them now.** Speculative work that would likely be wrong for some products.

## Consequences

- Each item gets its own ADR when it is taken up.
