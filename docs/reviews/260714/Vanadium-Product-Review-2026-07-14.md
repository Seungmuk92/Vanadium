# Vanadium 제품 리뷰 (냉정 평가판)

> 평가자 관점: 시니어 UX 리서처 + 소프트웨어 아키텍트
> 목적: 칭찬이 아니라 **출시 전 반드시 막아야 할 구멍 찾기**
> 근거: 실제 코드(`Vanadium.Note.REST`, `Vanadium.Note.Web`) 정독 기반. 각 항목에 `파일:라인` 증거 표기.
> 심각도: 치명(Critical) / 높음(High) / 중간(Medium) / 낮음(Low)

---

## 0. 한 줄 총평

핵심 기능은 놀랍도록 완성도 있게 짜여 있다. soft-delete/archive/orphan-cleanup 같은 라이프사이클 경계 조건, 낙관적 동시성, 트라이그램 검색, 저장 실패 시 draft 복구까지 "1인 개인 프로젝트"라고 보기 어려운 수준으로 방어적이다. **문제는 코드 품질이 아니라 두 가지 축이다: (1) 시크릿·인증 경계의 실전 리스크, (2) UI에 "보이지 않는" 기능들** — 코드가 옳게 동작해도 사용자가 발견하지 못하거나 실패를 인지하지 못하는 지점.

---

## 1. UX / 사용성 평가 (화면별)

### 1.1 로그인 (`/login`)

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **중간** | 비밀번호 필드에 `name`/`autocomplete="current-password"`/`<label>`/`id`가 전부 없음 (`Login.razor:13-19`, placeholder만 존재) | 비밀번호 매니저가 자격증명을 저장·자동입력하지 못한다. 단일 사용자·비밀번호 하나로만 도는 앱에서 매니저 호환 실패는 매일 겪는 마찰이 된다. 접근성 레이블도 없어 스크린리더가 필드를 읽지 못함 | `<input name="password" autocomplete="current-password" id="password">` + 숨김 username 필드 + `<label>` 추가 |
| 낮음 | 로그인 에러 피드백은 오히려 잘 되어 있음 — 오답/429/네트워크/서버오류 구분 (`Login.razor:54-60`, `AuthService.cs:21-26`) | — | [OK] |

### 1.2 노트 에디터 (`/editor/{id}`)

전반적으로 이 화면이 앱에서 가장 방어적이다. 409/403/로드실패/에디터초기화실패 전부 전용 배너가 있고(`NoteEditor.razor:56-95`), 탭 닫기·네비게이션 시 `beforeunload` 가드와 pending flush가 걸려 있으며(`unsaved-changes.js`, `DisposeAsync:1015`), 저장 실패 시 draft를 sessionStorage에 백업해 재로그인 후 복구한다(`:547, :391-401`). 여기까지는 훌륭하다. 그런데 결정적 구멍이 있다:

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **높음** | **`SubNoteDialog`(하위 노트 인라인 편집)에는 `beforeunload` 가드도 DraftStore도 없다.** 409 충돌 시 "다른 곳에서 수정됨 — 다시 열어라"만 뜨고(`SubNoteDialog.razor:244-248`), `DisposeAsync:330-331`가 다시 `SaveCoreAsync`를 호출해 또 409 → 편집분 소실 | 풀페이지 에디터는 데이터 손실을 막는데 **다이얼로그는 안 막는다.** 사용자는 둘의 차이를 모르므로 "가끔 하위 노트가 저장 안 된다"는 신뢰 붕괴로 이어짐. 1.5초 디바운스 창 안에서 다이얼로그 닫아도 소실 | 다이얼로그에도 동일한 `beforeunload` + draft 스택 적용, 또는 닫기 전 강제 flush |
| **중간** | 자동저장 실패가 작은 "Save failed" 라벨로만 표시(`DoAutoSave:566-571`) — 수동저장(`:623`)과 달리 **스낵바 없음** | 일시적 5xx나 리다이렉트 직전 401 상황에서 사용자가 계속 타이핑하는데 저장이 안 되고 있다는 걸 눈치채기 어렵다. draft 백업이 손실은 막지만 인지 실패는 못 막음 | 자동저장 실패도 수동저장과 동일하게 스낵바로 승격 |
| 중간 | 새 노트에 붙인 라벨을 `AddLabelToNoteAsync` 결과를 무시하고 저장(`NoteEditor.razor:575-576, 640-641`) | 라벨 POST 실패 시 UI엔 라벨이 보이는데 실제로는 저장 안 됨 → "라벨이 사라졌다" | 결과 확인 후 실패 시 토스트 + UI 롤백 |
| 낮음 | 빈/공백 제목 미정규화(`SaveOnceAsync:522-529`) — `Board.razor:334`는 trim하는데 여기선 안 함 | 목록에 공백 제목 노트가 남음. 일관성 결함 | 저장 전 `Trim()` + 빈 제목 fallback("Untitled") |

