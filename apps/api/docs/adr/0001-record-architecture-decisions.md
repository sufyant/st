# 0001. Record architecture decisions

- Status: Proposed
- Date: 2026-10-04

## Context

The template is the starting point for every future product, so its decisions must be visible, reviewable and stable. They were first written as one overview in Turkish (`apps/api/docs/ARCHITECTURE.md`). An overview cannot carry a status per decision, and instruction files that copy its rules drift from it.

## Decision

- Every architectural decision is an Architecture Decision Record in `apps/api/docs/adr/`, one decision per record, numbered sequentially (`NNNN-short-title.md`).
- Each record has the sections Context, Decision, Alternatives considered and Consequences. Records that depend on something to be checked during setup carry a To verify section; the result is written back into that record.
- Status is Proposed, Accepted or Superseded by a later record. Agents propose records; only people accept them.
- ADRs are written in English and are authoritative. `ARCHITECTURE.md` remains the overview they were split from.
- Agent instruction files and code comments reference ADRs; they do not copy their rules.
- A changed decision is a new record that supersedes the old one.

## Alternatives considered

- **Keep only the overview document.** Simple, but decisions cannot be reviewed, accepted or superseded one at a time.
- **ADRs in a repository-root `docs/` folder.** The decisions concern the API, so they live with it under `apps/api/`.
- **A wiki outside the repository.** Not versioned with the code and not visible in review.

## Consequences

- Decisions can be reviewed and changed individually, with history in git.
- The overview and the records can drift; when they disagree, the record wins.
- Writing a record is part of any change that needs a decision no record covers.
