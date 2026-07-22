# Vanadium 제품 리뷰 — 2026-07-21

> 평가자 관점: 시니어 UX 리서처 + 소프트웨어 아키텍트. 목적은 칭찬이 아니라 결함 발견.
> 근거: 2026-07-21 시점 실제 소스 정독 (`Vanadium.Note.REST`, `Vanadium.Note.Web`, `wwwroot/js/tiptap`, docker-compose, nginx.conf). 모든 항목에 `파일:라인` 증거 표기.
> 심각도: 치명 / 높음 / 중간 / 낮음 — **단일 사용자 셀프호스팅** 기준으로 보정 (데이터 유실·무음 실패가 최악, 멀티테넌트류 우려는 감점하지 않음).

---

## 0. 한 줄 총평

코어(동시성, 라이프사이클, 드래프트 복구, 업로드 검증)는 개인 프로젝트 수준을 훌쩍 넘는다. 7/14 리뷰에서 지적된 항목 다수가 실제로 고쳐졌다(CSP 도입, 로그인 락아웃, XFF configurator, 공유 페이지 placeholder/Mermaid 렌더, SubNoteDialog 드래프트, WebP 매직바이트, CI 워크플로우 추가). **남은 문제의 무게중심은 세 곳이다: (1) 에디터의 "같은 라우트 이동" 데이터 유실 클러스터, (2) 파괴적 엔드포인트·기본 배포 설정의 보안 경계, (3) 한글 IME 미처리 — 한국어 사용자가 만든 앱인데 한글 입력 엣지가 무방비다.**

참고 — 제품 개요 문서와 코드의 드리프트: JWT 수명은 "24시간"이 아니라 **480분(8h)** 이 기본이고(`appsettings.json:7`), "CI 없음"이라 했지만 `.github/workflows/ci.yml`이 존재한다. 리뷰 요청서 자체가 구버전 스펙을 서술하고 있다는 것 — 문서 관리도 제품의 일부다.

---

## 1. UX / 사용성 (화면별)

