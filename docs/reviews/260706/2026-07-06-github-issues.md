# GitHub 이슈 목록 — 2026-07 리뷰 후속

원본: `2026-07-06-improvement-review.md`, `2026-07-06-improvement-execution-plan.md`
finding 하나당 이슈 1개. 각 이슈에 제목 / 라벨 / 마일스톤 / 본문 포함.
(코드 심볼·파일 경로·finding ID는 원문 유지)

권장 라벨: `security` `data-loss` `resilience` `hardening` `tech-debt` `docs` `a11y` `ux`
+ `priority: high|medium|low`
마일스톤: `Stage 1`~`Stage 4`, 그 외는 `Backlog`.

---

## Epic (트래킹)

**제목:** `[Epic] 코드베이스 개선 — 2026-07 리뷰 후속`
**라벨:** `epic`
본문:
```
2026-07-06 전체 코드베이스 리뷰의 finding별 이슈를 묶는 트래킹 이슈.
원칙: 데이터 손실 먼저, 리팩터링 마지막. 버그픽스는 회귀 테스트와 함께.

Stage 1 (데이터 손실): #H1 #H4 #H6 #H7 #H8
Stage 2 (XSS): #H3 #H2
Stage 3 (복원력): #M12 #M8 #M9 #M10 #H5
Stage 4 (하드닝): #M1 #M5 #M6 #M7 #M3 #M4 #M2 + 테스트/문서
Backlog: #M11 #M13 #M14 #M15 #M16 #M17 #L1~#L14

확정: H2는 서버 사이드 HtmlSanitizer 허용목록 방식.
```

---

# High (Stage 1~3)

## H1
**제목:** `H1: 노트 생성 시 오버포스팅으로 보이지 않는 purge 대상 노트 생성`
**라벨:** `data-loss` `security` `priority: high` · **마일스톤:** Stage 1
```
문제: NotesController.Create가 NoteItem 엔티티를 직접 바인딩. body에 "deletedAt"/"archivedAt"을
넣으면 IsDeletionRoot=false로 soft-delete된 노트가 생성됨 — 휴지통 목록에도 안 보이고 복원 불가,
RecycleBinPurgeJob이 조용히 영구 삭제.

수정: NoteService.Create에서 상태 필드 강제 초기화 —
  DeletedAt=null; IsDeletionRoot=false; ArchivedAt=null; IsArchiveRoot=false;
(기존 Id/UpdatedAt/ContentText 덮어쓰기와 함께). 서버 소유 필드도 함께 null 처리.
지금은 create DTO보다 이 방식 선호(표면 최소화).

테스트: NoteServiceTests — DeletedAt/ArchivedAt 세팅한 POST가 활성 노트로 기본 목록에 노출.
파일: Controllers/NotesController.cs:86, Services/NoteService.cs:203
검증: build; 유닛 테스트 green.
```

## H2
**제목:** `H2: 서버 사이드 HTML 새니타이징 부재 → PAT-세션 권한 상승`
**라벨:** `security` `priority: high` · **마일스톤:** Stage 2
```
문제: 노트 HTML이 그대로 저장(StripHtml은 ContentText용일 뿐). 유출된 API 토큰이 노트에
<script>를 심으면 소유자 브라우저에서 실행 → localStorage의 JWT 탈취 →
DELETE /api/settings/all-data 도달. 현재 방어는 클라이언트 Tiptap 스키마 필터링뿐.

수정(확정): Ganss.Xss(HtmlSanitizer) 추가. 중앙 IHtmlSanitizerService로 허용목록 단일 소유.
NoteService.Create/Update에서 저장 전 새니타이즈. 허용목록은 Tiptap 스키마 미러링 —
커스텀 div[data-type=...] 노드(Callout, MermaidNode, PageLink, NoteMention, FileAttachment,
Toggle/ToggleSummary/ToggleContent, AccordionGroup), data-* 속성, data-collapsible/data-open
헤딩, 코드블록, 링크, /uploads·/images 이미지.
치명적 제약: 에디터·StripHtml·검색이 의존하는 data-type/data-* 속성을 절대 제거하지 말 것.
허용목록 확정 전 tiptap-editor.js에서 스키마 열거.

테스트: HtmlSanitizerServiceTests — (a) <script>/on* 제거 (b) 각 커스텀 노드 HTML 무변경
라운드트립 (c) javascript: URL 제거.
파일: Vanadium.Note.REST.csproj, Services/NoteService.cs
```

