---
description: Address the latest /review-pr review on a PR by amending its existing branch
argument-hint: <pr-number>
---

You are addressing the code review on pull request **#$ARGUMENTS** in this repository. This PR was produced by `/fix-issue` and reviewed by `/review-pr`; your job is to make the follow-up fixes the review asked for **on the PR's existing branch** and push, so the same PR updates in place. Follow this workflow strictly and in order. Do not skip steps. **Never create a new branch and never open a new PR — you are continuing an existing one.**

**Argument normalization:** `$ARGUMENTS` may arrive as `#123` or `123`. Strip any leading `#` and use the bare number in every `gh` command.

**Shell-state warning:** shell variables may not persist between separate command invocations. Treat every code block below as stand-alone — re-derive `BRANCH`, `HEAD_SHA`, `OWNER_REPO`, etc. inside the block that uses them rather than relying on earlier shell state.

**Terminal status marker (ALWAYS emit last).** Whatever the outcome, the LAST line of your final report MUST be a machine-readable marker so the `/fix-and-review` orchestrator can decide whether to loop again:

- Success — new commits pushed in step 7: `<!-- address-review: status=changes-pushed; pr=<NNN> -->`
- No-actionable-items exit (step 1): `<!-- address-review: status=no-actionable-items; pr=<NNN> -->`
- Already approved at current head (step 1): `<!-- address-review: status=already-approved; pr=<NNN> -->`
- Stale review — review's `commit_id` ≠ HEAD (step 1): `<!-- address-review: status=stale-review; pr=<NNN> -->`
- Any other STOP — PR closed/merged, 범위 밖 conflict, non-fast-forward divergence: `<!-- address-review: status=stopped; pr=<NNN>; reason=<short> -->`

The four non-`changes-pushed` statuses all mean "no new commit was pushed"; an orchestrator must treat them as loop-terminating (do NOT re-review), not as a reason to retry.

## 0. Verify starting state

- Run `git status`. The working tree MUST be clean. If there are uncommitted changes, STOP and ask the user how to handle them — never let pre-existing changes leak into your commits.
- Record the current branch name (`git branch --show-current`) so you can note where you started.

## 1. Load the PR and its latest review

```bash
gh pr view $ARGUMENTS --json number,title,body,headRefName,baseRefName,state,isDraft,closingIssuesReferences

# latest review WITH the commit it was submitted against
OWNER_REPO=$(gh repo view --json nameWithOwner --jq .nameWithOwner)
gh api "repos/$OWNER_REPO/pulls/$ARGUMENTS/reviews" \
  --jq '.[] | {user: .user.login, state, commit_id, submitted_at, body}'

HEAD_SHA=$(gh pr view $ARGUMENTS --json headRefOid --jq .headRefOid)
```

- Confirm the PR is **open** and not merged/closed. If it is closed or merged, STOP and report that rather than changing code.
- Take the **most recent** review from the `/review-pr` workflow — identified by the hidden marker `<!-- generated-by: review-pr -->` in its `body` (for older reviews posted before the marker existed, fall back to recognizing the 요약 / 완료 조건 점검 structure). That review's body is your work list. Do NOT act on a human review that merely looks similar but lacks the marker without confirming with the user.
- **Decide whether there is anything to do:**
  - If the latest review's verdict is **Approve** (or, for self-review, `[판정: Approve]` on the first line) AND its `commit_id` equals `HEAD_SHA`, there is nothing to address — STOP and report that the PR is already approved at the current head.
  - If the latest review's `commit_id` differs from `HEAD_SHA`, new commits were pushed after that review. STOP and tell the user to re-run `/review-pr #$ARGUMENTS` first, so you act on a review of the current head rather than a stale one.
  - **No-actionable-items exit:** if the review contains **no 주요 지적 (Blocking) items**, **no not-met 완료 조건**, and **no plain-unverifiable 완료 조건**, and every remaining concern is either a **수동 확인 필요** item or a Non-blocking 제안 you judge should be declined (out of scope or not low-risk), then there is nothing this command can do. STOP and report "no actionable review items" — list any 수동 확인 필요 items for the user to verify manually and any declined 제안 with the reason. Do NOT manufacture changes just to have something to commit; steps 3–7 assume real changes exist and must not run in this case.
  - Otherwise (Request changes, or Comment with actionable items, against the current head), proceed.

## 2. Restate the work list

From the review body, extract and restate:

- Every item under **주요 지적 (Blocking)** — these MUST all be resolved.
- Items under **제안 (Non-blocking)** — address them only if they are low-risk and clearly in scope; otherwise note explicitly that you are leaving them and why. Do not let non-blocking nits expand the change.
- Any 완료 조건 marked **not-met** or **unverifiable** in the review — treat not-met as blocking, and for unverifiable, add whatever the review said was missing (usually a test or clearer evidence).
- Items marked **수동 확인 필요** are explicitly NOT part of your work list. They are environmentally unverifiable — no PR change can resolve them — so leave them for the user and do not attempt to "fix" them. They are the loop's exit condition, not a defect. (If they were the review's only remaining concerns, you should already have stopped at the no-actionable-items exit in step 1.)

