# Vanadium 제품 리뷰 — 2026-07-14

관점: 시니어 UX 리서처 / 소프트웨어 아키텍트. 문서가 아닌 **실제 코드**를 근거로 평가했으며, 각 항목에 file:line 근거를 표기한다. 목적은 문제 발견이다.

총평 한 줄: **해피패스는 상용 제품 수준으로 다듬어져 있으나, "실패하는 순간"과 "두 번째 사용자(익명 뷰어)"가 만나는 지점에서 완성도가 급락한다.** 코어 암호화·sanitize·미들웨어는 정석이지만, 커밋된 실 크리덴셜과 단일 방어층 XSS 모델이 보안 점수를 끌어내린다.

---

## 1. UX/사용성 — 화면별 막힘 지점

### Home (`/`)

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **높음** | 벌크 삭제가 물리 `Delete` 키로만 발동. 체크박스·전체선택 UI는 있으나(`Home.razor:70-91`) 트리거는 키 입력뿐(`:269`, `:424`) | 발견 불가능한 기능은 없는 기능이다. 체크박스를 눌러도 아무 일이 안 일어나 "버그"로 인지됨 | 행 선택 시 "N개 선택됨 — 삭제" 액션 바를 상단에 노출. Delete 키는 보조 수단으로 유지 |
| 중간 | 계층 탐색이 1단계 인라인 확장뿐. 손자 노트는 `+N` 배지로만 표시(`:187-190`) | "계층형 노트"가 핵심 기능인데 3단계 이상 트리는 노트를 열고 들어가야만 탐색 가능 | 재귀 확장 또는 사이드바 트리 뷰. 최소한 배지 클릭 시 해당 자식으로 드릴다운 |

### NoteEditor (`/editor/{id}`)

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **높음** | `SubNoteDialog`는 `beforeunload` 가드도 DraftStore도 없음. 409 시 "reopen to edit" 안내 후 `DisposeAsync`가 재시도해도 다시 409(`SubNoteDialog.razor:244-248`, `:330-331`) → **편집 내용 유실** | 본편집기는 이중 보호(가드+draft)인데 다이얼로그만 무방비. 사용자는 두 경로의 보호 수준 차이를 알 수 없다 | 본편집기의 draft-stash 경로를 다이얼로그에도 적용, 409 시 diff/덮어쓰기 선택지 제공 |
| 중간 | 자동 저장 실패가 작은 "Save failed" 라벨뿐, 스낵바 없음(`NoteEditor.razor:566-571`; 수동 저장 `:623`은 스낵바 있음) | 타이핑에 몰입한 사용자는 상태 라벨을 안 본다. 일시적 5xx 동안 저장이 계속 실패해도 인지 못 함 | 연속 N회 실패 시 스낵바 승격 + 재시도 버튼 |
| 중간 | 새 노트의 라벨 저장 결과를 무시(`:575-576`, `:640-641`), Board의 add-note도 동일(`Board.razor:338`) | UI에는 라벨이 보이는데 서버에는 없음 → 보드 컬럼에서 노트가 증발하는 "유령 버그"로 나타남 | `ServiceResult` 검사 후 실패 시 롤백+토스트 |
| 중간 | 슬래시 커맨드가 `/` 타이핑으로만 발견됨. placeholder("Write something…", `interop.js:77`)에 힌트 없음. 업로드에 클라이언트측 크기/타입 검증 없음(`upload.js`) | 커스텀 블록 7종이 셀링 포인트인데 진입로가 숨겨져 있음. 100MB 초과 파일은 다 올라간 뒤에야 거절됨 | placeholder에 "`/` 로 블록 삽입" 힌트, 업로드 전 크기/타입 프리체크 |
| 낮음 | 다중 이미지 붙여넣기 시 첫 장만 처리(`interop.js:166`의 `break`) | 스크린샷 여러 장을 복사한 사용자는 나머지가 조용히 사라진 걸 나중에야 발견 | 루프 전체 처리 |

잘된 것(참고): 409/403/로드 실패 전용 배너, beforeunload 가드, 저장 인디케이터, 401→draft 보관→재로그인 복원 체인(`AuthTokenHandler.cs:22-44`, `NoteEditor.razor:391-401,547`)은 견고하다.

### Login (`/login`)

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| 중간 | 비밀번호 필드에 `name`/`autocomplete="current-password"`/`<label>` 전부 없음(`Login.razor:13-19`) | **비밀번호 하나가 유일한 크리덴셜인 제품**에서 패스워드 매니저가 저장/자동완성을 못 하면 사용자는 약한 비밀번호를 고르게 됨. 보안 설계(600k PBKDF2)를 UX가 무력화 | `autocomplete="current-password"` + `name` + label 추가, hidden username 필드로 매니저 호환성 확보 |

### Share (`/share/{token}`, 익명)

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| **높음** | 공유 페이지에서 Mermaid가 raw 코드 블록으로 노출 — `Share.razor:29`가 HTML만 덤프하고 `renderMermaidIn`를 어디서도 호출 안 함 | 타깃 사용자 정의가 "다이어그램 중심 기록 개발자"다. 공유의 주 용도(다이어그램 포함 기술 노트 전달)에서 핵심 콘텐츠가 깨진 채 전달됨 | Share 페이지 렌더 후 `renderMermaidIn` 호출 (읽기 전용이라 interop 최소) |
| 중간 | 이미지가 브라우저 broken-image 아이콘으로 표시(placeholder/`onerror` 없음). 하단 경고문(`:32-33`)만 존재 | "알려진 제약"이어도 시각적 완충이 없으면 받은 사람에게는 그냥 깨진 페이지 | 단기: `onerror`로 placeholder 치환. 근본: 공유 노트 한정 익명 자산 프록시(토큰 스코프 검증) |

