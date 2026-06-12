# 역할 및 목표
당신은 개발자용 노트 앱(Notion-like, 마크다운/리치텍스트 하이브리드) 분야의 시니어 기획자입니다.
아래 기능에 대한 기획 문서를 작성해 주세요. 이 문서는 이후 Claude를 통한
코드 구현 단계에서 그대로 입력으로 사용될 예정이므로, 구현에 필요한 수준의
구체성을 갖춰야 합니다.

# 기능 개요
- 기능명: Note Editor Collapsible Content (Toggle / Toggle Heading / Accordion)
- 한 줄 설명: 노트 본문에서 내용을 접고 펼칠 수 있는 블록을 제공해 긴 문서의 구조화와 가독성을 높인다.
- 배경/문제 상황: 현재 에디터는 모든 내용이 항상 펼쳐진 상태로만 표시된다. 기술 노트 특성상 코드 블록, 로그, 참고 자료 등 부피가 큰 내용이 많아 문서가 길어지면 탐색이 어렵다. Notion의 토글처럼 세부 내용을 접어둘 수 있어야 한다.
- 포함 범위 (3가지 모두 기획에 포함):
  1. **토글 블록 (Toggle Block)** — Notion 스타일. 한 줄 요약(summary) + 접을 수 있는 본문(content). 핵심 기능.
  2. **토글 헤딩 (Toggle Heading)** — H1~H3 헤딩 자체를 접이식으로 만들어, 해당 헤딩의 하위 내용을 숨기는 방식.
  3. **아코디언 그룹 (Accordion Group)** — 여러 토글을 묶는 그룹 컨테이너. 그룹 내에서는 한 번에 하나만 펼쳐진다(상호 배타적 펼침).

# 기술 컨텍스트
- 언어/프레임워크: C# / .NET 10, Blazor WebAssembly (MudBlazor), 백엔드 ASP.NET Core Web API + PostgreSQL (EF Core)
- 에디터: **Tiptap v2** (esm.sh CDN import, npm 빌드 체인 없음). 노트 본문은 Tiptap이 생성한 **HTML 문자열**로 DB에 저장됨 (`NoteItem.Content` + 검색용 `ContentText`).
- 관련 모듈:
  - `Vanadium.Note.Web/wwwroot/js/tiptap-editor.js` — 모든 Tiptap 확장과 JS interop(`tiptapInterop.*`)이 이 단일 파일에 있음. 커스텀 노드 선례: `Callout`, `MermaidNode`, `FileAttachment`, `PageLink`. 신규 블록은 이 패턴을 따라야 함.
  - `NoteEditor.razor` — interop을 호출하는 유일한 컴포넌트. 아카이브된 노트는 read-only 모드(`setEditable(false)`)로 렌더링됨.
  - 슬래시 커맨드(`createSlashCommandsExtension`)와 BubbleMenu가 이미 존재 — 새 블록 삽입 UX는 슬래시 커맨드에 통합.
  - `tiptap-markdown` 확장 사용 중 — 마크다운 붙여넣기/직렬화와의 호환 여부 검토 필요.
- 외부 표준/의존성 제약:
  - Tiptap v2의 공식 `Details` 확장은 **Pro(유료) 확장**이므로 사용 불가. `Callout`처럼 `Node.create()` 기반 **커스텀 노드로 직접 구현**하는 것을 기본 방향으로 한다.
  - 신규 top-level 의존성 추가는 명확한 정당화 없이는 금지 (프로젝트 정책).
- 제약 조건:
  - 코드·주석·UI 텍스트는 전부 영어 (한국어 금지).
  - 저장 포맷은 HTML이므로, 접힘 상태를 HTML 속성으로 보존할지(예: `<details open>` 유사 패턴 또는 `data-*` 속성) 아니면 항상 펼침 기본값으로 할지 결정 필요.
  - 백엔드 full-text 검색은 `ContentText`(HTML에서 추출한 텍스트)를 트라이그램 인덱스로 검색함 — 접힌 내용도 검색에 포함되어야 하며 텍스트 추출이 누락되면 안 됨.
  - `OrphanFileCleanupJob`은 노트 HTML에서 `/uploads/file_{guid}` 부분 문자열을 스캔함 — 토글 내부에 들어간 파일/이미지 참조도 HTML에 그대로 남아야 함.
  - 자동 저장(내용 변경 1500ms 디바운스)이 존재 — "접기/펼치기" 상호작용이 저장을 유발해야 하는지(상태 영속) 여부를 명시적으로 결정할 것.
  - read-only 모드(아카이브 노트)에서도 접기/펼치기 자체는 동작해야 함 (내용 편집만 차단).
  - 다크 모드 지원 필수 (기존 `dark-mode` body 클래스 기반).

# 작성해야 할 항목
1. 요구사항 정의 (기능/비기능 요구사항을 구분; 3가지 블록 유형별로 구분하여 작성)
2. 사용자 시나리오 / 유스케이스 (삽입, 키보드 조작, 중첩, 복사/붙여넣기, read-only 열람 포함)
3. 입력·출력 명세 (각 블록의 HTML 직렬화 구조, Tiptap 노드 스키마 — attrs, content expression, 그룹/토글 간 부모-자식 규칙)
4. 처리 흐름 (노드 생성/접기·펼치기/아코디언 상호 배타 로직, 필요 시 의사코드)
5. 인터페이스 설계 (`tiptap-editor.js`에 추가될 노드/확장 정의 초안, 슬래시 커맨드 항목, NodeView 구조; 백엔드 변경이 필요하면 해당 시그니처)
6. 예외 및 엣지 케이스 처리 (빈 토글, 토글 안의 토글 중첩 깊이, 토글 내부 코드블록/테이블/이미지, 헤딩 토글과 일반 헤딩의 변환, 아코디언 해체 시 동작, 마크다운 붙여넣기 변환)
7. 테스트 시나리오 (정상/경계/실패 케이스; 검색·파일 정리 작업과의 상호작용 검증 포함)
8. 구현 시 고려사항 / 미결정 사항(open questions) — 특히: 접힘 상태 영속 여부, 토글 헤딩을 별도 노드로 할지 기존 heading 노드 확장으로 할지, 아코디언을 1단계에서 제외하고 후속 단계로 분리할지에 대한 권고안 포함

# 산출물 형식
- **영어** 마크다운 문서로 작성 (코드베이스 규칙: 문서·코드 모두 영어)
- 파일 위치: `docs/plannings/note-toggle-feature.md` (기존 기획 문서 컨벤션과 동일)
- 각 항목에 명확한 헤딩 부여
- 구현 단계에서 참조하기 쉽도록 데이터 구조(HTML 직렬화 예시, Tiptap 노드 스키마)와 인터페이스는 코드 블록으로 표현
- 기능이 3가지이므로, 구현 순서(단계별 마일스톤) 제안을 문서 말미에 포함