### 1.3 홈 (`/`) & 보드 (`/board`)

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **높음** | **일괄 삭제 트리거가 물리 `Delete` 키밖에 없다** (`Home.razor:269, 424`). 행별 체크박스와 전체선택은 렌더되는데(`:70-91`) "선택 삭제" 버튼/액션바가 없음 | 체크박스가 있으니 사용자는 뭔가 되리라 기대하지만 아무 버튼도 없어 "기능이 고장난 것"처럼 보인다. Delete 키를 눌러야 한다는 걸 알 방법이 없음 | 체크된 항목이 있으면 나타나는 액션바("N개 삭제") 추가 |
| 중간 | 계층이 한 단계만 인라인 확장. 손자 노트는 `+N` 뱃지로만 표시되고 인라인 확장 불가(`Home.razor:187-190`), 전체 트리뷰 없음 | 깊은 계층 탐색이 반복적으로 노트 안으로 들어가야만 가능. "개발자용 계층 노트"라는 핵심 가치가 얕게 구현됨 | 재귀 확장 또는 별도 트리 사이드바 |
| 중간 | 보드 추가노트도 라벨 결과 무시(`Board.ConfirmAddNote:338`) | 라벨 실패 시 노트가 어느 컬럼에도 안 들어가고 사라진 것처럼 보임 | 결과 확인 |
| 낮음 | 보드는 카테고리 전체 요약을 페이지네이션 없이 로드(`Board.razor:197`) | 개인용이라 당장은 무방하나 노트가 쌓이면 무한정 커짐 | 상한/가상 스크롤 |
| [OK] | 삭제 확인 + Undo, 빈/로딩/에러 상태는 홈·아카이브·휴지통 모두 잘 되어 있음 | — | — |

### 1.4 퀵 네비게이션 (Ctrl+K)

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| 중간 | 포커스 트랩 없음 + a11y 결함. `role="dialog" aria-modal`은 선언(`QuickNavDialog.razor:11-16`)했으나 Board와 달리 `focusTrap` 미적용 → Tab이 뒤 페이지로 샘. 결과 목록이 `role="listbox/option"`/`aria-activedescendant` 없어 화살표 선택이 스크린리더에 안 보임 | 키보드 우선 워크플로우가 핵심 가치인데 정작 팔레트의 키보드 접근성이 새고 있음 | Board의 `focus-trap.js` 재사용 + listbox ARIA |
| [OK] | 250ms 디바운스, 키스트로크별 취소, 최소 2자, 상태 분기, 화살표/Enter/Esc 래핑 모두 구현(`:194, 171-174, 221-247`) | — | — |

### 1.5 공유 (`/share/{token}`)

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **높음** | **공유 페이지가 Mermaid 다이어그램을 원본 코드로 노출** (`Share.razor:29`가 `(MarkupString)Content` 덤프, `renderMermaidIn` 호출 없음 — grep상 `.razor` 호출자 0) | "다이어그램 중심 기록"이 타깃인데 공유 링크를 받은 사람은 다이어그램 대신 날것의 `<pre>` 코드를 본다. 공유 기능의 첫인상을 망침 | 익명 페이지에서도 Mermaid 렌더 interop 호출 |
| 중간 | 이미지/첨부가 인증 엔드포인트라 익명 뷰어에게 브라우저 기본 깨진-이미지 아이콘으로 표시(`Share.razor`에 `onerror` fallback 없음). 알려진 제약이지만 시각적 처리는 없음 | 공유받은 사람에게 "고장난 페이지"로 보임 | 최소한 placeholder("이미지는 공유 뷰에서 볼 수 없습니다") onerror 처리; 근본책은 익명 asset 프록시(설계상 out of scope) |
| [OK] | ShareDialog는 명확 — 모드 라디오, 링크 복사 버튼+토스트, 이미지 미표시 경고 캡션까지(`ShareDialog.razor:38-50`). 만료/취소 토큰은 친절한 안내(`Share.razor:14-19`) | — | — |

### 1.6 아카이브 / 휴지통

