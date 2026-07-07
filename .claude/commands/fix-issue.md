---
description: Fix a GitHub issue following the Vanadium issue-fix workflow
argument-hint: <issue-number>
---

You are fixing GitHub issue **#$ARGUMENTS** in this repository. Follow this workflow strictly and in order. Do not skip steps.

## 0. Verify starting state

- **Normalize the argument.** `$ARGUMENTS` may arrive as `#102` or `102`. Strip any leading `#` and use the bare number `NNN` in every `gh` command below — `gh issue view` / `gh pr list` do not accept a leading `#`.
- **Shell-state warning.** Shell variables may not persist between separate command invocations. Treat every code block in this file as stand-alone: re-derive any variable (e.g. `DEFAULT_BRANCH`) inside the block that uses it rather than relying on earlier shell state.
- **Idempotency check.** This command is NOT safe to blindly re-run on the same issue, so verify it has not already been handled:

  ```bash
  gh issue view <NNN> --json state --jq .state    # must be OPEN — if CLOSED, STOP and report
  gh pr list --state open --search "Closes #<NNN> in:body" --json number,title,headRefName
  ```

  If the issue is closed, or an open PR already references it, STOP and report that instead of creating a duplicate branch/PR — suggest `/review-pr` or `/address-review` on the existing PR as the next step.
- Run `git status`. The working tree MUST be clean. If there are uncommitted changes, STOP and ask the user how to handle them — never let pre-existing changes leak into your commits.
- Check out the default branch and update it. Derive the default branch explicitly instead of guessing:

  ```bash
  DEFAULT_BRANCH=$(gh repo view --json defaultBranchRef --jq .defaultBranchRef.name)
  git checkout "$DEFAULT_BRANCH" && git pull
  ```

  All work starts from the up-to-date default branch.
- Run `dotnet build Vanadium.slnx` once now to establish a warning baseline. "No new warnings" in later steps means: no warnings beyond this baseline.

## 1. Read the issue

Fetch issue #$ARGUMENTS with `gh issue view $ARGUMENTS`. Issues follow the repo's issue-form templates (`.github/ISSUE_TEMPLATE/`), so map their sections directly:

- **Type** — take it from the title prefix `[Bug]` / `[Feat]`. If the prefix is missing, fall back to the `bug` / `enhancement` label. If prefix and label conflict, the title prefix wins. This determines the branch prefix in step 2. Do not re-guess from prose.
- **현재 동작 / 재현 절차 / 기대 동작** (bug) or **목적 / 배경 / 요구사항** (feat) — the problem to solve.
- **관련 위치 (아는 만큼)** — starting points for your search. If it is empty or shows `_No response_`, locate the relevant code yourself.
- **완료 조건 (Acceptance Criteria)** — the checklist your fix MUST satisfy. Carry it into the Verification step and confirm each item.
- **범위 밖 (건드리지 말 것) / 제약** — hard boundaries. Do NOT modify anything listed here even if it looks related. If you determine the fix is IMPOSSIBLE without touching an out-of-scope file, STOP: do not proceed — explain the conflict to the user (and/or comment on the issue) and wait for a decision.

Restate your understanding (type, root-cause hypothesis, acceptance criteria, out-of-scope) before touching code. If a required section is missing or self-contradictory, ask before proceeding rather than guessing.

## 2. Create a branch

Create a branch named by this rule:

- Format: `{bug|feat}/NNN-<2-4 word english summary>` — the issue number WITHOUT a `#`. A `#` in a branch name is valid to git but is a recurring footgun (shell-quoting mistakes, `%23` URL-encoding, noise in some CI tooling); the issue link lives in commit messages (`#NNN <요약>`) and the PR body (`Closes #NNN`) instead.
- `bug/` for defects, `feat/` for features/enhancements.
- All lowercase, words joined with `-`.
- Examples:
  - "Bug, 노트 생성 시 오버포스팅... #102" → `bug/102-invisible-purge-note`
  - "enhancement, SHARE 기능을 추가한다. #94" → `feat/94-add-share`

## 3. Checkout and implement

Check out the new branch and make the fix there. Follow the code conventions in CLAUDE.md (English-only code/comments, nullable handling, async I/O, file-per-class).

- **Minimal change principle.** Make only the changes required to satisfy the acceptance criteria. Do NOT refactor, reformat, or "improve" surrounding code the issue does not ask for — even when 범위 밖 is empty.
- **Tests.** For a bug fix, add a regression test that fails before the fix and passes after, whenever the affected code is testable. For a feature, add tests covering the 요구사항. If the repo has no test project covering the affected area, say so explicitly instead of silently skipping.
- **Schema changes.** If the fix changes the EF Core model/schema, add a migration (`dotnet ef migrations add ...`) as part of the implementation and commit it as its own logical unit. Run `dotnet ef database update` only if a local dev database is available in this environment; if not, state that the migration was created but not applied.

## 4. Commit in logical units

- Commit in coherent logical groups — never dump all changes in one final commit.
- Small fixes where a single commit fully captures one logical change ARE allowed as one commit.
- Multiple commits are fine, but each must be a defensible logical unit you can explain.
- **Build before every commit.** Run `dotnet build Vanadium.slnx` before each commit. If it fails, do NOT commit — fix the error first, then commit only once it compiles clean (no warnings beyond the step-0 baseline). This guarantees every commit compiles on its own without any later "build fix" commit or history rewrite.
- **Write commit messages in Korean.** Match the existing style (e.g. `#102 <요약>`).
- **Do NOT add a `Co-Authored-By:` trailer** (or any other co-author/attribution trailer) to commit messages. Commits should be attributed to the repository owner only — no `Co-Authored-By: Claude ...` line. This keeps GitHub from displaying a second "co-authored" committer on the commit.

## 5. Verification (before pushing)

Do NOT push until every item below passes:

- Run `dotnet test Vanadium.slnx` — all tests pass, including the new regression/feature tests from step 3.
- Every item under **완료 조건 (Acceptance Criteria)** is satisfied. Verify each item explicitly and state HOW it was verified (which test, command, or observed behavior) — do not simply assert it.
- Confirm scope with a stand-alone block (re-derive the default branch — do not assume shell state from step 0):

  ```bash
  DEFAULT_BRANCH=$(gh repo view --json defaultBranchRef --jq .defaultBranchRef.name)
  git diff "$DEFAULT_BRANCH"...HEAD --name-only
  ```

  No file listed under **범위 밖 / 제약** was modified, and no unrelated file changed.
- Each commit was built with `dotnet build Vanadium.slnx` before being made (see step 4) — so a final full build should already be clean.

If any item fails, go back to step 3, fix it, and re-verify.

## 6. Push

Once verification passes, push the branch: `git push -u origin <branch>`.

## 7. Open a PR

Create a pull request (`gh pr create`) targeting the default branch. Title and body in Korean, referencing the issue (e.g. `Closes #$ARGUMENTS`). Summarize what changed and why, and include how each acceptance criterion was verified.
