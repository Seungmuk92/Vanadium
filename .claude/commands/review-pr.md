---
description: Code-review a GitHub PR created by /fix-issue and post the review to GitHub
argument-hint: <pr-number>
---

You are code-reviewing pull request **#$ARGUMENTS** in this repository. This PR was (typically) produced by `/fix-issue`, so treat the review as an independent double-check of that automated fix. Follow this workflow strictly and in order. Do not skip steps. **This command reviews only — it does NOT change code, push, or merge.**

**Argument normalization:** `$ARGUMENTS` may arrive as `#123` or `123`. Strip any leading `#` and use the bare number in every `gh` command.

**Shell-state warning:** shell variables may not persist between separate command invocations. Treat every code block below as stand-alone — re-derive `HEAD_SHA`, `OWNER_REPO`, the original branch name, etc. inside the block that uses them rather than relying on earlier shell state.

## 0. Record starting state & check for prior reviews

Before anything else:

```bash
git branch --show-current   # remember this — you MUST return to it at the end
git status --short          # if the working tree is dirty, do NOT checkout anything; review via diff only

# current PR head commit
HEAD_SHA=$(gh pr view $ARGUMENTS --json headRefOid --jq .headRefOid)

# prior reviews WITH the commit each was submitted against (gh pr view --json reviews
# does NOT expose commit_id — the REST reviews API does)
OWNER_REPO=$(gh repo view --json nameWithOwner --jq .nameWithOwner)
gh api "repos/$OWNER_REPO/pulls/$ARGUMENTS/reviews" \
  --jq '.[] | {user: .user.login, state, commit_id, submitted_at, body_head: (.body[:40])}'
```

- **Record the current branch name.** Whatever happens during this review, the final step must restore the repository to this branch with a clean working tree.
- **Check existing reviews.** If a previous review from this same workflow already exists on the PR — identified by the hidden marker `<!-- generated-by: review-pr -->` in its `body` (for older reviews posted before the marker existed, fall back to recognizing the 요약/완료 조건 점검 structure) —:
  - Compare that review's `commit_id` to `HEAD_SHA`. If they **match**, the PR head is unchanged since that review — stop and report that the PR has already been reviewed at that commit; do not post a duplicate.
  - If `commit_id` differs from `HEAD_SHA` (new commits were pushed since), proceed, but scope the new review to what changed since that reviewed commit where practical (`gh pr diff $ARGUMENTS` still shows the full PR; use `git diff <commit_id>..$HEAD_SHA` after fetching for the incremental view), and say so in the 요약.

## 1. Load the PR

Fetch the PR and its diff:

```bash
gh pr view $ARGUMENTS --json number,title,body,headRefName,baseRefName,state,isDraft,files,additions,deletions,closingIssuesReferences
gh pr diff $ARGUMENTS --name-only
```

- Confirm the PR is open and not already merged/closed. If it is closed or merged, stop and report that rather than reviewing.
- **Large-PR handling:** if the PR touches more than ~15 files or ~800 changed lines, do NOT dump the entire diff at once. Instead, read it file-by-file (`gh pr diff $ARGUMENTS -- <path>` or `git show <head>:<path>` after fetching), starting with the files most relevant to the linked issue, so review quality stays consistent. Otherwise, load the full diff with `gh pr diff $ARGUMENTS`.
- From `body` / `closingIssuesReferences`, find the linked issue (`Closes #NNN`). If found, load it with `gh issue view <NNN>` — its **완료 조건 (Acceptance Criteria)** and **범위 밖 (건드리지 말 것)** sections are the contract this PR must satisfy. If no issue is linked, review against the PR description alone and note the missing link.

## 2. Understand the intent

Before judging the diff, restate in one short paragraph: what problem the PR claims to solve, the linked issue's acceptance criteria, and its out-of-scope boundaries. This is the yardstick for the rest of the review.

## 3. Review the diff

Read the full diff and the surrounding code (open files as needed — a diff hunk rarely tells the whole story). Check, in roughly this priority order:

