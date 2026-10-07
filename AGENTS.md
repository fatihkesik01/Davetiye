# Davetiye — Agent Instructions

## Product Source of Truth

Before planning, designing, or implementing any feature, read:

- `docs/PRODUCT.md`

`docs/PRODUCT.md` defines the current product scope and requirements.

Before making technical architecture or integration decisions, also read:

- `docs/ARCHITECTURE.md`

`docs/ARCHITECTURE.md` records accepted MVP technical constraints and
provider choices. It does not override `docs/PRODUCT.md`.

Before starting implementation, also read:

- `docs/ROADMAP.md`
- `docs/PHASE_0_PLAN.md`
- `docs/THREAT_MODEL.md`
- `docs/UX_FLOWS.md`
- the plan document for the currently approved phase
- relevant accepted ADRs under `docs/adr/`
- `docs/AI_WORKFLOW.md`
- `docs/AI_HANDOFF.md`

**This full list is for the Orchestrator at the start of a phase or a new
session** (establishing orientation and the current point-in-time state),
**not a blanket requirement repeated by every specialist for every task.**
A specialist delegated to mid-session follows its own `docs/agents/<name>.md`
reading list plus whatever specific excerpt the Orchestrator hands it
(`docs/AI_WORKFLOW.md` §2) — it does not re-read this entire list from
scratch for a routine milestone.

Do not assume that the existence of a phase plan authorizes implementation of that phase.
Only the phase explicitly approved by the user may be implemented.

Open product decisions in the Phase 0 decision register or later phase plans are not permission to invent behavior.

Accepted ADRs are under `docs/adr/`. Open product decisions in the Phase
0 decision register are not permission to invent behavior.

Do not invent product features that are not described there.
If a product decision is unclear or missing, ask the user instead of making a major assumption.

`docs/ROADMAP.md` is the long-term delivery map from the verified current
state through production/business launch. It provides phase sequencing and
high-level status, but never authorizes implementation by itself.

`docs/AI_WORKFLOW.md` is the common, provider-neutral workflow
source-of-truth — it defines the orchestration protocol referenced below in
full, regardless of which AI tool (Codex, Claude Code, Cursor, or another)
is acting. `docs/AI_HANDOFF.md` shows the current point-in-time work state
(approved phase, active/completed/remaining milestones, blockers). On a new
session or a switch to a different AI provider, the handoff state must be
independently verified against the actual repository (`git log`/`git
status`, real build/test runs, actual file contents) before being trusted —
see `docs/AI_WORKFLOW.md` §1 and §11. As stated above, the existence of a
phase plan is never itself authorization to implement it; only the phase
the user has explicitly approved may be implemented.

## VPS / Deployment

Before connecting to, deploying to, or deleting anything on the VPS, read:

- `docs/DEPLOYMENT.md`

It has the SSH connection command, what else runs on the shared VPS (Lora — never touch it), deploy steps, and safe reset/removal steps. Do not ask the user for VPS connection details — they are in that file.

## Agent Definitions Are Vendor-Neutral

The specialist/orchestrator prompts described in this file are implemented once,
under `docs/agents/<name>.md` (Markdown + YAML frontmatter). That is the only
place to hand-edit an agent's instructions.

- `.claude/agents/*.md` and `.cursor/agents/*.md` are verbatim copies (Claude
  Code and Cursor read the same subagent file format natively).
- `.codex/agents/*.toml` and `.codex/config.toml` are generated wrappers
  around the same content, because Codex expects TOML instead of Markdown.

Do not edit files under `.claude/agents/`, `.cursor/agents/`, or
`.codex/agents/` (or `.codex/config.toml`'s agent table) directly — they are
overwritten by the sync script and any direct edit will be silently lost, or
will drift from the other vendors' copies.

After changing anything under `docs/agents/`, run:

```
powershell -File scripts/sync-agents.ps1
```

## Project Goal

Build a production-ready responsive digital invitation web application.

The accepted stack is recorded in `docs/PRODUCT.md` §28 ("Teknik Temel")
and `docs/ARCHITECTURE.md` — read those for the current, complete list
(frontend/backend/database/media/payment/email providers, hosting).
This file does not keep its own copy, to avoid it drifting out of sync.


## Development Principles

- Keep the architecture maintainable and modular.
- Prefer simple solutions over unnecessary abstractions.
- Do not introduce a microservice architecture unless explicitly requested.
- Never hardcode configurable business limits when they belong in plans or system settings.
- Security and authorization must be enforced on the backend.
- Public invitation identifiers must never grant Creator permissions.
- Never store authentication secrets or credentials in the repository.
- The application must be responsive on mobile, tablet, and desktop.
- Do not implement features listed as MVP-out/backlog unless explicitly requested.

## Working Rules

Before implementing a substantial feature:

1. Read the relevant product requirements.
2. Inspect the existing codebase.
3. Identify dependencies and affected areas.
4. Create or update the implementation plan when appropriate.
5. Implement the smallest coherent solution.
6. Run relevant tests/builds.
7. Review the resulting changes before declaring the task complete.

Do not rewrite unrelated working code.

Do not silently change established product decisions.

If implementation requires changing the product scope, stop and explain the issue first.

## Delivery Orchestration

Substantial implementation work should be coordinated by the Orchestrator agent.

The Orchestrator is the delivery lead for phase and milestone execution. Its
job is to coordinate specialist agents, verification, reviews, and phase
transitions. It is not the default feature implementer.

Expected workflow:

User
→ Orchestrator
→ Relevant Specialist Agents
→ Tester/Reviewer (combined quality pass; may still run separately when a
  milestone warrants it)
→ Security when relevant
→ Orchestrator
→ User

**The full protocol — orchestrator/specialist responsibilities, product vs.
technical decision authority, milestone decomposition, safe parallelism,
the implement→test→security→review→fix→reverify loop, phase completion
criteria, next-phase planning, the user approval gate, git/commit approach,
and the cross-provider handoff protocol — lives in `docs/AI_WORKFLOW.md`.**
That document applies identically no matter which AI tool is acting.

Two invariants are repeated here because they are the most consequential to
get wrong in this specific repository:

- **Only the phase the user has explicitly approved may be implemented.** A
  phase plan document's existence, or a phase execution record that claims
  progress, is never itself authorization.
- **Do not trust a prior agent's "done" claim** — including this
  repository's own `docs/AI_HANDOFF.md` — without independently verifying it
  against `git log`/`git status`, real build/test runs, and actual file
  contents (`docs/AI_WORKFLOW.md` §1).

Specialist roles are summarized in `docs/AI_WORKFLOW.md` §2; each role's
full instructions live in `docs/agents/<name>.md` (see "Agent Definitions
Are Vendor-Neutral" above). Not every task requires every specialist.

When preparing the next phase's plan, use `docs/PHASE_TEMPLATE.md` as the
starting structure, and update `docs/AI_HANDOFF.md` at milestone and phase
boundaries so any AI provider can resume without re-deriving context.
