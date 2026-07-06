# 이슈 작성 가이드 (Claude 자동 작성용)

Claude가 코드 리뷰 등에서 발견한 문제를 GitHub 이슈로 등록할 때 사용하는 표준 형식이다.
`.github/ISSUE_TEMPLATE/`의 이슈 폼과 **동일한 섹션 헤더**를 쓰므로, 이렇게 작성한 이슈는
`/fix-issue`(`.claude/commands/fix-issue.md`)가 그대로 읽어 수정을 진행할 수 있다.

`gh issue create`는 웹 폼을 거치지 않으므로, 본문을 아래 헤더 구조로 직접 작성해야 왕복이 성립한다.

## 공통 규칙

- 이슈 하나에는 **논리적으로 하나의 주제**만 담는다 (커밋을 논리 단위로 나누기 위함).
- 제목 접두사와 라벨을 반드시 붙인다: 버그는 `[Bug] ` + 라벨 `bug`, 기능/개선은 `[Feat] ` + 라벨 `enhancement`.
- 제목은 증상이 아니라 **핵심을 한 줄로** — 여기서 브랜치 영문 요약(2~4단어)이 나온다.
- **완료 조건**은 참/거짓으로 판정 가능한 문장으로 쓴다. ("잘 되게" X → "아카이브된 노트는 mention 검색 결과에 나타나지 않는다" O)
- 아는 만큼 **코드 위치**(파일·메서드·문서 경로)를 지목한다. 모르면 해당 섹션을 비운다.
- 스키마 변경이 예상되면 완료 조건에 마이그레이션 추가를 명시한다.

## 버그 (gh 예시)

```bash
gh issue create --title "[Bug] <핵심 요약>" --label bug --body "$(cat <<'EOF'
### 현재 동작
<무엇이 잘못되는지. 에러/로그가 있으면 그대로>

### 재현 절차
1.
2.
3.

### 기대 동작
<어떻게 되어야 하는지>

### 관련 위치 (아는 만큼)
- 파일/메서드: 예) Vanadium.Note.REST/Services/NoteService.cs → SearchForMention
- 참고 문서: 예) docs/plannings/note-archive-feature.md

### 완료 조건 (Acceptance Criteria)
- [ ] <검증 가능한 조건>
- [ ] 빌드 클린 (dotnet build Vanadium.slnx)

### 범위 밖 (건드리지 말 것)
- <손대면 안 되는 영역 / CLAUDE.md 제약>
EOF
)"
```

## 기능 / 개선 (gh 예시)

```bash
gh issue create --title "[Feat] <핵심 요약>" --label enhancement --body "$(cat <<'EOF'
### 목적 / 배경
<왜 필요한지, 어떤 사용자 흐름인지>

### 요구사항
- [ ]
- [ ]

### 관련 위치 (아는 만큼)
- 프론트: 예) Vanadium.Note.Web/Pages/Archive.razor
- 백엔드: 예) NotesController + NoteService
- 참고 문서:

### 완료 조건 (Acceptance Criteria)
- [ ] 빌드 클린 (dotnet build Vanadium.slnx)
- [ ] (스키마 변경 시) EF Core 마이그레이션 추가
- [ ]

### 범위 밖 / 제약
- 예) Vanadium.Note.Shared 프로젝트 신설 금지, JWT issuer/audience 검증 추가 금지
EOF
)"
```

> 헤더 텍스트는 `.github/ISSUE_TEMPLATE/`의 폼 필드 라벨과 일치해야 한다. 폼을 바꾸면 이 문서도 함께 갱신한다.
