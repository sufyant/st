# 0043. Test writing conventions

- Status: Proposed
- Date: 2026-10-04

## Context

Tests are read far more often than written and must be deterministic (Meszaros, *xUnit Test Patterns*; Martin, *Clean Code*). These conventions are adapted from the company testing ADR (numbered ADR-0006 in the company's own records, not in this repository).

## Decision

- Arrange, Act, Assert, separated by blank lines, without label comments.
- No `if`, `switch` or loops in tests; no expected values computed with production logic.
- `[Fact]` for one scenario; `[Theory]` for explicit inputs of the same behaviour.
- Builders and factories return fresh, valid, deterministic objects; values that define the scenario stay visible in the test (DAMP over DRY).
- Time through `TimeProvider`; randomness and ids under control; no sleeps, no `.Result`, no `async void`.
- Flaky tests are fixed at the root, never hidden by retries.
- A bug fix adds a regression test that is first shown to fail.
- Names describe behaviour; test classes are grouped by behaviour area.

## Alternatives considered

- **No written conventions.** Test quality would vary by author.

## Consequences

- Tests read as specifications and do not flake on time or ordering.