### 1.1 에디터 `/editor/{id}` — 앱의 심장, 그리고 가장 아픈 곳

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **높음** | **에디터→에디터(같은 라우트) 이동 시 미저장 편집이 플러시 없이 소멸.** `OnParametersSetAsync`(NoteEditor.razor:325-337)가 `_currentId != Id`면 상태를 즉시 리셋 — `HasPendingChanges` 플러시도 `_autoSave.Cancel()`도 없음. `DisposeAsync`(:1070)의 플러시는 다른 라우트로 나갈 때만 실행되고, 백링크 링크(:209)·브레드크럼 부모 링크(:30)는 같은 컴포넌트를 재사용한다 | 노트 A에서 타이핑 직후(≤1.5s) 백링크를 클릭하면 경고 없이 편집분이 사라진다. 부수 버그 2건: (a) 리셋 후 발화하는 잔존 디바운스 타이머가 빈 content로 B에 PUT → 가짜 409 배너, (b) A의 in-flight 저장이 B 로드 후 완료되면 `_serverUpdatedAt`(:527-567)에 A의 타임스탬프가 덮여 B의 다음 저장이 409 | 리셋 전에 이전 Id 문맥으로 `_autoSave.Cancel(); if (HasPendingChanges) await SaveCoreAsync();` 실행. in-flight 완료 콜백에 "저장 시작 시점 Id == 현재 Id" 가드 |
| **중간** | **충돌(409) 상태에서 계속 타이핑한 내용은 저장도 드래프트도 안 됨** — `ScheduleAutoSave`(:507-513)가 `_hasConflict`면 조기 리턴, `DisposeAsync`도 충돌 시 저장 스킵. "Reload Latest"(:707-721)는 백업 없이 로컬 텍스트를 덮어씀. SubNoteDialog는 같은 상황에서 드래프트를 스태시한다(:346-349) — 비일관 | 두 탭 시나리오에서 배너를 보고도 마저 쓴 텍스트는 Force Save 외 복구 불가. Reload 오클릭 한 번이면 전부 증발 | 충돌 상태에서도 `DraftStore.SaveAsync` 지속 갱신(SubNoteDialog 패턴), Reload 진입 시 드래프트 백업 |
| **중간** | **DraftStore가 단일 슬롯** — 키는 `vanadium.editor-draft.v1` 하나(DraftStore.cs:15), 저장 성공 시 소유자 확인 없이 무조건 `ClearAsync()`(NoteEditor.razor:569) | A 편집 중 401 → 드래프트 스태시 → 재로그인 후 B를 먼저 열어 저장 → **A의 드래프트가 소리 없이 삭제**. 마지막 방어선이 자기들끼리 충돌 | 키를 `…draft.{noteId}`로, 또는 `ClearAsync(noteId)`로 소유자 일치 시에만 삭제 |
| **중간** | **Ctrl+S가 저장 후 에디터를 떠나 목록으로 이동** — `RegisterAsync("ctrl+s", …, SaveNote)`(:458) → `SaveNote()` 끝에 무조건 `Nav.NavigateTo(BackUrl)`(:668). SubNoteDialog의 Ctrl+S는 머무른다(flush-and-stay) — 같은 앱에서 단축키 의미가 화면마다 다름 | 키보드 중심 사용자는 습관적으로 Ctrl+S를 누른다. 그때마다 흐름이 끊긴다 | Ctrl+S는 flush-and-stay로, "Save & close"는 별도 버튼으로 |
| **중간** | `/page` 슬래시 커맨드가 새(미저장) 노트에서 무반응 — `if (!Id.HasValue) return null`(:828-831) + slash-commands.js:151-153이 조용히 리턴. `/page` 텍스트는 지워지고 아무 일도 없음 | 새 노트 만들자마자 하위 페이지를 만드는 흔한 흐름에서 명령이 먹은 것처럼 사라지고 피드백 0 | 스낵바 안내 또는 첫 자동저장 강제 플러시 후 생성 |
| 낮음 | 드래프트 복원이 타임스탬프 비교 없이 무조건 적용(:400-407) — 다기기 사용 시 구본 드래프트가 최신 서버본을 덮을 수 있음 | 단일 기기에선 무해. 서버 `UpdatedAt`이 더 최신이면 선택지 배너 제공 | |
| [OK] | **JWT 만료 중 타이핑 시나리오는 모범적**: 401 → 토큰 클리어+returnUrl 리다이렉트(AuthTokenHandler.cs:22-27) → sessionStorage 드래프트 스태시(:559-565) → 재로그인 복원 + 스낵바(:398-408). 가짜 409를 막는 in-flight 저장 병합(:519-534)도 견고 | | |

### 1.2 홈 `/`

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **높음** | **Settings의 "Notes List" 설정(정렬·페이지 크기)이 어디에도 적용되지 않음.** Settings.razor:40-83은 `DefaultSortBy/DefaultSortDir/DefaultPageSize`를 편집·저장하지만, Home.razor는 하드코딩(`private const int PageSize = 30`, `currentSort = SortColumn.Date` — :274-280). `SettingsService` 소비처는 MainLayout(테마)뿐 | "50개씩 보기"로 바꾸고 저장 성공 스낵바까지 봤는데 아무것도 안 변한다. 조용한 no-op 설정은 설정 화면 전체의 신뢰를 무너뜨린다 | Home 초기화 시 `SettingsService.GetAsync()` 주입 — 그 전까진 해당 섹션을 Settings에서 제거하는 게 차라리 정직 |
| **중간** | **Ctrl+N은 Chromium 예약 단축키라 절대 동작하지 않는데**(브라우저가 웹 콘텐츠에 전달 안 함) Ctrl+/ 도움말에 "New note"로 버젓이 표시됨(MainLayout.razor:93) | 사용자는 앱 버그로 인식. 새 브라우저 창만 뜬다 | `alt+n` 등 비예약 키로 교체 |
| 낮음 | 검색/필터/페이지 변경마다 트리 확장 상태 전부 리셋(`expandedNoteIds.Clear()` — Home.razor:356-357) | 탐색 맥락 상실 | 결과에 존재하는 노트의 확장 상태는 보존 |

