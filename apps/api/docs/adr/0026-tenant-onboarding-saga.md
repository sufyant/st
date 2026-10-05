# 0026. Tenant onboarding saga

- Status: Proposed
- Date: 2026-10-04

## Context

Creating a tenant spans several steps (tenant record, first owner invitation, activation) that must either complete or leave a clear state. The template is invite-only, but self-serve sign-up may come later.

## Decision

- An orchestrated saga in ControlPlane (0025), started by a system admin.
- Flow: create the tenant as `provisioning`, create the invitation record for the first owner, set the tenant to `active`, and send the invitation email last.
- If a step fails, compensation sets the tenant to `failed`.
- The invitation email is the last step and is not compensated.
- The saga does not depend on who triggers it; self-serve sign-up can later trigger the same saga from a public endpoint.

### How it works

- **Start.** A system admin with `system.tenants.create` calls `POST /v1/admin/tenants` with the slug and the first owner's email (0031). The endpoint chooses the new tenant's id on the server, and the whole onboarding runs inside that tenant: every step runs in its tenant transaction (0016), and the tenant travels with each message (0017).
- **Steps.**
  1. `StartTenantOnboarding`, inside the request: the tenant is created as `provisioning`, and the answer is the tenant with its status. An invalid slug is 400; a slug another tenant has is 409 `tenant.slug_taken`, also when two onboardings race for it, because the slug's unique index decides.
  2. `CreateFirstOwnerInvitation`: the invitation record for the first owner, with the built-in Owner role and no token yet (0029).
  3. `ActivateTenant`: the tenant becomes `active`, and `TenantActivated` is published for other modules (0009).
  4. `DeliverInvitation`: Clerk and the email, last; not compensated (0029).
- **State.** The tenant's status is the saga's state (0025). A step that finds the tenant no longer provisioning does nothing.
- **Compensation.** Steps 2 and 3 are retried three times; then the step goes to the dead letter queue, and its fault leaves the tenant `failed` (0025). If the first owner's invitation was created, it has no token, so nobody can accept it.

## Alternatives considered

- **One transaction for all steps.** The invitation reaches external systems (Clerk, email) that cannot join a database transaction.

## Consequences

- A tenant is visibly `provisioning`, `active` or `failed`.
- A failure while sending the email does not roll the tenant back; the tenant stays `active` with its invitation record, which has no token until a delivery succeeds.
