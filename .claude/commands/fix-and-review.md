---
description: Orchestrate /fix-issue → /review-pr → /address-review in a loop until the PR is clean or a round cap is hit, then hand the merge decision to the user
argument-hint: <issue-number> [max-address-rounds]
---

You are driving the **full automated fix→review→address loop** for GitHub issue **#$ARGUMENTS** in this repository. Your job is to sequence three existing commands — `/fix-issue`, `/review-pr`, `/address-review` — and decide, after each review, whether to loop again or stop. You **orchestrate only**: you never merge, never close, and never invent work of your own beyond what those commands do.

## Marker & code contract (source of truth for branching)

Each sub-command emits exactly ONE of these as the **last line** of its output. Branch only on these; recover a missing marker via the fallbacks below.

| Emitter | Marker (allowed values) |
|---|---|
| `/fix-issue` | `<!-- fix-issue: status=pr-created; issue=<N>; pr=<N> -->` — PR opened |
| | `<!-- fix-issue: status=stopped; issue=<N>; reason=<code> -->` where `<code>` ∈ `existing-pr` (resume point, not a failure), `issue-closed`, `dirty-tree`, `scope-conflict`, `bad-issue`, `other` |
| `/review-pr` | `<!-- review-pr: verdict=approve\|request-changes\|comment; loop=done\|continue -->` — `loop=done` ⇒ stop (approve, or Comment with only 수동 확인 필요); `loop=continue` ⇒ actionable, run address |
| `/address-review` | `<!-- address-review: status=changes-pushed; pr=<N> -->` — new commits pushed, re-review |
| | `<!-- address-review: status=no-actionable-items\|already-approved\|stale-review\|stopped; pr=<N>[; reason=<short>] -->` — no commit pushed |

Ground rules: markers (not GitHub review state, which is always `COMMENT` here) drive every decision; `changes-pushed` is the only address status that loops; `existing-pr` is the only stop-reason that resumes rather than halts; `stale-review` is recoverable exactly once (step 2c).

**Argument parsing.** `$ARGUMENTS` is `<issue-number> [max-address-rounds]`. The first token is the issue number (strip any leading `#`); if it is not a positive integer, STOP and report the malformed argument. The optional second token is the address-round cap; if absent, default to **3**; if present but NOT a positive integer, STOP and report the malformed argument — do not guess what the user meant. Call the issue number `ISSUE` and the cap `MAX_ROUNDS` below.

**How to run each phase.** Each phase follows the *exact* workflow in its command file — read the file and execute it step by step as if the user had typed the slash command:

- Phase A → `.claude/commands/fix-issue.md` (arg = `ISSUE`)
- Review → `.claude/commands/review-pr.md` (arg = `PR`)
- Address → `.claude/commands/address-review.md` (arg = `PR`)

**`$ARGUMENTS` substitution rule (do not violate):** when executing a sub-command file, every `$ARGUMENTS` inside THAT file is replaced with the **single bare number** for that phase — `ISSUE` for fix-issue, `PR` for review-pr and address-review. NEVER substitute this orchestrator's own raw argument string (e.g. `"102 3"`) into a sub-command; that would produce broken invocations like `gh issue view 102 3`.

Do not re-implement or shortcut those workflows; this command's only added logic is the sequencing and the stop/loop decisions below. Each of those commands emits a machine-readable **status marker** as its last line — those markers, not prose and not the GitHub review state, are what you branch on.

**Single-owner reality.** This repo is single-owner, so every PR is a self-review: the GitHub review *state* is always `COMMENT` and cannot be trusted. Read the verdict from the `<!-- review-pr: verdict=…; loop=… -->` marker, never from `gh` review state.

