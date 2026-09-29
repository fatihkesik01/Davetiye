# AI Workflow — Provider-Neutral Delivery Protocol

Status: **Accepted baseline**
Last updated: **2026-09-28**

This document is the single, provider-neutral description of how AI coding
agents collaborate on Davetiye — regardless of whether the acting tool is
Codex, Claude Code, Cursor, or a future provider. It does not define product
behavior (`docs/PRODUCT.md`), technical constraints (`docs/ARCHITECTURE.md`),
or the current point-in-time state of the work (`docs/AI_HANDOFF.md`). It
defines the *process*.

Any AI agent — orchestrator or specialist, on any provider — must follow this
document. `AGENTS.md` points here instead of restating the protocol; provider
adapters (`.claude/agents/`, `.cursor/agents/`, `.codex/agents/`) point here
too, through `docs/agents/<name>.md` (see "Provider Adapter Principle" below).

## 1. The Single Most Important Rule

**A new agent, orchestrator, or session must never trust a previous agent's
claim that work is "done."**

Before relying on any prior status — including this repository's own
`docs/AI_HANDOFF.md`, a phase execution record, or a chat summary — verify it
independently against:

- actual repository state (`git status`, `git log`, `git diff`);
- the source-of-truth docs (`docs/PRODUCT.md`, `docs/ARCHITECTURE.md`, ADRs,
  the current phase plan);
- real build and test results (run the build/test commands yourself; do not
  assume a green status from a document);
- the actual implementation on disk, not just its plan or execution record.

Documents can drift from reality (a phase execution record can under- or
over-state what actually exists in `src/`). When a document and the repo
disagree, the repo wins, and the discrepancy is reported, not silently
resolved.

## 2. Roles

### Orchestrator

Delivery lead. Coordinates specialists, verification, and phase transitions.
Not the default implementer. Responsible for:

- reading the current phase's source-of-truth docs before delegating;
- decomposing an approved phase into small, dependency-aware, verifiable
  milestones;
- selecting only the specialists a milestone actually needs;
- giving each specialist an objective, scope, source-of-truth references,
  constraints, expected output and verification criteria;
- parallelizing only genuinely independent work (see §5);
- routing verification findings back to the owning specialist and requiring
  re-verification, never accepting "should be fixed now" without re-checking;
- keeping work inside the currently approved phase;
- escalating unresolved product decisions to the user instead of deciding
  them;
- updating `docs/AI_HANDOFF.md` at milestone and phase boundaries so the next
  session (any provider) can pick up without re-deriving context from scratch.

### Specialists

| Role | Owns |
| --- | --- |
| Architect | Architecture, module boundaries, cross-cutting technical decisions, ADRs |
| Backend | ASP.NET Core APIs, application/domain logic, integrations |
| Frontend | React implementation, client-side behavior |
| Database | PostgreSQL schema, migrations, constraints, indexes, data integrity |
| UI/UX | Flows, usability, responsive behavior, accessibility guidance |
| Security | AuthN/AuthZ, abuse cases, trust boundaries, security review |
| Tester | Automated tests, integration tests, regression verification, failure reproduction |
| Reviewer | Independent review for correctness, maintainability, architecture and scope compliance |

Each specialist's detailed system prompt lives in `docs/agents/<name>.md` (see
§8). This table is the stable, at-a-glance summary; the detail belongs in
those files, not duplicated here.

## 3. Product Decisions vs. Technical Decisions

- **Product decisions** belong to the user and `docs/PRODUCT.md`. Examples:
  pricing, package limits, user-visible behavior, retention behavior,
  permissions visible to users, workflow semantics, MVP scope. No agent may
  invent one to unblock work — stop and ask.
- **Technical decisions** that do not change product behavior (e.g. how a
  module is internally structured, which test harness to use) may be
  resolved by the Architect and the relevant specialist without user
  escalation.
- An **open product decision** recorded in a decision register (e.g.
  `docs/PHASE_0_BASELINE.md` §12, PD-01…PD-13) is not permission to invent
  behavior. Foundation work must not close it implicitly by choosing an
  implementation that only makes sense under one of the possible answers.

When specialist recommendations conflict, or a document appears to
contradict another, resolve using this priority order:

1. `docs/PRODUCT.md`
2. product decisions the user has explicitly accepted
3. accepted ADRs under `docs/adr/`
4. `docs/ARCHITECTURE.md`
5. the current phase baseline and approved phase plan
6. specialist technical recommendations

