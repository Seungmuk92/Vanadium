# Product Review Prompt (완성본)

당신은 10년차 시니어 UX 리서처이자 소프트웨어 아키텍트입니다.
아래 제품을 냉정하게 평가해주세요. 칭찬보다 문제점 발견이 목적입니다.

## 제품 개요

- **제품명/한 줄 설명**: Vanadium — 개발자를 위한 Notion 스타일 셀프호스팅 개인 노트 앱 (단일 사용자, 비밀번호 하나로 운영)
- **타깃 사용자**: 자기 서버(Docker)에 직접 배포해 쓰는 개인 개발자 1인. 마크다운/코드/다이어그램 중심으로 기록하며, 키보드 위주 워크플로우(Ctrl+K 팔레트, 슬래시 커맨드)를 선호함
- **핵심 기능 3가지**:
  1. **Tiptap 기반 리치 텍스트 편집기** — 커스텀 노드(Callout, Mermaid 다이어그램, PageLink/NoteMention, 파일 첨부, 접이식 Toggle/Accordion), 슬래시 커맨드, 1.5초 디바운스 자동 저장
  2. **노트 라이프사이클 관리** — 계층형 노트(부모/자식) + 라벨(카테고리 내 상호배타) + 칸반 보드 뷰 + 아카이브(읽기 전용, 영구 보존) + 휴지통(soft delete, 30일 후 자동 영구삭제)
  3. **검색·공유** — PostgreSQL trigram(GIN) 전문 검색, Ctrl+K 퀵 네비게이션(최근 노트 + 라이브 검색), 추측 불가 토큰 기반 익명 노트 공유 링크
- **기술 스택**: .NET 10 — ASP.NET Core Web API(JWT 인증, EF Core + PostgreSQL) + Blazor WebAssembly(MudBlazor, Tiptap JS interop). 배포는 Docker Compose(nginx가 WASM 정적 파일 서빙), 로깅 Serilog(+선택적 Seq)

## 첨부 자료

### 사용자 플로우

1. **로그인**: `/login`에서 비밀번호만 입력(계정/아이디 없음) → PBKDF2-SHA256(600k iterations) 해시 검증 → JWT 발급(기본 24시간, 리프레시 토큰 없음) → `localStorage` 저장
2. **노트 작성**: Home(`/`) 또는 Ctrl+K → `/editor/{id}` → Tiptap 편집, 슬래시 커맨드로 커스텀 블록 삽입, 마지막 입력 1.5초 후 자동 저장(낙관적 동시성 체크)
3. **파일/이미지 업로드**: 에디터에서 업로드 → 첨부는 `/api/files/{guid}`(DB 메타데이터 있음), 이미지는 `/api/images/{guid}`(DB 레코드 없음) → 백그라운드 잡이 노트 HTML에서 참조가 사라진 고아 파일 주기 삭제
4. **정리**: 노트 → 아카이브(읽기 전용) 또는 휴지통(soft delete) → 30일 후 자동 영구삭제, 영구삭제 시점에 파일 참조 정리
5. **공유**: 에디터의 Share 버튼 → 토큰 생성 → 익명 사용자가 `/share/{token}`에서 읽기 전용 열람(단, 노트에 포함된 이미지/첨부는 인증 엔드포인트라 익명 뷰어에게 렌더링되지 않는 알려진 제약 존재)

### 코드 구조

- 백엔드 `Vanadium.Note.REST`: Controllers(Auth/Notes/Labels/Files/Images/Share/ApiTokens/Settings), Services(NoteService, FileCleanupService, HtmlSanitizerService, 백그라운드 잡 2종), 미들웨어 파이프라인(보안 헤더 → CorrelationId → CORS → RateLimiter → 인증 → 요청 로깅)
- 프론트엔드 `Vanadium.Note.Web`: Home/Board/NoteEditor/Archive/RecycleBin/Login/Share 페이지, Tiptap interop은 `wwwroot/js/tiptap-editor.js` 단일 진입점
- 테스트: xUnit + EF Core SQLite in-memory로 `NoteService` 수준만 커버. Web 프로젝트 테스트 없음, CI 없음
- 알려진 설계 결정: DTO는 REST/Web 양쪽에 의도적 중복, JWT issuer/audience 미검증(단일 테넌트), `appsettings.Development.json`에 실 dev DB 접속정보 커밋(의도적), 로그인 rate limit 10 req/min

## 평가 요청

1. UX/사용성: 주요 화면별로 사용자가 막힐 만한 지점을 찾고,
   심각도(치명/높음/중간/낮음)로 분류해주세요.
2. 기술 품질: 구조, 성능, 보안, 유지보수성 관점에서
   리스크를 지적해주세요.
3. 각 문제마다 "왜 문제인지 + 구체적 개선안"을 함께 제시해주세요.
4. 마지막에 항목별 10점 만점 점수와 출시 전 반드시 고칠
   상위 3가지를 정리해주세요.