**Marker fallbacks (when a sub-command's terminal marker is missing from its output):**

- **fix-issue** — derive the PR (substitute the bare issue number for `$ISSUE`): `gh pr list --state open --search "Closes #$ISSUE in:body" --json number --jq '.[0].number'`. If that yields nothing, STOP and report that no PR was produced.
- **review-pr** — the same marker line is also embedded in the review body posted to GitHub. Recover it from there:

  ```bash
  OWNER_REPO=$(gh repo view --json nameWithOwner --jq .nameWithOwner)
  gh api "repos/$OWNER_REPO/pulls/$PR/reviews" \
    --jq '[.[] | select(.body | contains("<!-- generated-by: review-pr -->"))] | last | .body'
  ```

  Read `verdict` / `loop` from the `<!-- review-pr: … -->` line of that body. If no such review exists on the PR either, STOP and report that the review phase failed to post.
- **address-review** — recover the outcome by comparing the PR head before/after the phase. Record `HEAD_SHA_BEFORE=$(gh pr view $PR --json headRefOid --jq .headRefOid)` immediately before running the phase; re-read it after. Head changed → treat as `status=changes-pushed`. Head unchanged AND no marker → you cannot tell WHICH non-push exit occurred, so STOP and report the ambiguity to the user; do not loop again.

**Inter-phase working-tree check.** After **every** phase (A, review, address) completes, run `git status --short`. If the tree is dirty, STOP the whole orchestration immediately and report which phase left it dirty — do not let a later phase inherit a corrupted state (e.g. review-pr silently degrading to diff-only mode, or address-review hard-stopping one phase too late after a wasted review comment was already posted).

---

## 0. Preconditions

- Confirm the working tree is clean (`git status --short`). If it is dirty, STOP immediately and ask the user how to proceed — do not start a run on top of uncommitted changes.
- Initialize `rounds = 0` (counts `/address-review` invocations — the repeated, bounded step).
- Initialize `stale_recoveries = 0` (see step 2c) and `reviews_posted = 0` (for the final report).

## 1. Phase A — Fix the issue

Run the `/fix-issue` workflow for issue `ISSUE`. When it finishes, read its terminal marker:

- `status=pr-created` → capture `pr=<N>` as `PR` and continue to step 2 (fresh run). If the marker is missing, use the fix-issue fallback above.
- `status=stopped` → branch on the `reason` **code**:
  - **`reason=existing-pr` → RESUME MODE.** This is the normal state after a previous run hit the round cap or was interrupted, so do not treat it as a failure. Derive the PR (substitute the bare issue number for `$ISSUE`):

    ```bash
    PR=$(gh pr list --state open --search "Closes #$ISSUE in:body" --json number --jq '.[0].number')
    ```

    If a PR number is found, tell the user you are **resuming** the loop on existing PR `#PR` (rounds and the cap start fresh for this invocation), then continue to step 2. If no PR number can be derived despite the code, STOP and report the inconsistency.
  - **Any other code** (`issue-closed`, `dirty-tree`, `scope-conflict`, `bad-issue`, `other`) → STOP the whole orchestration and relay the `reason` to the user verbatim. Do **not** proceed to review.

## 2. Phase B — Review ⇄ address loop

Loop:

**2a. Review.** Run the `/review-pr` workflow for `PR`. It posts a review and emits `<!-- review-pr: verdict=…; loop=… -->` (recover via the review-pr fallback if the marker is missing from output). If it actually posted a **new** review (i.e. it did NOT take the step-0 "already reviewed at this head" early stop described in the note below), increment `reviews_posted`. Read the `loop` field:

- `loop=done` → the loop is finished (verdict is **approve**, or a Comment whose only remaining items are 수동 확인 필요). Go to **step 3**.
- `loop=continue` → there are actionable items. Go to 2b.

Note: review-pr's own step 0 may also stop without posting ("already reviewed at this head"). That is not an error — if the existing review's marker says `loop=done`, go to step 3; if `loop=continue`, go to 2b using that existing review as the work list.

**2b. Round cap (backstop).** If `rounds >= MAX_ROUNDS`, STOP: do not run `/address-review` again. Go to step 3 and report that the cap was reached with the review still requesting changes — the user decides whether to keep going (they can re-run `/fix-and-review ISSUE` later; resume mode in step 1 will pick the same PR back up). This bound is the hard guarantee against an infinite loop, on top of the commands' own semantic exits.

**2c. Address.** Increment `rounds`. Record `HEAD_SHA_BEFORE` (see marker fallbacks). Run the `/address-review` workflow for `PR`. Read its terminal marker (or fall back to the before/after head comparison):

- `status=changes-pushed` → new commits landed on the PR head. Loop back to **2a** to re-review the new head. (The commit_id handshake is enforced by the commands themselves: `/address-review` refuses to run against a stale review, and `/review-pr` refuses to duplicate a review at an unchanged head — so always re-review after a push, and never run `/address-review` twice without a review in between.)
- `status=stale-review` → the review's `commit_id` no longer matches the PR head (new commits appeared after the review — e.g. a manual push mid-run). This is **recoverable by exactly one re-review**: if `stale_recoveries == 0`, set `stale_recoveries = 1`, decrement `rounds` (this address pass did no work and must not consume the cap), and loop back to **2a** to review the current head. If `stale_recoveries >= 1` (it happened again), something is racing this workflow — STOP and go to step 3; report the repeated staleness so the user can investigate.
- `status=no-actionable-items` / `already-approved` / `stopped` → **no new commit was pushed** and no address pass can move this state. STOP and go to step 3; do **not** loop again. Report the status (and `reason` if present) so the user can resolve it manually.

## 3. Hand off to the user (never merge)

Stop here regardless of how you exited the loop. Do **not** merge, close, or push anything. Report, concisely:

- Issue `#ISSUE`, PR `#PR` and its URL (`gh pr view PR --json url --jq .url`), and whether this was a fresh run or a **resume** of an existing PR.
- The final review verdict and the exit reason (approved / loop-exit on 수동 확인 필요 / round cap reached / repeated stale-review / stuck: `<status>`).
- `rounds` used out of `MAX_ROUNDS`, and `reviews_posted` — so the user knows how many review comments this run added to the PR.
- Any **수동 확인 필요** items from the latest review that the user must verify by hand before merging, and any declined non-blocking 제안.
- **Self-review caveat:** even when the final verdict is Approve, GitHub will NOT show the PR as "Approved" — self-reviews are posted as comments, and the verdict lives in the **[판정: …]** line / marker of the review body. Say this explicitly so the user does not misread the PR page.
- A clear closing line: the user should review the PR and **merge it themselves** if satisfied.

## Guardrails (do not violate)

- **Never merge or close** the PR — the merge decision is always the user's.
- `/address-review` runs **at most `MAX_ROUNDS` times** per invocation of this command (a stale-review recovery pass that pushed nothing does not count).
- **Always re-run `/review-pr` after `/address-review`**, and never run `/address-review` twice in a row without a review between them.
- **At most ONE stale-review recovery** per invocation — a second stale-review halts the run.
- When executing sub-commands, substitute their `$ARGUMENTS` with the single bare number for that phase only — never this command's raw argument string.
- Check `git status --short` after every phase; a dirty tree halts the orchestration immediately.
- Any `status=stopped` (other than the resume case in step 1) or hard failure (build/test/push error, auth failure, scope conflict) from **any** sub-command halts the whole loop and returns control to the user — never paper over it and continue.
- Branch on **markers** (with the defined fallbacks), never on GitHub review state (always `COMMENT` here) or on a best-guess reading of the Korean review body.