If the conflict cannot be resolved without changing product behavior, stop
and ask the user — do not pick a side of the priority order to paper over a
real product ambiguity.

## 4. Milestone Decomposition

- Break an approved phase into milestones small enough to verify
  independently (their own build/tests/review), not so small that
  coordination overhead dominates.
- Each milestone states: objective, dependencies, owning specialist(s), and
  an explicit, checkable completion criterion (not "looks done" — a build
  passes, a test suite is green, a documented contract exists).
- Milestones are dependency-aware: a milestone that needs another milestone's
  output declares that dependency explicitly rather than assuming ordering.

## 5. Safe Parallelism

- Parallelize only work that is genuinely independent — different modules,
  different files, no shared contract still being negotiated.
- Never let two agents concurrently edit the same file or tightly coupled
  area. When two milestones must touch the same file (e.g. a shared
  `DbContext`, a shared config file, a shared `package.json`), serialize
  them through a single named owner for that file, even if the milestones
  themselves run in parallel otherwise.
- A milestone that changes a cross-cutting contract (API shape, DB schema,
  shared module boundary) blocks dependents until it is merged and verified,
  not just "in progress."

## 6. Implement → Test → Security → Review → Fix → Re-verify Loop

For every milestone:

```
Plan
  → select specialists
  → implement
  → test
  → security review (when the milestone touches auth, data exposure,
    payments, uploads, or another trust boundary)
  → independent review
  → fix findings (routed back to the owning specialist)
  → re-test / re-review (never skipped because a fix "should" work)
  → milestone completion report
```

An implementer's own statement that a milestone is complete is not
sufficient. Completion requires the milestone's stated verification criteria
to actually pass, checked by someone other than the implementer where a
Tester/Security/Reviewer role is in scope for that milestone.

## 7. Phase Completion Criteria

A phase is not complete because its milestones are individually marked done.
Before declaring a phase complete:

1. Run the phase's defined completion checks (build, tests, architecture
   tests, security tests, and any infrastructure evidence the phase plan
   requires — e.g. a real container runtime run, not just config inspection).
2. Have Reviewer perform an independent phase-level review.
3. Have Security perform a phase-level review when security-relevant.
4. Compare the actual implementation against `docs/PRODUCT.md`,
   `docs/ARCHITECTURE.md`, accepted ADRs, `docs/THREAT_MODEL.md`,
   `docs/UX_FLOWS.md`, and the phase plan itself.
5. Identify unresolved blockers, regressions, scope deviations, or technical
   debt, and record them (severity, owner, disposition).
6. Report — to the user — whether the phase's completion criteria are
   actually satisfied, including any Medium+ findings still open.

A phase cannot close while an unresolved Critical or High finding is open.

## 8. Next-Phase Planning

- Only after the current phase is verified complete does the orchestrator
  prepare the next phase's plan.
- The next plan is derived from `docs/PRODUCT.md`, accepted architecture and
  ADRs, the project roadmap, what was *actually* implemented (not what was
  planned), remaining dependencies, unresolved product decisions, and review
  findings/technical debt from the closed phase.
- Use `docs/PHASE_TEMPLATE.md` as the starting structure for the new phase
  plan document (e.g. `docs/PHASE_2_PLAN.md`).
- **Preparing the next phase plan does not authorize implementing it.**

## 9. User Approval Gate

- The existence of a phase plan document is never, by itself, authorization
  to implement that phase.
- Only the phase the user has explicitly approved may be implemented.
- When a milestone or phase appears complete, the orchestrator reports this
  to the user and stops; it does not automatically begin the next phase.
- Any implementation-affecting product decision that is unresolved (a
  decision-register entry, an open ADR question) must be escalated to the
  user before the blocked work begins — never assumed.

## 10. Git / Commit Approach

- Prefer small, coherent commits and milestones over large, unrelated
  changes.
- Before recommending a commit: inspect the actual diff, confirm the
  relevant build/tests pass, confirm required Security/Reviewer checks are
  complete, and summarize the resulting change set clearly.
- Do not create a "final" milestone or phase commit merely because
  implementation looks complete — the verification in §6–§7 must have
  actually run.
- Never bypass hooks or force-push as a shortcut past a failing check;
  fix the underlying issue.

## 11. Handoff Protocol Between Agents / Providers / Sessions

When a new AI agent (same provider, new session, or an entirely different
provider) picks up this repository:

1. Read `AGENTS.md` first — it names the reading order and points here.
2. Read this document (`docs/AI_WORKFLOW.md`) for the protocol.
3. Read `docs/AI_HANDOFF.md` for the claimed current state — approved phase,
   status, active/completed/remaining milestones, blockers, unresolved
   product decisions, known technical debt, last verification summary, next
   recommended action.
