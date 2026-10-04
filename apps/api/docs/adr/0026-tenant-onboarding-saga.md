# 0026. Tenant onboarding saga

- Status: Proposed
- Date: 2026-10-04

## Context

Creating a tenant spans several steps (tenant record, first owner invitation, activation) that must either complete or leave a clear state. The template is invite-only, but self-serve sign-up may come later.

## Decision

- An orchestrated saga in ControlPlane (0025), started by a system admin.
- Flow: create the tenant as `provisioning`, send an invitation to the first owner, set the tenant to `active`.
- If a step fails, compensation sets the tenant to `failed`.
- The invitation email is the last step and is not compensated.
- The saga does not depend on who triggers it; self-serve sign-up can later trigger the same saga from a public endpoint.

## Alternatives considered

- **One transaction for all steps.** The invitation reaches external systems (Clerk, email) that cannot join a database transaction.

## Consequences

- A tenant is visibly `provisioning`, `active` or `failed`.
- The overview lists the invitation before activation but also calls the invitation email the last step. One reading is that the invitation record is created before activation and the email is sent after it. This must be settled before Phase 5.