### 1.3 보드 `/board`

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **중간** | 카드 이동이 remove→add 2단계(Board.razor:282-286) — remove 성공 후 add 실패 시 노트가 라벨을 잃고 **어느 컬럼에서도 사라짐**. 서버가 카테고리 내 상호배제를 강제하므로 add 한 번이면 원자적으로 끝난다 | 자체 서버 규칙(상호배타 라벨)을 클라이언트가 활용하지 않아 불필요한 실패 모드를 만듦 | remove 호출 삭제, add 단일 호출 |
| **중간** | 선택 카테고리 라벨이 없는 노트는 보드에서 완전 비가시 — "미분류" 컬럼도 카운트 힌트도 없음(:308-311) | 위 실패와 결합하면 "노트가 사라졌다"로 체감 | "No label" 가상 컬럼 또는 미분류 N개 힌트 |
| **중간** | HTML5 drag&drop만 등록(board-drag-drop.js:106-112) — 터치에서 카드 이동 불가 | 모바일에서 보드는 읽기 전용이 됨 | 터치 폴백으로 "라벨 이동" 컨텍스트 메뉴(라이브러리보다 저비용) |

### 1.4 공유 뷰어 `/share/{token}`

7/14 대비 크게 개선됨: 깨진 이미지 placeholder 치환·첨부 링크 무력화(interop.js:438-468), Mermaid 렌더(:403-428), 소유자용 사전 고지(ShareDialog.razor:47-50) 모두 확인.

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **중간** | **접힌 Toggle/Accordion을 익명 뷰어가 펼칠 수 없음** — 공유 콘텐츠는 `(MarkupString)` 원시 덤프(Share.razor:30)라 Tiptap 노드뷰(토글 화살표 버튼, toggle.js:74-96)가 없음. `data-open="false"`인 본문은 영원히 읽을 수 없거나, CSS 스코프에 따라 화살표 없이 전부 펼쳐짐 | 공유받은 사람이 문서 일부를 아예 못 읽는다 | 공유 페이지 전용 소형 JS로 `[data-type=toggle]` 클릭 토글 부착, 또는 공유 응답에서 `data-open="true"` 정규화 |
| 낮음 | 코드 블록 구문 강조 없음(lowlight는 에디터 런타임 데코레이션) — 무강조 `<pre>` | 개발자 대상 공유에서 체감 큼 | 공유 페이지에서만 highlight.js 지연 로드 |
| 낮음 | 5xx/네트워크 실패와 revoke된 토큰이 같은 "This note isn't available" 화면(:14-19) — `IsNotFound` 구분값을 받고도 UI에서 합침. 재시도 버튼 없음 | 일시 장애를 영구 소멸로 오인 | 상태 분리 + Retry |

### 1.5 Quick Nav (Ctrl+K)

디바운스 250ms·최소 2자·상태 분기·화살표 순환·라우트 변경 시 강제 닫기 — 모범적.

| 심각도 | 문제 | 개선안 |
|---|---|---|
| **중간** | `aria-modal="true"` 선언(QuickNavDialog.razor:12-16)에도 포커스 트랩 미적용 — Tab이 배경으로 탈출. `focus-trap.js` 인프라는 이미 있고 Board만 사용 | 열릴 때 `focusTrap.activate` 한 줄 |
| 낮음 | Enter가 항상 peek 다이얼로그(의도적, :249-259) — 전체 페이지로 바로 가는 키가 없어 "열기"에 항상 2단계 | `Shift+Enter` = 전체 페이지 바이패스 |