4. **Independently verify step 3** per §1 before acting on it: check
   `git log`/`git status`/`git diff`, re-run the build and test suites, and
   compare what actually exists in `src/`, `tests/`, `docs/adr/` and the
   phase plan against what `docs/AI_HANDOFF.md` claims.
5. If verification contradicts `docs/AI_HANDOFF.md` (e.g. it says a
   milestone is "waiting" but the code already implements it, or says
   "complete" but tests fail), report the discrepancy to the user and
   correct `docs/AI_HANDOFF.md` to match reality — do not silently trust
   either the stale doc or your own assumption.
6. Only then resume work, staying inside the currently approved phase (§9).

`docs/AI_HANDOFF.md` is a living snapshot, not a history log. Permanent
history belongs in Git and in dated phase documents (`docs/PHASE_1_PLAN.md`,
`docs/PHASE_1_EXECUTION.md`, future phase plans) — do not let the handoff
file grow into a chronological journal.

## 12. Provider Adapter Principle

- Provider-specific configuration (a Codex `.toml` agent file, a Claude Code
  or Cursor `.md` subagent file, or any future provider's equivalent) is
  **only an adapter**. It exists to satisfy that tool's discovery mechanism.
- The actual product, workflow and phase-state source of truth always lives
  under `docs/` (`docs/PRODUCT.md`, `docs/ARCHITECTURE.md`, this document,
  `docs/AI_HANDOFF.md`, `docs/agents/<name>.md`) — never inside a
  provider-specific config directory.
- Provider-specific agent files are not themselves a source of truth. In
  this repository they are generated: `docs/agents/<name>.md` is the single
  canonical per-agent instruction file, and `scripts/sync-agents.ps1`
  produces `.claude/agents/<name>.md`, `.cursor/agents/<name>.md` (verbatim
  copies — both tools share the same Markdown+frontmatter subagent format)
  and `.codex/agents/<name>.toml` + `.codex/config.toml` (a generated TOML
  wrapper, because Codex expects TOML). Never hand-edit the generated files;
  edit `docs/agents/<name>.md` and rerun the script.
- A new, not-yet-integrated provider should not have speculative config
  files fabricated for it ahead of time. When that provider is actually
  being used, its adapter should be added the same way: a thin file that
  points at `docs/` and, ideally, is generated from `docs/agents/<name>.md`
  rather than hand-duplicated.
- Regardless of adapter, every provider's agent must still perform the
  handoff verification in §11 before acting — the adapter format changes,
  the obligation to verify repository state does not.

## 13. Status Reporting: Honesty, Progress, and Agent Attribution

Three obligations apply to every status report the Orchestrator gives —
milestone report, phase report, or any progress update to the user. These
extend §1 from "don't trust another agent's done claim" to "don't let your
own report to the user be unfounded."

### 13.1 No fabricated status

The Orchestrator must never state a completion percentage, milestone
status, or finding count it has not derived from an actual, checkable
source: the milestone table, real build/test output, or actual
Reviewer/Security pass results. A number that is an estimate rather than a
direct count (e.g. "roughly half the remaining milestones look similar in
size") must be explicitly labeled as an estimate, never presented as a
measured fact.

### 13.2 Progress percentage

Every milestone or phase status report must include a concrete completion
ratio derived from the phase's milestone table (e.g. "10/17 milestone
units complete = ~59%"), not a qualitative impression alone ("mostly
done"). This ratio is kept current in `docs/AI_HANDOFF.md`'s "Current
Phase Status" section at each milestone/phase boundary.

### 13.3 Agent attribution

For each milestone, the Orchestrator records: which specialist(s) owned
implementation, which specialist(s) performed the Reviewer/Security
passes, and the resulting finding counts by severity (Critical/High/
Medium/Low raised and fixed). This is recorded as columns/notes on the
current phase's execution document's milestone table (e.g.
`docs/PHASE_1_EXECUTION.md` §3).

This attribution is a **record of responsibility**, not a measured
workload split. It must never be presented to the user as a precise
effort-share percentage ("Backend did 40% of the work") — no tool in this
workflow measures actual effort or time spent. It answers "who owned this
milestone and what did independent review find," not "how much did each
agent contribute." If a milestone's implementer is not explicitly named in
the execution record, the Orchestrator records "not recorded" rather than
inferring or guessing an owner after the fact.
