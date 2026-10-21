# Working with two agents

Claude and GPT/Codex both work on this repository. These rules stop them from editing the same files at once and make handoffs visible. William's explicit instructions take precedence over them. Messages go in the [inbox, issue #4](https://github.com/Willzy12h/Local_Device_Toolkit/issues/4); see [agent-comms/README.md](../../agent-comms/README.md).

## Branches

| Agent | Prefix | Example |
|---|---|---|
| Claude | `claude/` | `claude/unresolved-work-reasons` |
| GPT/Codex | `codex/` | `codex/engineer-readiness-design` |
| Either, by agreement | `fix/`, `docs/` | `docs/acceptance-matrix` |

- Branch from the current tip of the agreed base, and say in the PR which commit you branched from. Until `codex/engineer-readiness-design` merges, agree the base in the inbox, because `main` doesn't contain the full toolkit.
- Never commit directly to `main`.
- Never push to, rebase, amend or force-push the other agent's branch. Ask for changes in its PR or in the inbox.
- One change per branch.

## Pre-flight check before starting work

1. `git fetch origin` and confirm you are on the current tip of the base.
2. List open PRs and read the changed files of any that are open.
3. Read the inbox index, the active messages addressed to you, and [WORK-CLAIMS.md](WORK-CLAIMS.md).
4. If an open PR touches your files or feature area, stop. Name that PR in the inbox and wait, or pick different work.

## Claiming work

Open a **draft PR as your first act** (one scaffold commit with the plan is enough). The draft PR is the claim. Add a row to `WORK-CLAIMS.md` as the durable record, and link the claim in an inbox message.

**Feature areas:** engine/workbench · evidence and history · reporting and handover · silent diagnostics · settings and restoration · diagnostics and repairs · deployments · elevation/worker · Windows adapters and scripts · interface (WPF) · packaging and startup · build/CI · documentation.

- One agent holds an area at a time, until its PR merges.
- Cross-cutting changes (shared schemas, evidence record kinds, worker protocol, catalogue format) are raised first in the inbox and recorded in `docs/IMPLEMENTATION-DECISIONS.md`, before anyone builds on them.

## Merging and conflicts

- The first PR that is ready merges first. The second brings the new base in and resolves conflicts on its own branch.
- Resolve conflicts by deciding which behaviour is correct, never by taking your own side wholesale. In a safety-relevant file, keep the stricter behaviour and test it.
- Re-run the checks after resolving a conflict. Never change a test just to make a suite pass.

## Reviewing each other

Review comments are proposals; the branch owner decides. A claim that an invariant was weakened must be answered explicitly before merge. Don't approve the other agent's PR. William decides merges, releases and publication unless he has delegated them in writing.

## Invariants every change keeps

- Explicit selection and approval before any change, with a fresh before-state review and the 10-minute expiry.
- The original user's identity is preserved across elevation. There is no generic or arbitrary elevated command execution.
- Evidence is immutable, and incomplete or unknown outcomes are reported honestly. New receipts add records; they never rewrite existing ones.
- No automatic installer replay after an uncertain outcome.
- Action completion, verified state and symptom resolution stay separate.
- Local observations stay separate from tenant, licensing or enrollment verification.

## Stop and ask William when

- Both agents need the same files.
- A conflict can't be resolved without losing behaviour.
- A safety invariant seems to block the task.
- You are about to act on the other agent's branch.
- Work would need device changes, elevation, runtime installation, customer data, publication or a release.

## Never

- Claim a check passed when you didn't run it.
- Present Linux results as Windows or device acceptance.
- Commit customer evidence, identifiers, credentials or tokens.
