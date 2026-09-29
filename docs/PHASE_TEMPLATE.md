# Phase N — <Phase Name> Plan

Status: **Draft — not approved**
Depends on: <prior phase(s), e.g. "Phase 1 verified complete">

> Copy this file to `docs/PHASE_N_PLAN.md`, fill it in, and stop for user
> approval before any implementation begins. See `docs/AI_WORKFLOW.md` §8–9
> for how phase plans are produced and why their existence does not
> authorize implementation.

## Objective

<One paragraph: what this phase exists to achieve, and why it follows the
previous phase. Do not restate product scope here — reference
`docs/PRODUCT.md` sections instead of duplicating them.>

## Scope

<What this phase actually implements. Be specific — name the modules,
features, or foundation pieces in scope.>

## Out of Scope

<Explicitly excluded items, especially anything that might be assumed
in-scope by proximity. Reference `docs/PRODUCT.md` §31 (Backlog / MVP-out)
where relevant, and name any backlog item someone might otherwise assume is
included.>

## Dependencies

<What must already be true before this phase can start — completed prior
phases, accepted ADRs, resolved product decisions. If a dependency is not
yet satisfied, this phase cannot begin regardless of user approval timing.>

## Unresolved Product Decisions Blocking This Phase

<List by ID (e.g. PD-07) from the relevant decision register, and state
exactly which milestone each one blocks. A milestone that depends on an
unresolved decision does not start until the user resolves it — it is not
inferred or assumed. If none block this phase, say so explicitly rather than
leaving the section silently empty.>

## Milestones

| No | Milestone | Dependency | Owning specialist(s) | Completion criterion |
| ---: | --- | --- | --- | --- |
| 1 | | | | |

<Each completion criterion must be checkable, not a feeling — "build passes
and X integration test is green," not "looks correct." Milestones should be
small enough to verify independently.>

## Milestone Dependency Graph

<Either a short dependency list per milestone (as in the table above) or a
text diagram if the graph is non-trivial. Call out which milestones may run
in parallel and which must serialize because they touch the same file or
contract (see `docs/AI_WORKFLOW.md` §5).>

## Expected Specialist Roles

<Which roles from `docs/AI_WORKFLOW.md` §2 this phase actually needs. Not
every phase needs every specialist — name only the ones with real work.>

## Verification Criteria

<Per-milestone and phase-wide: what must be run and pass. Name concrete
commands/suites where known (build, unit tests, architecture tests,
integration tests, security tests, a11y/E2E scenarios). If a milestone
requires infrastructure evidence (e.g. a real container runtime, a real
PostgreSQL instance) rather than config inspection, say so explicitly.>

## Phase Completion Criteria

<What must be true, all at once, for this phase to be reported complete to
the user. Should mirror `docs/AI_WORKFLOW.md` §7: completion checks pass,
independent Reviewer (and Security, when relevant) sign-off obtained,
implementation compared against PRODUCT/ARCHITECTURE/ADRs/UX_FLOWS/this
plan, no unresolved Critical/High findings, remaining Medium findings have
an owner and disposition.>

## Handoff Requirements

<What must be written into `docs/AI_HANDOFF.md` at the end of this phase so
any AI provider can resume: current phase status, completed/remaining
milestones, blockers, unresolved product decisions, known technical debt,
last verification summary (actual build/test results, not a claim), next
recommended action. See `docs/AI_WORKFLOW.md` §11.>

## Next-Phase Planning Gate

This phase's completion does not authorize starting the next phase. Once
this phase is independently verified complete (`docs/AI_WORKFLOW.md` §7),
the orchestrator may prepare — but not begin implementing — the next phase
plan, following `docs/AI_WORKFLOW.md` §8. Implementation of the next phase
requires explicit user approval.