| 심각도 | 문제 | 개선안 |
|---|---|---|
| 낮음 | 일괄 액션 없음 — 전부 행별(`Archive.razor:53-56`, `RecycleBin.razor:58-61`). "휴지통 비우기"만 유일한 벌크 | 다중 선택 복원/영구삭제 |
| 낮음 | 아카이브에서 삭제 시 Undo 없음(`Archive.DeleteNote:137`) — 홈/에디터는 Undo 제공 | 일관성 위해 Undo 추가 |
| [OK] | 휴지통 비우기 전 개수+"되돌릴 수 없음" 경고, 영구삭제 개별 확인(`RecycleBin.razor:151, 135-137`) | — |

### 1.7 전반 UX

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| 중간 | **오프라인 처리 전무.** 원격 API 의존 WASM SPA인데 `navigator.onLine` 체크·서비스워커·오프라인 배너 없음 | 네트워크 끊기면 모든 동작이 일반적 "Failed to…" 스낵바로 실패, 오프라인 상황임을 알려주지 않음 | 온라인 상태 배너 + 오프라인 시 저장 큐잉 |
| 중간 | 슬래시 커맨드 발견성 낮음 + 업로드 클라 사이드 크기/타입 가드 없음. 빈 에디터 placeholder("Write something…", `interop.js:77`)에 힌트 없음, 삽입 "+" 어포던스 없음. 업로드는 서버가 거절할 때까지 무한정 업로드(`upload.js`) | 신규 사용자는 커스텀 블록의 존재를 모른다. 100MB 파일도 다 올라간 뒤 거절됨 | placeholder에 "/ 입력해 블록 삽입" 힌트, 업로드 전 클라 크기/타입 체크 |
| 중간 | **모바일 미지원.** 보드는 HTML5 `draggable`(`Board.razor:70`)이라 터치 미발동, 버블/슬래시 메뉴는 포인터 전제, 터치 핸들러 전무 | 셀프호스팅 개인 노트는 모바일에서 볼 일이 잦다 | 최소 읽기/간단 편집만이라도 터치 대응 |
| 낮음 | 전역 단축키가 입력 중에도 발동(`keyboard-shortcuts.js:21-29`는 bare 키만 스킵). `Ctrl+N`(새 노트, `MainLayout.razor:93`)이 제목/검색 타이핑 중에도 페이지 이탈 | 편집 중 의도치 않은 이탈 | 텍스트 필드 포커스 시 modifier 조합도 스킵 |
| [OK] | 스낵바 일관성, draft 복구, 크로스탭 토큰 동기화, 프론트 서비스의 예외 미삼킴(로그+`ServiceResult.Fail`, 404 vs 일시오류 구분) | — | — |

---

## 2. 기술 품질 평가 (구조 / 성능 / 보안 / 유지보수성)

