---
description: Rewrite an existing GitHub issue into the repo's standard template format
argument-hint: <issue-number | all>
---

You normalize existing GitHub issues into the repo's standard template so that `/fix-issue` can consume them. **This command ONLY rewrites issue text — it does NOT create branches, change code, or fix anything.**

Target: **$ARGUMENTS** (an issue number, or `all` = every OPEN issue).

## 1. Load target issue(s)

- Single number: `gh issue view $ARGUMENTS --json number,title,body,labels`
- `all`: `gh issue list --state open --json number,title,labels`, then process each in ascending number order.

## 2. Determine type

Classify each issue as **bug** or **feature/enhancement** from its title, labels, and body:
- Bug → title prefix `[Bug] `, label `bug`.
- Feature/enhancement → title prefix `[Feat] `, label `enhancement`.

Keep the existing human-written title text; only add/normalize the prefix (don't duplicate if already present). Set the matching label with `--add-label`; if the wrong type label is present, note it (do not silently remove unless obviously wrong).

## 3. Rewrite the body

Reformat the body into the exact section headers defined in `docs/issue-authoring.md` (read that file for the canonical structure):

- Bug: `### 현재 동작` / `### 재현 절차` / `### 기대 동작` / `### 관련 위치 (아는 만큼)` / `### 완료 조건 (Acceptance Criteria)` / `### 범위 밖 (건드리지 말 것)`
- Feat: `### 목적 / 배경` / `### 요구사항` / `### 관련 위치 (아는 만큼)` / `### 완료 조건 (Acceptance Criteria)` / `### 범위 밖 / 제약`

Rules for content:
- **Preserve all information from the original body.** Map existing text into the right section; never drop details.
- **Missing sections:** infer a draft from the code and repo context (CLAUDE.md, relevant source, `docs/`). Write the inferred draft, then mark it on the line above with `> (추론 — 확인 필요)` so a human knows to verify it. Do NOT present inferences as confirmed fact.
- **완료 조건:** write as true/false-verifiable statements. Include `- [ ] 빌드 클린 (dotnet build Vanadium.slnx)` and, if the change likely touches the DB schema, `- [ ] EF Core 마이그레이션 추가`.
- If the original genuinely lacks the info and you cannot reasonably infer it, leave the section as `_확인 필요_` rather than fabricating specifics.
- Do not invent reproduction steps, error messages, or acceptance criteria that contradict the original.

## 4. Apply the update

```bash
gh issue edit <번호> \
  --title "[Bug|Feat] <기존 제목>" \
  --add-label <bug|enhancement> \
  --body "$(cat <<'EOF'
<재작성된 본문>
EOF
)"
```

## 5. Report

For each processed issue, print: number, new title, chosen type, and which sections were **inferred** (`추론 — 확인 필요`) vs. carried over. Recommend the user run `gh issue view <번호>` to verify inferred sections before running `/fix-issue`.

Do NOT proceed to fixing. Normalization ends here.
