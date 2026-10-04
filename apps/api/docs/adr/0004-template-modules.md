# 0004. Template modules

- Status: Proposed
- Date: 2026-10-04

## Context

Every SaaS product needs tenants, users, access control, notifications and an audit trail; few need the same billing. The template should contain what every product needs and nothing more. Golding (*Building Multi-Tenant SaaS Architectures*) and AWS SaaS Architecture Fundamentals separate the control plane, which manages tenants, from the application plane.

## Decision

The template ships these modules:

| Module | Responsibility | Schema | RLS |
| --- | --- | --- | --- |
| ControlPlane | Tenants, users, memberships, roles and permissions, invitations, system admins, onboarding saga | `catalog` | No |
| Notifications | Notification channels, user-defined scheduled notifications | its own | Yes |
| Audit | Audit log | its own | Yes |
| SharedKernel | Dependency-free common ground: entity base, `ITenantEntity`, Result and error types | none | none |

- Billing and subscriptions are not part of the template; a product that needs them adds a separate module.
- SharedKernel stays small and holds no business rules. Contracts answer "what does a module offer others"; SharedKernel is common ground owned by no module.

## Alternatives considered

- **Include billing.** Billing models differ per product; a generic one would be wrong for most.
- **A business-language name instead of ControlPlane.** DDD asks for names from the business. ControlPlane is a technical term, but it is the established SaaS term (Golding), so it was kept.

## Consequences

- Accepted tension: the ControlPlane name comes from SaaS architecture vocabulary, not from the business domain.
- The schema names for Notifications and Audit are chosen when those modules are built.