## H3
**제목:** `H3: tiptap-editor.js의 innerHTML XSS 싱크`
**라벨:** `security` `priority: high` · **마일스톤:** Stage 2
```
문제: 멘션 메뉴(line 480, 노트 제목 보간), 업로드 토스트(line 1453, 파일명),
Mermaid 에러 프리뷰(line ~700, 에러 메시지)에서 innerHTML 사용. 제목이
<img src=x onerror=...>면 실행됨. H2와 결합 시 페이로드가 영속.

수정: 사용자 유래 문자열을 textContent/createElement로 교체(~15줄). 정적 아이콘 마크업은 유지 가능.
테스트: 수동 — 제목이 <img src=x onerror=alert(1)>인 노트를 멘션 메뉴에서 열어 스크립트 미실행,
순수 텍스트 렌더 확인.
파일: wwwroot/js/tiptap-editor.js
```

## H4
**제목:** `H4: SubNoteDialog가 마지막 1.5초 편집분을 조용히 폐기`
**라벨:** `data-loss` `priority: high` · **마일스톤:** Stage 1
```
문제: Close()/Dispose()가 debounce CTS를 flush 없이 취소(NoteEditor.DisposeAsync/ExpandToFullPage는
flush함). 버튼/Esc/백드롭으로 닫으면 대기 내용 유실되는데 상태는 "Unsaved changes"로 남음.

수정: 저장 대기 시 Close()에서 await DoAutoSave() 후 CTS 취소. flush 완료 후에만 상태 해제.
테스트: 수동 — 서브노트 입력 후 1.5초 내 버튼/Esc/백드롭으로 닫고 재오픈 → 내용 유지.
파일: Components/SubNoteDialog.razor
```

## H5
**제목:** `H5: Ctrl+K 이중 바인딩 — 링크 팝오버와 퀵내브 동시 발동`
**라벨:** `ux` `priority: high` · **마일스톤:** Stage 3
```
문제: tiptap-editor.js:1768이 Ctrl/Cmd+K에서 preventDefault만 하고 stopPropagation 없이
링크 팝오버 오픈 → 이벤트가 MainLayout의 전역 keyboard-shortcuts.js 핸들러로 버블 →
퀵내브 오버레이 밑에 링크 팝오버가 열림.

수정: 에디터 리스너에 e.stopPropagation() 추가. 링크를 Ctrl+Shift+K로 이동 검토.
검증: 에디터에서 Ctrl+K → 퀵내브만 열리고 링크 팝오버 없음.
파일: wwwroot/js/tiptap-editor.js:1768
```

## H6
**제목:** `H6: 오펀 파일 정리가 방금 업로드한 파일을 삭제`
**라벨:** `data-loss` `priority: high` · **마일스톤:** Stage 1
```
문제: FileCleanupService.DeleteAllOrphansAsync가 유예 기간 없이 미참조 파일을 오펀 처리.
아직 노트 HTML에 저장 안 된 파일(자동 저장 1500ms, 작성 중)이 스캔되면 삭제됨.
FileAttachment.UploadedAt이 있으나 참조 안 됨.

수정: ~1h 유예 창 도입. UploadedAt이 창 내인 첨부, CreationTimeUtc가 창 내인 이미지 파일 스킵.
설정 가능(FileCleanup:GraceMinutes, 기본 60).
테스트: FileCleanupServiceTests(신규) — 최근 UploadedAt 미참조 첨부는 생존, 오래된 것은 제거.
파일: Services/FileCleanupService.cs:66
```

## H7
**제목:** `H7: 컨테이너 재생성 시 업로드 파일 유실`
**라벨:** `data-loss` `priority: high` · **마일스톤:** Stage 1
```
문제: UploadsPath가 컨테이너 파일시스템에 있고 docker-compose.yml의 rest 서비스에 볼륨 정의 없음.
이미지 업데이트마다 업로드 파일 전부 파괴, FileAttachments 행은 생존(다운로드 404).

수정: /app/uploads에 네임드 볼륨 추가. 추가로 Dockerfile에 비루트 USER 지시자(현재 root 실행),
베이스 이미지 태그 고정, compose 이미지 태그 :latest 제거.
테스트: 수동 — docker compose up, 파일 업로드, rest 컨테이너 재생성, 파일 다운로드 확인.
파일: docker-compose.yml, Vanadium.Note.REST/Dockerfile
```