If found, also re-load the linked issue (`Closes #NNN` from `body` / `closingIssuesReferences`) with `gh issue view <NNN>` — its **완료 조건 (Acceptance Criteria)** and **범위 밖 (건드리지 말 것)** sections are still the binding contract. Do not resolve a review point in a way that violates 범위 밖; if the review and 범위 밖 genuinely conflict, STOP and surface the conflict to the user rather than guessing.

## 3. Check out the existing PR branch

Do NOT create a branch. Switch to the PR's own branch:

```bash
BRANCH=$(gh pr view $ARGUMENTS --json headRefName --jq .headRefName)
git fetch origin
git checkout "$BRANCH"
git pull --ff-only origin "$BRANCH"   # make sure you are at the PR head before adding commits
```

If the local branch has diverged from the remote and cannot fast-forward, STOP and ask the user how to reconcile — do not force anything.

**Establish the warning baseline.** This session did not run `/fix-issue`, so no baseline exists yet. Run `dotnet build Vanadium.slnx` once now — on the PR branch, before changing anything — and record the warnings. "No new warnings" in step 5 means: no warnings beyond this baseline.

## 4. Implement the fixes

Make only the changes the review requires, on this branch. Follow the same rules as `/fix-issue`:

- **Minimal change principle.** Resolve the review points and nothing more — no opportunistic refactors, reformatting, or scope creep, even in files you are already touching.
- **Conventions (CLAUDE.md).** English-only code/comments/UI text, explicit null handling (nullable reference types on, no `#nullable disable`), file-per-class with namespace matching folder, PascalCase DTOs, async/await end-to-end for I/O, no new top-level dependencies without justification, Serilog structured logging (no `Console.WriteLine`, no string-concatenated templates).
- **Tests.** If the review flagged a missing regression/behavior test, add it now (in `Vanadium.Note.REST.Tests`) so it fails before your fix and passes after. If the review pointed out a broken test, fix the cause, not the test's expectation.
- **Schema changes.** If a fix changes the EF Core model/schema, add a new migration (`dotnet ef migrations add ...`) — never edit an existing migration. Run `dotnet ef database update` only if a local dev DB is available; otherwise state that the migration was created but not applied.

## 5. Commit in logical units

- Commit in coherent logical groups — never dump everything in one catch-all commit. A single commit is fine when one logical change fully captures the fix.
- **Build before every commit.** Run `dotnet build Vanadium.slnx` before each commit; if it fails, fix it first and commit only once it compiles clean (no warnings beyond the baseline established in step 3). Every commit must build on its own — no later "build fix" commit or history rewrite.
- **Write commit messages in Korean**, matching the existing `#NNN <요약>` style. Where useful, make clear the commit addresses review feedback (e.g. `#102 리뷰 반영: <요약>`).
- **Do NOT add a `Co-Authored-By:` trailer** (or any attribution trailer). Commits are attributed to the repository owner only.
- Do NOT amend, squash, or force-push existing PR commits — only add new commits on top, so the reviewer can see exactly what changed since the last review.

## 6. Verification (before pushing)

Do NOT push until every item below passes:

- Run `dotnet test Vanadium.slnx` — all tests pass, including any test added in step 4.
- Every **주요 지적 (Blocking)** item from the review is resolved. State HOW each was resolved (which change / test / observed behavior) — do not merely assert it.
- Every **완료 조건 (Acceptance Criteria)** item from the linked issue is still satisfied (you did not regress a previously-met item while fixing another).
- Confirm scope with a stand-alone block (re-derive the default branch):

  ```bash
  DEFAULT_BRANCH=$(gh repo view --json defaultBranchRef --jq .defaultBranchRef.name)
  git diff "$DEFAULT_BRANCH"...HEAD --name-only
  ```

  No file under **범위 밖 / 제약** was touched and no unrelated file changed.

If any item fails, go back to step 4, fix it, and re-verify.

## 7. Push

Push the new commits to the same branch (this updates the existing PR — do NOT open a new one):

```bash
# re-derive — do not rely on $BRANCH surviving from step 3
BRANCH=$(gh pr view $ARGUMENTS --json headRefName --jq .headRefName)
git push origin "$BRANCH"
```

## 8. Report back

- Summarize which review points were addressed and how, and note any non-blocking suggestions you deliberately left (with the reason).
- Print the PR URL (`gh pr view $ARGUMENTS --json url`).
- Remind the user to re-run **`/review-pr #$ARGUMENTS`** for a re-review of the new head — its step 0 will compare commit IDs and scope the review to what changed.
- Do NOT merge — the loop ends by returning to review.