### 1.6 입력·단축키·IME — 한국어 사용자 핵심 이슈

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **높음** | **IME(한글) 조합 처리가 전무** — `wwwroot/js` 전체에 `isComposing`/`compositionstart`/`keyCode 229` 검사 0건. mention.js:132-136과 slash-commands.js:134-137의 keydown 핸들러가 조합 확정 Enter를 항목 선택으로 오소비 | `@` 뒤에 한글 이름을 조합하다 조합 확정 Enter를 누르면 원치 않는 멘션 삽입·글자 유실/중복이 발생하는 고전 패턴. 슬래시/멘션이 핵심 워크플로우인 앱에서 매일 밟는 지뢰. 부가로 조합 중간 음절("ㅅ","새","색")로 검색을 난사 | 두 `onKeyDown` 첫 줄에 `if (event.isComposing || event.keyCode === 229) return false;` — 두 줄짜리 수정 |
| **중간** | 제목/검색/팔레트 입력이 `value="@field"` + `@oninput` 패턴(NoteEditor.razor:98-104, Home.razor:96-100, QuickNavDialog.razor:17-25) — Blazor 재렌더가 조합 중 input value를 재설정하면 한글 조합이 끊기는 알려진 계열 이슈 | 코드만으로 단정 불가 — **한글 타이핑 수동 검증 필수**. 재현 시 JS 미러 입력으로 교체 | |
| 낮음 | Ctrl+K 의미가 문맥마다 다름(에디터 본문=링크 팝오버 interop.js:121-130, 그 외=팔레트) — 도움말엔 팔레트만 표기 | 링크는 Ctrl+Shift+K로 분리 권장 | |
| 낮음 | 슬래시 커맨드 목록·에디터 내부 키(Mod-Enter 접기, Tab 들여쓰기)가 Ctrl+/ 도움말에 없음 | 커스텀 블록 발견성 저하 | 도움말에 에디터 섹션 추가 |

### 1.7 기타 화면

- **Login**: 에러 구분(오답/429/네트워크/5xx)·로딩·Enter 제출·returnUrl 보존·패스워드 매니저 호환까지 양호. [낮음] 비밀번호 입력 autofocus 없음. [낮음] `returnUrl` 미검증 오픈 리다이렉트(Login.razor:63-65) — `/`로 시작·`//` 배제 검증 한 줄이면 닫힘.
- **Archive/RecycleBin**: 벌크 처리·부분 실패 리포트 견고. [낮음] Archive의 삭제만 Undo가 없어 Home/Editor와 비일관(Archive.razor:182). [낮음] 벌크가 순차 HTTP 루프 + 진행 표시 없음.
- **Settings**: [낮음] `navigator.clipboard.writeText`(Settings.razor:277, ShareDialog.razor:127)가 비보안 컨텍스트(http 배포)에서 undefined → 미처리 JSException. try/catch + 수동 복사 폴백.
- **에러 표면화 전반**: `ServiceResult` → 스낵바 일관 적용, Home·에디터는 무한 스피너 대신 Retry 배너 — 우수. [낮음] 백링크 로드 실패 시 "No other notes link here yet."라는 **틀린 빈 상태** 표시(NoteEditor.razor:428).

---

## 2. 기술 품질