## H8
**제목:** `H8: 탭 닫기 시 미저장 변경 보호 부재`
**라벨:** `data-loss` `priority: high` · **마일스톤:** Stage 1
```
문제: 앱 JS에 beforeunload 핸들러 없음. NoteEditor.DisposeAsync는 앱 내 내비게이션만 커버,
debounce 창에서 탭을 닫으면 데이터가 조용히 유실.

수정: _hasPendingChanges가 true인 동안 beforeunload 등록/해제. 브라우저 네이티브 확인창으로 충분.
테스트: 수동 — 편집 후 debounce 창에서 탭 닫기 시도 → 확인창 노출.
파일: wwwroot/js/(신규/기존 interop 확장), NoteEditor.razor, SubNoteDialog.razor
```

---

# Medium

## M1
**제목:** `M1: JwtSecret 시작 시 미검증`
**라벨:** `security` `priority: medium` · **마일스톤:** Stage 4
```
문제: ""(appsettings 기본값)가 null 체크를 통과하고 나중에 불투명한 에러로 실패. 짧은 시크릿은 HS256 약화.
수정: 시작 시 Length >= 32 검증, 빈 값/짧은 값은 fail-fast(Program.cs:56).
```

## M2
**제목:** `M2: PBKDF2 100k 반복은 현행 OWASP 권고(600k) 미달`
**라벨:** `security` `priority: medium` · **마일스톤:** Stage 4
```
문제: 반복 상향은 재해싱 필요.
수정: 저장 포맷에 반복 횟수 인코딩(salt:hash:iterations)하여 향후 포맷 파괴 없이 상향 가능하게.
```

## M3
**제목:** `M3: 로그인 rate limiter가 단일 전역 버킷`
**라벨:** `security` `priority: medium` · **마일스톤:** Stage 4
```
문제: 누구나 10 junk req/min으로 소유자 락아웃 가능.
수정: 클라이언트 IP로 파티션(ForwardedHeaders 필요, M4 참조).
```

## M4
**제목:** `M4: HTTPS 리다이렉트/HSTS/ForwardedHeaders/보안 헤더 부재`
**라벨:** `security` `priority: medium` · **마일스톤:** Stage 4
```
수정: TLS 프록시 가정 시 UseForwardedHeaders 추가(IP/scheme 정확화),
최소 X-Content-Type-Options: nosniff 추가(파일 서빙 관련). HSTS/HTTPS 리다이렉트 검토.
```

## M5
**제목:** `M5: ImagesController.Get이 인증 엔드포인트에 Cache-Control: public 설정`
**라벨:** `security` `priority: medium` · **마일스톤:** Stage 4
```
문제: 공유 프록시가 비공개 이미지를 캐시할 수 있음.
수정: ResponseCacheLocation.Client 사용.
```

## M6
**제목:** `M6: 멘션 스트리핑이 휴지통 노트를 누락`
**라벨:** `hardening` `priority: medium` · **마일스톤:** Stage 4
```
문제: StripMentionReferencesAsync/UpdatePageLinkReferences가 DeletedAt==null 필터로 조회 —
프로젝트의 IgnoreQueryFilters() 규칙 위반. 복원된 노트가 죽은 멘션 링크/오래된 page-link 제목 보유.
수정: IgnoreQueryFilters() 추가로 휴지통 노트 포함.
```

## M7
**제목:** `M7: HardDeleteAsync 다중 저장 시퀀스가 트랜잭션 없이 실행`
**라벨:** `hardening` `priority: medium` · **마일스톤:** Stage 4
```
문제: 실패 시 부분 완료 가능.
수정: AccountService처럼 execution strategy + 트랜잭션으로 래핑.
```

## M8
**제목:** `M8: 세션 중 401이 작업과 컨텍스트를 유실`
**라벨:** `resilience` `ux` `priority: medium` · **마일스톤:** Stage 3
```
문제: AuthTokenHandler가 returnUrl 없이 /login 리다이렉트. 내비게이션이 NoteEditor를 dispose,
flush 저장이 토큰 없이 재전송되어 실패.
수정: returnUrl 추가, 대기 내용을 sessionStorage에 보관 후 재로그인 시 복원.
```