- **Correctness** — does the change actually fix the issue / implement the feature? Any logic errors, off-by-one, null/`nullable` mishandling (nullable reference types are enabled — no `#nullable disable`, nulls handled explicitly), race conditions, or unhandled edge cases?
- **Acceptance criteria** — go through each **완료 조건** item and mark it with exactly one of four states:
  - **met** / **not-met** — confirmed either way from the diff and available verification.
  - **unverifiable** — cannot be confirmed *yet*, but the PR could still provide evidence (typical example: a missing regression test). This is actionable by `/address-review`.
  - **수동 확인 필요** — *environmentally* unverifiable in this workflow no matter what the PR adds: manual UI confirmation, behavior requiring a local dev DB, an external service, or hardware that is unavailable here. This is NOT actionable by `/address-review`; it is handed to the user. Distinguishing this from plain unverifiable is what prevents an infinite review→address loop.
- **Scope discipline** — nothing under **범위 밖 / 제약** was touched; no unrelated drive-by changes.
- **Architecture & conventions (CLAUDE.md)** — English-only code/comments/UI text (no Korean anywhere in code), file-per-class with namespace matching folder, PascalCase DTOs, async/await end-to-end for I/O, no new top-level dependencies without justification, Serilog structured logging (no `Console.WriteLine`, no string-concatenated log templates).
- **Project-specific invariants** — watch for the traps documented in CLAUDE.md, e.g.: any new note-content scan or bulk-delete must consider `IgnoreQueryFilters()` (soft-delete / archive); Tiptap serialization must keep user-visible text in element text content, never in attribute values; new searchable fields must extend both the trigram GIN index and the search query; new API endpoints need the DTO mirrored in both REST and Web projects; new upload MIME types added to both controller and frontend; schema changes accompanied by a new EF Core migration (existing migrations never edited).
- **Security** — auth/JWT handling, rate limiting on `/api/auth/login`, PBKDF2 parameters unchanged, file-upload whitelist/size cap intact, no secrets newly introduced beyond the intentionally-committed dev config.
- **Tests & verification** — apply this rule: **bug fixes and behavior changes require a regression/behavior test in `Vanadium.Note.REST.Tests`** (not-met counts as blocking); pure refactors need existing tests still passing; config, docs, and build-script changes are exempt from new tests. Also check whether the PR evidences a clean `dotnet build Vanadium.slnx`.
- **Commit hygiene** — commits are coherent logical units, messages in Korean matching `#NNN <요약>` style, and no `Co-Authored-By:` / attribution trailer.

### Verifying claims (build / targeted tests)

If a claim needs verification you can do cheaply, run it — but keep the branch and working tree pristine:

- **Preferred (no branch switch):** fetch the PR head without checking it out and inspect it read-only:

  ```bash
  git fetch origin pull/$ARGUMENTS/head:refs/remotes/pr/$ARGUMENTS
  git show pr/$ARGUMENTS:<path>          # inspect files at the PR head
  ```

- **If a build/test run is genuinely needed:** checkout the PR head **detached** so no local branch is created or moved:

  ```bash
  git checkout --detach pr/$ARGUMENTS
  dotnet build Vanadium.slnx             # and/or targeted: dotnet test --filter <name>
  git checkout <original-branch>         # the branch recorded in step 0 — ALWAYS return
  git status --short                     # must be clean; if not, restore before continuing
  ```

- Never commit, amend, stage, or otherwise modify anything on the PR branch. If the working tree was dirty in step 0, skip build/test verification entirely and mark affected criteria as unverifiable.

## 4. Decide a verdict

Pick exactly one, using these hard rules (do not soften them):

- **Request changes** — REQUIRED if ANY of the following holds:
  - any 완료 조건 item is **not met**;
  - any 범위 밖 area was touched (scope violation);
  - a correctness bug, security regression, or project-invariant breach was found;
  - a bug-fix/behavior-change PR lacks the required test per the rule above.