### 2.1 보안

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **높음** | **`DELETE /api/settings/all-data`가 PAT로도 호출 가능하고 재인증이 없음.** SettingsController.cs:8의 `[Authorize]`는 Smart 스킴(JWT 또는 PAT). ApiTokensController는 PAT 오남용을 막으려 JWT 전용으로 제한(:14)해 놓고, 정작 가장 파괴적인 엔드포인트는 열어뒀다 | CI 등에 심어둔 PAT 하나가 유출되면 요청 한 방으로 전 데이터 비가역 삭제. 백업 스토리도 없어(docker-compose에 uploads 볼륨만) 복구 불가 | JWT 전용 제한 + 요청 본문에 비밀번호 재확인. 삭제 전 dump 생성 옵션 |
| **높음** | **컴포즈 기본 배포가 평문 HTTP 직노출** — `ports: "5000:8080"`(docker-compose.yml:5-6), `API_BASE_URL` 기본 `http://localhost:5000`(:33). ForwardedHeaders 신뢰 설정은 주석 처리된 예시뿐(:15-19) → 프록시를 세워도 per-IP 로그인 리미터가 단일 버킷으로 퇴화 | 문서상 "HTTPS는 프록시 책임"인데 기본 컴포즈엔 프록시가 없다 — JWT·비밀번호가 평문 전송. 프록시를 붙이면 이번엔 리미터가 죽는다. 어느 쪽으로도 기본값이 안전하지 않음 | REST 포트는 `expose`로 내리고 TLS 종단 프록시를 컴포즈에 포함, XFF 신뢰 서브넷 기본 활성화. 최소한 기동 로그에 경고 |
| **중간** | **전역 로그인 락아웃이 소유자 로그인 DoS 벡터** — 락 중이면 비밀번호 검증 없이 429(AuthController.cs:40-48), 락 만료 후 실패 1회마다 지수 갱신(캡 900s) | 인터넷 노출 인스턴스에서 공격자가 15분마다 오답 1개만 보내면 소유자는 올바른 비밀번호로도 영구 로그인 불가 | 락 중에도 검증은 수행 — 성공이면 해제+통과, 실패만 429 (PAT 스로틀도 동일 패턴: ApiTokenAuthHandler.cs:41-45의 주석과 동작 불일치 함께 수정) |
| **중간** | **공유 노트가 비공유 노트의 제목·GUID를 유출** — page-link/mention 마크업(`data-note-id`, `data-title`, 표시 텍스트)이 공유 응답에 그대로 포함(page-link.js:25-33, ShareController.cs:36-42) | 부모 노트 하나 공유하면 참조된 개인 노트 제목이 익명 뷰어에게 전부 보임 | 공유 응답 조립 시 해당 속성 제거 + "🔒 private page" 치환 |
| **중간** | **CSP `script-src`가 esm.sh/jsdelivr 전체 허용**(nginx.conf:24) + `'unsafe-eval'` — esm.sh는 임의 npm 패키지를 서빙하므로 sanitizer를 우회한 `<script src="https://esm.sh/…">`는 CSP를 통과. "2차 방어선"이 사실상 sanitizer 단일 계층으로 수렴 | 특히 익명 공유 페이지가 같은 CSP를 씀 | 에디터 의존성 self-host 번들 → `script-src 'self' 'wasm-unsafe-eval'`로 축소(아래 CDN 항목과 동일 해법). 최소한 `/share/*`에 별도 엄격 CSP |
| **중간** | **공유 경로 재-sanitize 없음** — sanitize는 Create/Update 시점뿐(NoteService.cs:311, 354), 공유는 `MarkupString` 직주입(Share.razor:30). sanitizer 도입 이전 저장분 백필 여부 미확인 | 레거시 행에 스크립트가 남아 있다면 익명 뷰어에게 그대로 서빙되고 위 CSP 구멍과 결합해 실행 가능 | `GetSharedByToken` 반환 직전 sanitize 1회(싱글턴, 비용 미미) + 일회성 백필 |
| **높음(방침 재고)** | 실 자격증명 커밋 — `appsettings.Development.json:3,6,8`의 DB 접속정보는 루프백이 아니라 **외부 도달 가능한 실 도메인**(`pg.smoh.kr:55432`)이다. "의도적 커밋" 방침은 알고 있으나, throwaway가 아닌 실 DB 비밀번호라는 점에서 방침의 전제가 성립하지 않음 | repo 접근 = dev DB 접근. 리포 공개·유출 시 즉시 침해 | 이 파일에 한해 방침 폐기: dev DB를 사설망으로 한정하고 비밀번호 로테이트 |
| 낮음 | `/login?returnUrl=` 오픈 리다이렉트(위 1.7), 운영 환경 비밀번호 변경 경로 부재(`/api/auth/hash`는 dev 전용 — AuthController.cs:80-83) | | CLI 툴 또는 현재 비밀번호 검증 조건부 허용 |
| [OK] | PBKDF2 600k+고정시간 비교, JWT 시크릿 기동 검증, LIKE 이스케이프 전 경로 적용, 공유 토큰 위조 불가(Create/Update가 공유 필드 미노출), GUID 파일명으로 경로 탐색 차단, Mermaid `securityLevel:'strict'`, 멘션 메뉴 textContent 렌더 — 모두 검증 완료 | | |