### 2.1 보안

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **치명** | **실 시크릿이 소스에 커밋됨.** `appsettings.Development.json:3`에 실 원격 DB 접속문자열(`Host=pg.smoh.kr;Port=55432;…Password=8fXL6…`), `:8`에 실 `Auth:PasswordHash`, `:6`에 JWT 시크릿. 호스트가 소유자 도메인(`smoh.kr`)과 일치 | "의도적 커밋"이라 했지만 이건 throwaway가 아니라 **운영 인접 DB의 실 비밀번호**다. repo 접근 = DB 접근. 공유 링크·저장소 유출 시 즉시 침해 | 즉시 로테이트, user-secrets/env로 이전. CLAUDE.md의 "의도적 커밋" 방침 자체를 재검토 |
| **치명(설정)** | 커밋된 dev 비밀번호 해시가 2-part 레거시 포맷(`:8`)이라 `Verify`가 100k iterations로 검증(`PasswordHasher.cs:24,67`), 현행 600k가 아님 | 실 자격증명을 하향된 반복수로 배포 | 600k 3-part 해시로 재생성 |
| **높음** | **X-Forwarded-For를 아무 출처에서나 신뢰 → 로그인 rate-limit 우회.** `Program.cs:42-43`이 `KnownIPNetworks/KnownProxies`를 비워 위조 XFF 허용. 로그인 리미터가 그 IP로 파티션(`:118-120`) | 앱 포트가 프록시 외에 직접 도달 가능하면, 공격자가 XFF 값만 바꿔 IP당 새 10/min 버킷을 얻어 브루트포스 무력화. 보안이 전적으로 네트워크 격리에만 의존 | `KnownProxies`에 실제 프록시 지정(defense-in-depth) |
| **높음** | 계정 잠금/글로벌 스로틀 없음. 로그인은 IP당 10/min만(`AuthController.cs:29-53`, `Program.cs:118-129`) | 단일 비밀번호 + 분산 소스(또는 XFF 위조) 시 절대 상한·잠금 없음. 방어는 PBKDF2 600k뿐 | 실패 누적 시 지수 백오프/일시 잠금 |
| **높음** | 저장 HTML XSS 방어가 sanitizer 단일 계층, **CSP 없음.** `SecurityHeadersMiddleware.cs:13`은 `nosniff`만. sanitizer는 모든 `data-*` 보존(`HtmlSanitizerService.cs:38-42`) | Ganss.Xss에 우회가 하나라도 있거나, sanitize 이전/직접 DB write로 들어온 노트가 있으면 2차 방어선이 없다. 공유 페이지는 익명에게 노출됨 | CSP 헤더 추가(스크립트 인라인 차단) |
| 중간 | localStorage JWT: 서버측 폐기 불가 + 24h 수명 + XSS 탈취 가능(`AuthController.cs:87-96`) | XSS 하나면 하루짜리 토큰 유출. 폐기 수단 없음 | 수명 단축, (가능하면) httpOnly 쿠키 검토, 최소 CSP로 XSS 차단 |
| 중간 | `text/plain`·`text/markdown` 업로드는 매직바이트 검사 스킵(`FilesController.cs:114-115`) — 클라 Content-Type만 신뢰 | 임의 바이트 저장 후 `text/plain`으로 서빙(`nosniff`+Content-Disposition로 실행은 완화되어 Medium) | 최소한의 크기/휴리스틱 |
| 낮음 | WebP 매직바이트가 RIFF 접두만 검사(`FilesController.cs:111-112`), offset 8의 `WEBP` 미검사 — AVI/WAV도 통과. `ImagesController:79-82`는 제대로 검사(불일치) | 두 컨트롤러 검증 로직 통일 |
| 낮음 | PAT 인증 경로 rate-limit 없음(`ApiTokenAuthHandler.cs:27-67`) — 요청마다 DB 조회. 거절 시 토큰 끝 4자 로그(`:44-45`) | 유니크 인덱스 해시라 추측은 불가하나 DB 플러드 제어 없음 | 리미터 추가, 로그에서 토큰 조각 제거 |
| [OK] | 타이밍세이프 비교(`FixedTimeEquals`), `/api/auth/hash` 실제 dev-gated(`AuthController.cs:65`), JWT/PAT 만료 검증, sanitize-at-write(`NoteService.cs:311,354`), 경로 순회 방어(GUID 파일명+`{id:guid}`), 업로드 크기캡, 공유 토큰 128비트+유니크 인덱스, CORS 자격증명 미허용 | — | — |

### 2.2 성능

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| 중간 | `IsReferencedInAnyNoteAsync`가 파일마다 인덱스 없는 풀스캔(`FileCleanupService.cs:217-223`) — `Content.ToLower().Contains(needle)`. 트라이그램 인덱스는 `ContentText`에만(`NoteDbContext.cs:46-49`), `Content`엔 없음 | 첨부·이미지 **개당** 순차 스캔(`:85,127,159,186`). 노트/파일 늘면 일일 잡·삭제 시 비용 급증 | 참조 스캔 대상 컬럼에 인덱스 or `ContentText` 기반으로 전환 |
| 중간 | 백링크/제목 전파 스캔도 인덱스 미적용 — `GetBacklinks(:583)`, `UpdatePageLinkReferences(:394)`, `StripMentionReferencesAsync(:1010)`가 `Content.Contains("data-note-id=…")` LIKE | 코퍼스 크기에 비례하는 순차 스캔 | 참조 관계 정규화 테이블 or 인덱스 |
| [OK] | 목록 쿼리에 뚜렷한 N+1 없음 — 라벨 인라인 프로젝션, 자식 카운트/부모 제목 배치(`GetChildCountsAsync:1084`). 검색은 GIN 트라이그램 활용, LIKE 패턴 이스케이프 | — | — |