### QuickNav (Ctrl+K)

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| 중간 | focus trap 미적용(Board 다이얼로그는 적용, 팔레트만 예외), 결과 리스트에 `listbox`/`aria-activedescendant` 없음(`QuickNavDialog.razor:11-16`) | Tab이 뒤 페이지로 새고, 스크린리더에는 화살표 선택이 보이지 않음. "키보드 위주 워크플로우"를 표방하는 제품의 핵심 컴포넌트 | 기존 `focus-trap.js` 재사용 + ARIA listbox 패턴 적용 |

### Board / 전역

| 심각도 | 문제 | 왜 문제인가 | 개선안 |
|---|---|---|---|
| 중간 | 터치 미지원 — HTML5 `draggable` 기반 드래그(`Board.razor:70`)는 모바일에서 아예 동작 안 함. 터치 핸들러 전무 | 셀프호스팅 노트 앱은 폰에서 "잠깐 확인"하는 시나리오가 필수적으로 발생 | 최소한 보드에 터치 폴백(길게 눌러 이동 메뉴), 편집기 읽기 모드 검증 |
| 중간 | 오프라인 무감지 — `navigator.onLine` 체크, 서비스워커, 오프라인 배너 전무. 모든 실패가 일반 "Failed to…" 스낵바 | 사용자는 서버 장애/자기 네트워크/버그를 구분할 수 없음 | 온라인 상태 감지 + 오프라인 배너, 실패 메시지에 원인 구분 |
| 낮음 | `Ctrl+N` 등 전역 단축키가 입력 필드 포커스 중에도 발동(`keyboard-shortcuts.js:21-29`는 bare key만 차단) — 제목 입력 중 새 노트로 이동해버림 | 입력 컨텍스트에서 modifier 콤보도 필터링(편집기 `Ctrl+K`처럼 `stopPropagation` 패턴 확대) |
| 낮음 | Archive에서 삭제 시 Undo 없음(`Archive.razor:137`; Home/편집기는 있음), 휴지통/아카이브 벌크 액션 없음 | Undo 패턴 일관 적용, 다중 선택 복원/삭제 추가 |

---

## 2. 기술 품질

### 보안

**[치명] 실 크리덴셜이 리포에 커밋됨.** `appsettings.Development.json:3`에 실호스트 DB 접속정보(`Host=pg.smoh.kr;...Password=8fXL...`), `:6` JWT secret, `:8` 실 비밀번호 해시. "의도적 결정"이라지만 호스트가 소유자 도메인(smoh.kr)인 이상 throwaway가 아니다 — 리포 접근 = DB 즉시 침해. 게다가 커밋된 해시는 legacy 2-part 포맷이라 **100k iterations**로 검증됨(`PasswordHasher.cs:24,67`) — 600k 설계를 스스로 다운그레이드. → **크리덴셜 전부 회전 + user-secrets/env 이전. 이건 "편의" 예외로 둘 항목이 아님.**

**[높음] X-Forwarded-For 무조건 신뢰 → 로그인 rate limit 우회.** `Program.cs:42-43`이 `KnownIPNetworks`/`KnownProxies`를 비워 아무 발신자의 XFF나 수용. 로그인 리미터는 그 복원된 IP로 파티셔닝(`:118-120`)하므로, 앱 포트가 프록시 외 경로로 노출되면 헤더 로테이션만으로 10/min 버킷을 무한 생성. 방어가 전적으로 네트워크 격리 가정에 의존. → `KnownProxies` 지정 또는 프록시 미경유 시 XFF 무시.

**[높음] 단일 비밀번호 제품에 lockout/글로벌 상한 없음.** `AuthController.cs:29-53`의 방어는 per-IP 10/min뿐. 분산 소스(또는 위 XFF 스푸핑)에는 절대 상한이 없다. 계정이 하나뿐이라 크리덴셜 스터핑 대상이 명확. → 실패 누적 글로벌 지수 백오프 또는 임시 lockout.

**[높음] XSS 방어가 sanitizer 단일 층 — CSP 없음.** `SecurityHeadersMiddleware.cs:13`은 `nosniff`뿐, CSP는 명시적 보류(`:5-7`). sanitizer는 모든 `data-*` 속성을 보존(`HtmlSanitizerService.cs:38-42`). Ganss.Xss 우회가 하나라도 나오면 → localStorage의 24h JWT(회수 불가) 즉시 탈취 → 단일 사용자 앱 전체 장악. → 최소 `script-src 'self'` CSP 도입(WASM 요구사항 감안해 조정), 공유 페이지부터라도 적용.

중간: 강제 저장 우회 — 클라이언트가 `clientVersion`을 default로 보내면 동시성 체크 전체 스킵(`NoteService.cs:367-368`), 서버 승인 없는 last-write-wins(→ 명시적 `force=true` 파라미터로 분리). localStorage JWT + 무revocation(수용된 트레이드오프지만 CSP 부재와 결합 시 위험 증폭). 낮음: `text/plain`/`markdown` 업로드 content sniffing 스킵(`FilesController.cs:114-115`), WebP 매직바이트 검사 두 컨트롤러 불일치(`FilesController.cs:111