### 2.2 정확성 / 데이터 무결성

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **높음** | **JWT payload를 표준 base64로 디코드**(JwtAuthenticationStateProvider.cs:62 `Convert.FromBase64String`) — JWT는 base64**url**. `-`/`_` 포함 토큰(exp/iat 값 따라 무작위 발생)이면 FormatException → 빈 클레임. authenticationType은 있어서 로그인은 되는데 username이 null → **Settings/Logout 버튼이 간헐적으로 사라짐**(NavMenu.razor:51-65) | "어떤 날은 로그아웃 버튼이 없다" 류의 재현 불가 버그의 정체. 발생 여부가 토큰 바이트에 달려 있어 디버깅 지옥 | `-`→`+`, `_`→`/` 치환 후 디코드. 겸사겸사 `exp` 검사로 만료 토큰 플래시 로그인도 제거 |
| **높음** | **제목 변경 전파가 다른 노트를 동시성 검사 없이 덮어씀** — `UpdatePageLinkReferences`(NoteService.cs:420-443)가 참조 노트들을 로드·치환·저장하는데 `UpdatedAt` 갱신도 충돌 검사도 없음 | 제목 하나 바꿨을 뿐인데 다른 탭에서 편집 중이던 노트의 최근 편집이 조용히 유실될 수 있고, `UpdatedAt`을 안 바꿔 그 노트의 다음 저장이 거짓 409를 볼 수도 있음. 본 노트 저장 경로의 견고한 동시성과 극명한 대비 | 전파 대상도 마이크로초 `UpdatedAt` 갱신 + 충돌 시 스킵/재시도. 근본책은 참조 정규화 테이블(#141)로 콘텐츠 재작성 자체를 제거 |
| **중간** | **제목의 `$`가 정규식 치환에서 특수 해석됨** — `Regex.Replace(tag, @">.*?</div>$", $">📄 {encodedTitle}</div>")`(NoteService.cs:486, :456·:485도 동일). `HtmlEncode`는 `$`를 인코딩하지 않으므로 제목에 `$1`이 있으면 치환 그룹으로 해석되어 깨진 마크업 삽입 | "Cost is $100" 같은 제목을 참조하는 모든 노트의 링크가 손상 | `encodedTitle.Replace("$", "$$")` 또는 델리게이트 치환 |
| **중간** | **매직바이트 검증이 partial read에 취약** — `stream.ReadAsync(buffer)`(FilesController.cs:132-138, ImagesController.cs:68-71)는 요청 바이트를 보장하지 않음. 첫 read가 짧으면 정상 파일도 거부(WebP는 12바이트 필요) | 청크 경계에 따라 정상 업로드가 간헐 실패 — 재현 안 되는 업로드 오류 | `ReadAtLeastAsync(buffer, 12, throwOnEndOfStream:false)` |
| **중간** | **고아 정리 grace가 "업로드 시각" 기준**(FileCleanupService.cs:89, :132 — 60분) — 참조는 커밋된 노트 콘텐츠만 봄 | 이미지를 붙여넣고 60분 넘게 초안 상태(자동저장 실패·오프라인·방치)면 스캔이 자산을 삭제 → 이후 저장 시 깨진 이미지. 통상 1.5s 자동저장에선 안전하나 엣지가 정확히 "저장이 실패하고 있던" 최악의 순간과 겹침 | 미참조 실패 누적 기준 GC 또는 draft/pending 플래그 |
| 낮음 | Archive/RecycleBin 그룹 판정이 `ArchivedAt`/`DeletedAt` 타임스탬프 동일성(NoteService.cs:856-858) — 마이크로초 충돌 시 그룹 오분류(단일 사용자에선 사실상 발생 불가) | 전용 GroupId 컬럼이 정석 | |
| [OK] | 낙관적 동시성(OriginalValue 핀 + DB WHERE절 판정 + 명시적 `?force=true`만 우회 — 7/14 지적 "0-버전 무음 우회"는 수정 확인), 하드삭제 트랜잭션+파일정리 분리, 복원 시 부모 유효성 검사 — 검증 완료 | | |

### 2.3 성능

| 심각도 | 문제 | 개선안 |
|---|---|---|
| **중간** | **키 입력마다 `editor.getHTML()` 전체 직렬화 + WASM 경계 마샬링**(interop.js:114-117) — 디바운스는 .NET 쪽(1.5s)에만 있고 직렬화 자체는 매 키스트로크. 수백 KB 노트면 키 입력당 수백 KB 복사 | JS 쪽 300–500ms 디바운스 후 1회 전송, 또는 저장 시점에 .NET이 pull |
| **중간** | 고아 스캔이 파일마다 개별 `ILIKE '%guid%'` 쿼리(FileCleanupService.cs:83-127) — N+1 GC. 파일 수천 개면 24h 잡이 수천 쿼리 | 노트 콘텐츠에서 참조 GUID 집합을 1회 추출 → 디스크 집합과 차집합으로 역전 |
| 낮음 | 조상/자손 순회가 깊이당 DB 왕복(NoteService.cs:496-509, 1017-1032) — 재귀 CTE로 단일화 가능. 리스트 페이징 COUNT 이중 실행 — 통상적 트레이드오프 | 기록용 |
| [OK] | 검색은 `ContentText` GIN trigram + LIKE 이스케이프 + 페이지 clamp, Home 30개 페이지네이션 + 자식 lazy 로드, Board 컬럼당 50 캡 — 규모 대비 적절 | |

### 2.4 구조 / 유지보수성

| 심각도 | 문제 | 개선안 |
|---|---|---|
| **높음** | **정규식 기반 HTML 재작성이 콘텐츠 무결성의 단일 실패점** — 제목 전파·참조 strip·page-link 제거 전부 `Regex.Replace`(NoteService.cs:445-490, 1054-1063). `$` 버그(위)가 이 취약성의 실례이고, 중첩 구조·속성 순서 변화에 조용히 오작동 | #141 참조 정규화 테이블로 이행 시 재작성 자체가 소멸. 과도기엔 AngleSharp DOM 조작 |
| **중간** | **에디터 스택 전체가 런타임 CDN 의존**(interop.js:1-13 esm.sh, mermaid.js jsdelivr, 버전 핀도 `@2` 메이저 범위) — 셀프호스팅 앱인데 CDN이 죽으면 편집 불가, CSP 구멍(위)을 강제하며, CDN 쪽 마이너 업데이트로 어느 날 조용히 깨질 수 있음 | 번들 self-host — 보안·가용성·CSP 세 문제를 한 번에 해결. **백엔드 다음으로 투자 대비 효과가 가장 큰 단일 작업** |
| **중간** | Web 프로젝트 테스트 0 — 위에서 발견된 프론트 버그 전부(base64url, 같은 라우트 유실, 설정 no-op, IME)가 테스트 사각지대. 백엔드 테스트는 SQLite 폴백이라 trigram·rate limiter 실경로 미커버 | bUnit 스모크 + Playwright 시나리오 3개(만료 중 저장, 두 탭 충돌, 한글 멘션)면 최악은 잡는다 |
| 낮음 | `NoteService` 48KB 단일 클래스(CRUD·검색·공유·라이프사이클·HTML재작성), link-popover의 document 리스너 미해제 누수(link-popover.js:61-66 — destroy에서 remove 안 함), `_inputSafe` 단축키 목록 JS 하드코딩 이중 소스(keyboard-shortcuts.js:8), DTO 중복의 실비용 흔적(SubNoteDialog.razor:247 "every save 409s" 주석) | Lifecycle/Share/Rewriter 분리, removeEventListener, register API에 inputSafe 플래그 |
| [OK] | 미들웨어 순서 정확, 백그라운드 잡 격리, ProblemDetails 일관, stale 응답 레이스 방어(취소 토큰·Id 가드) 일관 적용, 크로스탭 토큰 동기화 정확 — 검증 완료 | |

---

## 3. 항목별 점수 (10점 만점)

| 항목 | 점수 | 근거 |
|---|---:|---|
| UX / 사용성 | **6.5** | 세션 만료·충돌·에러 상태의 방어 UX는 상위 수준. 그러나 같은 라우트 유실·no-op 설정·죽은 Ctrl+N·IME 무방비 등 "코드는 옳은데 사용자를 배신하는" 지점이 신뢰를 깎음 |
| 보안 | **6.5** | 7/14 대비 실질 개선(CSP·락아웃·XFF·매직바이트). 남은 것은 경계 설계: PAT로 전체 삭제, 평문 기본 배포, CSP의 CDN 구멍, 락아웃 DoS. 기본기가 아니라 "기본값"이 문제 |
| 성능 | **7.0** | 검색·목록은 인덱스 활용 우수. 키스트로크 직렬화와 N+1 GC가 규모 성장 시 첫 병목 |
| 정확성/무결성 | **6.0** | 저장 경로 동시성은 모범적이나, 그 옆의 전파 경로가 무방비이고 base64url·`$` 치환·partial read 같은 "간헐 재현" 버그가 셋 이상 |
| 유지보수성 | **6.5** | 코드 가독성·주석·이슈 추적 연동은 좋음. 프론트 테스트 0 + 정규식 HTML 재작성 + CDN 런타임 의존이 구조적 부채 |
| **종합** | **6.5** | "잘 만든 개인 도구"는 이미 넘었다. "안심하고 남에게 권할 수 있는 셀프호스팅 제품" 사이에 남은 벽은 기본값 안전성과 엣지 유실 |

---

## 4. 출시 전 반드시 고칠 상위 3가지

1. **에디터 데이터 유실 클러스터 (같은 라우트 이동 + 충돌 중 타이핑 + 단일 슬롯 드래프트).**
   이 앱의 핵심 약속은 "쓰면 저장된다"이다. 백링크 클릭 한 번, Reload 오클릭 한 번, 재로그인 후 다른 노트 저장 한 번 — 각각이 무경고 유실 경로다. 세 수정 모두 기존 인프라(DisposeAsync 플러시, DraftStore, SubNoteDialog 패턴)의 재사용이라 비용이 작다. `OnParametersSetAsync` 플러시 + 충돌 중 드래프트 갱신 + noteId 스코프 드래프트 키.

2. **파괴 반경 축소: `all-data` 엔드포인트 + 기본 배포 설정.**
   `DELETE /api/settings/all-data`를 JWT 전용 + 비밀번호 재확인으로 잠그고, 컴포즈 기본값을 "REST 포트 비노출 + TLS 프록시 포함 + XFF 신뢰 설정"으로 뒤집어라. 지금은 PAT 유출 하나 또는 기본 설정 그대로의 배포가 곧 전체 데이터 위험이고, 백업 스토리가 없어 복구도 불가하다.

3. **한글 IME 가드 (멘션·슬래시·전역 단축키).**
   `event.isComposing` 체크 두 줄이 없어서, 한국어 사용자의 가장 빈번한 입력 흐름(@멘션에 한글 제목, / 뒤 한글)이 매번 오동작 위험에 노출된다. 타깃 사용자가 본인이라는 점에서 역설적으로 가장 빨리 체감할 결함. 수정 후 제목 입력·검색창의 조합 끊김도 수동 검증할 것.

> **차순위 강력 권장** — 에디터 의존성 self-host 번들링. CDN 가용성, 공급망, CSP 구멍(`script-src`의 esm.sh 허용), `@2` 범위 핀의 조용한 파손 — 네 가지 리스크를 단일 작업으로 제거하며, "셀프호스팅" 철학과의 모순도 해소된다. 그 다음은 base64url 디코드 수정(1줄)과 Settings no-op 해소(소비 코드 연결)이 체감 대비 가장 싸다.