## M9
**제목:** `M9: 로그인이 401/429/네트워크를 "Invalid password"로 뭉뚱그림`
**라벨:** `ux` `priority: medium` · **마일스톤:** Stage 3
```
문제: rate-limited/오프라인 사용자가 비밀번호 틀렸다고 안내받음.
수정: 판별 가능한 결과(discriminated result) 반환.
파일: Login.razor
```

## M10
**제목:** `M10: 모든 노트 로드 실패를 404로 취급`
**라벨:** `resilience` `priority: medium` · **마일스톤:** Stage 3
```
문제: NoteEditor가 일시적 500/네트워크 blip에도 recents 정리 후 홈 리다이렉트.
수정: GetAsync를 상태 인식하게, 실제 404에서만 정리.
```

## M11
**제목:** `M11: 수동 저장이 자동 저장과 경합`
**라벨:** `ux` `priority: medium` · **마일스톤:** Backlog
```
문제: SaveNote()가 _isSaving 가드를 우회 → 자동 저장 진행 중 Ctrl+S 시 잘못된 409 배너.
2s "Saved" 지연이 _isSaving을 잡아 다음 저장 블록.
수정: 저장 경로 단일 가드로 통합, 코스메틱 지연이 락 잡지 않게.
```

## M12
**제목:** `M12: 에디터 스택을 런타임에 CDN에서 로드, 일부 버전 미고정`
**라벨:** `resilience` `priority: medium` · **마일스톤:** Stage 3
```
문제: prosemirror-state, prosemirror-view, lowlight, tiptap-markdown이 버전 미고정.
오프라인/CDN 장애 시 사용 불가, 업스트림 배포로 밤새 깨질 수 있음, init 실패는 콘솔 로그뿐(죽은 div).
수정(지금): 미고정 import 전부 정확한 버전 고정. init 실패 시 "Editor failed to load — Retry" 배너.
수정(백로그): 에디터 스택 로컬 번들링(오프라인/PWA 전제).
검증: devtools에서 CDN 도메인 차단 → 배너 확인.
파일: tiptap-editor.js
```

## M13
**제목:** `M13: 키보드 접근성 갭`
**라벨:** `a11y` `priority: medium` · **마일스톤:** Backlog
```
문제: Home 행/정렬 헤더/확장 버튼이 click-only div. QuickNavDialog에 role="dialog"/aria-modal 없음.
Board 다이얼로그 focus trap 없음.
수정: 제목은 anchor, 헤더는 button, 팔레트에 aria 속성. 다이얼로그 focus trap.
```

## M14
**제목:** `M14: 확인 UX 불일치`
**라벨:** `ux` `priority: medium` · **마일스톤:** Backlog
```
문제: 네이티브 confirm()(Home, NoteEditor) vs 스타일드 ConfirmDialog(Archive, RecycleBin).
NoteEditor.DeleteNote는 자식 없을 때 확인 없음, Home은 항상 확인.
수정: 공유 confirm 서비스로 추출.
```

## M15
**제목:** `M15: 컴포넌트 중복`
**라벨:** `tech-debt` `priority: medium` · **마일스톤:** Backlog
```
문제: 페이지네이션 마크업 ×3, ConfirmAsync ×2, ~55줄 라벨 피커가 NoteEditor/SubNoteDialog 중복,
자동 저장 배관 중복(SubNoteDialog는 fire-and-forget ContinueWith, 재진입 가드 없음).
수정: Pagination/LabelPicker/confirm helper 추출, 자동 저장을 공유 헬퍼로 통합.
```

## M16
**제목:** `M16: 모바일/반응형 미지원`
**라벨:** `ux` `docs` `priority: medium` · **마일스톤:** Backlog
```
문제: 페이지 레벨 미디어쿼리 없음. 좁은 화면에서 테이블/보드/에디터 헤더 오버플로.
수정: 데스크톱 전용이 의도면 CLAUDE.md에 명시. 아니면 반응형 추가.
```