### 2.3 구조 / 유지보수성

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **높음** | **테스트 커버리지 공백 + CI 없음.** `NoteService` 수준 xUnit(SQLite in-memory)만, Web 프로젝트 테스트 0, CI 워크플로우 0 | 위에 나열된 UX 회귀(라벨 무시, 다이얼로그 데이터손실, 공유 Mermaid)들이 전부 테스트로 못 잡히는 영역. 트라이그램 등 PG 전용 동작은 수동 검증뿐 | 최소 스모크 E2E(Playwright) + GitHub Actions로 `dotnet build`+`dotnet test` 자동화 |
| 중간 | 동시성 강제저장 우회가 클라/PAT로 가능. `clientVersion == default`면 낙관적 동시성 완전 우회(`NoteService.cs:367-368`) — last-write-wins로 동시 편집 무음 덮어쓰기 | "force save"가 서버 인가 액션이 아니라 클라가 0 버전만 보내면 트리거됨 | 강제저장은 명시적 별도 파라미터/엔드포인트로 |
| 낮음 | no-op 마이그레이션 커밋(`20260709082457_AddNoteConcurrencyToken.cs` Up/Down 비어있음) | 무해하나 마이그레이션 로그 노이즈 | — |
| [OK] | 백그라운드 잡 try/catch로 호스트 미크래시 + `CreateAsyncScope`로 scoped DbContext, 하드삭제는 `ExecutionStrategy`+트랜잭션, 글로벌 예외→ProblemDetails(내부 미노출), 미들웨어 순서 정확, 프론트 서비스 예외 미삼킴 | — | — |

---

## 3. 항목별 점수 (10점 만점)

| 항목 | 점수 | 근거 요약 |
|---|---:|---|
| **UX / 사용성** | **6.5** | 에디터의 방어 로직·상태 처리·Undo는 상위 5%. 그러나 일괄삭제 미노출·SubNoteDialog 데이터손실·공유 Mermaid 미렌더 등 "보이지 않는/새는" 지점이 신뢰를 깎음 |
| **아키텍처 / 구조** | **8.0** | 라이프사이클 경계, 미들웨어 순서, 잡 격리, 예외 처리 모두 깔끔. 모듈 분리 양호 |
| **성능** | **7.5** | 목록/검색은 인덱스 활용 우수. 파일 참조·백링크 스캔이 인덱스 밖 풀스캔이라 규모 확장 시 부담 |
| **보안** | **4.5** | 해시·타이밍·sanitize-at-write·업로드 방어 등 기본기는 탄탄하나, **실 시크릿 커밋 + XFF 신뢰 + CSP 부재**가 치명/높음으로 상쇄 |
| **유지보수성** | **6.0** | 코드 자체는 읽기 쉬우나 테스트 공백 + CI 부재가 회귀 방지를 개인 규율에만 의존하게 만듦 |
| **종합** | **6.5** | "잘 만든 개인 프로젝트"에서 "안심하고 셀프호스팅 배포" 사이에 시크릿·인증·발견성 3개 벽 |

---

## 4. 출시 전 반드시 고칠 상위 3가지

1. **[치명] 커밋된 실 시크릿 즉시 로테이트 + 저장소에서 제거.**
   `appsettings.Development.json`의 DB 비밀번호·JWT 시크릿·비밀번호 해시(`:3,6,8`)는 지금 이 순간 유효하다. DB 비밀번호를 즉시 바꾸고, 시크릿을 env/user-secrets로 옮기고, git 히스토리에서 제거(`git filter-repo`). "의도적 커밋" 방침을 이 파일에 한해 폐기. **이건 코드 수정이 아니라 사고 대응이다.**

2. **[높음] 인증 경계 2건 동시 처리 — XFF 신뢰 + CSP 부재.**
   `Program.cs:42-43`의 `KnownProxies`를 실제 프록시로 채워 로그인 rate-limit 우회를 막고, `SecurityHeadersMiddleware`에 CSP를 추가해 저장 HTML XSS의 2차 방어선을 세운다. 이 둘이 없으면 "익명 공유 + localStorage JWT" 조합이 실질 위험이 된다.

3. **[높음] 데이터 손실·발견성 UX 3종 픽스.**
   (a) `SubNoteDialog`에 `beforeunload`+draft 가드 이식(`SubNoteDialog.razor` — 현재 편집분 소실), (b) 홈 체크박스에 "선택 삭제" 액션바 추가(`Home.razor` — 현재 Delete 키 외 트리거 없음), (c) 공유 페이지 Mermaid 렌더 호출(`Share.razor:29`). 셋 다 코드는 이미 옳은데 UI가 사용자를 배신하는 지점이다.

> **덧: 4번째 후보(강력 권장)** — 최소 CI(GitHub Actions로 `dotnet build`+`dotnet test`) + 스모크 E2E. 위 픽스들의 회귀를 사람 규율이 아니라 파이프라인이 잡게 만들어야 한다.