- **Approve** — only when **every** 완료 조건 item is **met** (none not-met, none unverifiable, none 수동 확인 필요), scope is clean, and conventions/security/tests pass.
- **Comment** — no blocking problems, but either (a) one or more 완료 조건 items are **unverifiable** or **수동 확인 필요**, or (b) there are non-trivial observations worth surfacing before merge. **An unverifiable or 수동-확인-필요 acceptance criterion caps the verdict at Comment — never Approve.**

**Loop-exit rule:** if the verdict is Comment and the ONLY reasons are 수동 확인 필요 items (no blocking issues, no not-met, no plain-unverifiable items, no declined-scope problems), the automated fix→review→address loop is DONE from the workflow's perspective — no PR change can resolve what remains. Say so explicitly in the 요약: list exactly what the user must verify manually, and state that **`/address-review` should NOT be run for these items**. This prevents an infinite Comment → address → Comment cycle over things the workflow can never prove.

## 5. Post the review to GitHub

Write the review body in **Korean** (matching the repo's issue/PR language), structured as:

- **요약** — one-paragraph verdict and why. If this is a re-review after new commits (step 0), say so. If the loop-exit rule from step 4 applies, state it here per that rule.
- **완료 조건 점검** — checklist of each acceptance criterion with met / not-met / unverifiable / 수동 확인 필요.
- **주요 지적 (Blocking)** — blocking issues, each with `file:line` and a concrete fix suggestion. Omit the section if none.
- **제안 (Non-blocking)** — nits and improvements. Omit if none.
- **검증** — what you built/ran and the result (or why verification was skipped).
- End the body with the marker line `<!-- generated-by: review-pr -->` (an HTML comment — invisible when rendered on GitHub). Step 0 of this command and `/address-review` both rely on this marker to reliably identify reviews produced by this workflow, instead of guessing from body structure that a human review might coincidentally match.
- On the **very last line**, after `<!-- generated-by: review-pr -->`, ALWAYS emit a machine-readable status marker — regardless of verdict and regardless of the self-review fallback below:

  `<!-- review-pr: verdict=approve|request-changes|comment; loop=done|continue -->`

  - `verdict` is the TRUE decided verdict from step 4. Set it even when the self-review fallback forced a `--comment` submission — so a review posted with `--comment` may still carry `verdict=approve` or `verdict=request-changes`.
  - `loop=done` means the fix→review→address loop should STOP: either `verdict=approve`, OR the step-4 **Loop-exit rule** applies (a Comment whose only remaining items are 수동 확인 필요). `loop=continue` means there is something `/address-review` can act on: `verdict=request-changes`, or a Comment with any not-met / plain-unverifiable / blocking item.
  - This marker is the **single source of truth** for the `/fix-and-review` orchestrator, because in this single-owner repo the GitHub review *state* is always `COMMENT` (self-review) and must not be trusted for branching.

**Do not use separate inline comments.** All line-specific points go in the review body as `file:line` references, so the verdict and every remark land in one atomic review. (`gh pr review --body` cannot attach inline comments to the same review; splitting them across multiple submissions fragments the review.)

Post it with the matching verdict flag:

```bash
gh pr review $ARGUMENTS --approve         --body "$(cat <<'EOF'
<리뷰 본문>
EOF
)"
# or --request-changes / --comment
```

**Self-review fallback:** if the PR author is the same account running this review, GitHub rejects `--approve` and `--request-changes` (HTTP 422: cannot approve/request changes on your own pull request). In that case, re-post with `--comment` and put the intended verdict on the first line of the body in bold, e.g. **[판정: Request changes]** or **[판정: Approve]**, then note in the 요약 that the formal verdict flag was unavailable due to self-review restrictions.

## 6. Report back & restore state

- Print the chosen verdict, a one-line summary, and the URL of the posted review (`gh pr view $ARGUMENTS --json url`).
- Print the `<!-- review-pr: verdict=…; loop=… -->` marker values (verdict and loop) as the final line of your report, so a caller (`/fix-and-review`) can branch on them without re-parsing the Korean body.
- Confirm the repository is back on the branch recorded in step 0 with a clean working tree (`git branch --show-current`, `git status --short`). If not, restore it now.
- Do NOT merge, push, or edit code — the review ends here.