## M17
**제목:** `M17: 에디터 클로저가 오래된 토큰 캡처`
**라벨:** `resilience` `priority: medium` · **마일스톤:** Backlog
```
문제: 토큰이 tiptapInterop.init에서 1회 페치. 만료+재로그인 후 열린 에디터가 이미지 로드/업로드
조용히 실패.
수정: dotnetRef로 작업마다 토큰 페치.
```

---

# Low (Backlog)

## L1
**제목:** `L1: 에러 계약 불일치`
**라벨:** `tech-debt` `priority: low`
```
ProblemDetails, {message}, {error}, bare-string BadRequest 혼재. ProblemDetails로 표준화.
```

## L2
**제목:** `L2: 로드 실패/로딩 상태 부재`
**라벨:** `ux` `priority: low`
```
Home이 로드 실패 후 "No notes yet. Create one!" 표시, Board는 로딩 상태 없음. 실패/로딩 상태 추가.
```

## L3
**제목:** `L3: 검색 placeholder가 실제 범위와 불일치`
**라벨:** `ux` `priority: low`
```
"Search by title..."이나 검색은 제목+내용 커버. 문구 수정.
```

## L4
**제목:** `L4: 동시성 체크가 인메모리 read-then-write`
**라벨:** `hardening` `priority: low`
```
UpdatedAt에 [ConcurrencyCheck] 추가로 무결하게.
```

## L5
**제목:** `L5: MainLayout/NavMenu의 async void 인증 핸들러`
**라벨:** `tech-debt` `priority: low`
```
async void auth-state 핸들러를 try/catch로 래핑.
```

## L6
**제목:** `L6: tiptap-editor.js 2064줄 모놀리스`
**라벨:** `tech-debt` `priority: low`
```
이미 ES 모듈 — 빌드 스텝 없이 nodes/*.js, upload.js, interop.js로 분할.
```

## L7
**제목:** `L7: 멘션 제안이 매 키스트로크마다 API 호출`
**라벨:** `resilience` `priority: low`
```
~150ms 디바운스 추가.
```

## L8
**제목:** `L8: TokenStore 캐시가 탭 간 재조회 안 함`
**라벨:** `ux` `priority: low`
```
storage 리스너로 탭 간 로그아웃 동기화.
```

## L9
**제목:** `L9: 무제한 문자열 컬럼`
**라벨:** `hardening` `priority: low`
```
FileAttachment.OriginalName/ContentType, ApiToken.TokenHash에 [MaxLength] 추가.
```

## L10
**제목:** `L10: FileCleanupService가 전체 노트 내용을 단일 문자열로 로드`
**라벨:** `tech-debt` `priority: low`
```
현재는 무방하나 per-GUID AnyAsync(Contains)가 확장성 좋음.
```

## L11
**제목:** `L11: 데드 코드 — FilesController.Upload의 도달 불가 content-type 폴백`
**라벨:** `tech-debt` `priority: low`
```
~line 74의 도달 불가 폴백 제거.
```

## L12
**제목:** `L12: 프로덕션 JS 경로의 콘솔 노이즈`
**라벨:** `tech-debt` `priority: low`
```
console.log → console.debug.
```

## L13
**제목:** `L13: CLAUDE.md 문서 불일치`
**라벨:** `docs` `priority: low`
```
문서화된 미들웨어 순서가 실제와 다름(실제가 맞음). 필수 docker 환경변수에 AUTH_PASSWORD_HASH 누락.
이미지 저장 포맷({guid}.{ext}, DB 레코드 없음) 미문서화. 전부 수정.
```

## L14
**제목:** `L14: CancellationToken 배관 불일치`
**라벨:** `tech-debt` `priority: low`
```
ApiToken/cleanup 경로엔 있으나 대부분 NoteService/컨트롤러 액션엔 없음. 일관되게 배관.
```

---

# 기능 제안 (부재 확인됨, 별도 이슈/라벨 `enhancement`)

- 백링크 패널("what links here" — 멘션 데이터 존재하나 UI/엔드포인트 없음)
- 전체 노트셋 내보내기(현재 per-note Markdown만)
- .md 파일 가져오기
- 노트 템플릿
- Home 목록에서 드래그로 재부모 지정(API는 ParentNoteId 지원)
- 오프라인/PWA(M12에서 에디터 로컬 번들링 후 가능)
