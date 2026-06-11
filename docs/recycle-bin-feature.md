# 기능 정의: 노트 휴지통 (Soft Delete)

상태: 검토용 초안
작성자: smoh (Claude와 함께 작성)
작성일: 2026-06-10

## 1. 개요

현재 노트 삭제는 즉시 실행되며 되돌릴 수 없다. `NoteService.Delete()`가 행을 하드 삭제하고, 하위 노트와 `NoteLabels`로 cascade되며, 다른 노트의 멘션/페이지 링크 참조를 제거하고, 고아 파일을 삭제한다. 클릭 실수(또는 `Home.razor`의 Delete 키 일괄 삭제)로 콘텐츠가 영구 파괴될 수 있다.

이 기능은 **휴지통**을 도입한다. 삭제된 노트는 유예 기간 동안 보존되어 복원할 수 있고, **30일** 경과 후 자동으로 영구 삭제된다.

### 목표

- 노트 삭제 시 파괴하지 않고 휴지통으로 이동한다.
- 휴지통의 노트는 모든 곳에서 보이지 않는다 (목록, 보드, 검색, 멘션, 하위 노트 목록).
- 사용자는 휴지통 페이지에서 복원/영구 삭제하거나 휴지통 전체를 비울 수 있다.
- 백그라운드 잡이 30일(설정 가능) 이상 경과한 노트를 영구 삭제한다.

### 비목표

- 버전 히스토리 / 콘텐츠 스냅샷.
- 라벨, 라벨 카테고리, 독립 파일 첨부에 대한 휴지통.
- 노트별 보존 기간 개별 설정.
- 리프레시 토큰, 멀티 테넌트 관련 사항 (기존 설계 유지).

## 2. 현재 동작 (기준선)

| 항목 | 현재 |
|---|---|
| 삭제 API | `DELETE /api/notes/{id}` → `NoteService.Delete()` 하드 삭제 |
| 하위 노트 | `ParentNoteId` FK의 DB cascade (`DeleteBehavior.Cascade`) |
| 라벨 | `NoteLabels` 행 cascade 삭제 |
| 참조 | 삭제 시점에 부모 콘텐츠에서 페이지 링크 블록 제거, 모든 노트에서 멘션 링크 제거 |
| 파일 | 삭제 직후 `FileCleanupService.DeleteOrphanedFromContentAsync(content)` 실행, `OrphanFileCleanupJob`이 24시간마다 재스캔(안전망) |
| 삭제 UI | `Home.razor` (Delete 키 + 일괄 선택, 확인 다이얼로그), `NoteEditor.razor` (삭제 버튼, 확인 다이얼로그) |

## 3. 사용자 경험

### 3.1 삭제

- `Home.razor`와 `NoteEditor.razor`의 삭제 진입점은 그대로 유지하되, 이제 **휴지통으로 이동**한다.
- 확인 다이얼로그 문구를 "영구 삭제"에서 다음과 같이 변경: "Move 3 note(s) to the Recycle Bin? Sub-notes will be moved too. Items in the Recycle Bin are deleted permanently after 30 days." (UI 텍스트는 영문 컨벤션 유지)
- 삭제 후 Snackbar 표시: "Note moved to Recycle Bin" + **Undo** 액션(복원 호출). 대부분의 실수 삭제를 휴지통 페이지 방문 없이 원클릭으로 복구할 수 있다.

### 3.2 휴지통 페이지

새 라우트 `/recycle-bin` (`RecycleBin.razor`), 내비게이션 메뉴에서 진입 (휴지통 아이콘).

- 사용자 소유의 휴지통 노트를 삭제일 최신순으로 표시: 제목, 삭제일, "N일 후 영구 삭제" 안내, 서브트리 루트의 하위 노트 수.
- **삭제 루트**만 표시한다 (부모와 함께 휴지통에 들어간 하위 노트는 개별 표시하지 않으며, 부모와 함께 복원된다).
- 행별 액션: **복원(Restore)**, **영구 삭제(Delete forever)** (확인 다이얼로그).
- 헤더 액션: **휴지통 비우기(Empty Recycle Bin)** (건수 포함 확인 다이얼로그).
- 휴지통의 노트는 읽기 전용이며, 휴지통 페이지에서 에디터를 열지 않는다.

### 3.3 휴지통 상태의 노출 규칙

- 제외 대상: 노트 목록/보드, 전문 검색, 멘션 검색(`/api/notes/mention-search`), 하위 노트 목록, 하위 노트 수.
- 휴지통 노트에 대한 `GET /api/notes/{id}`는 일반 에디터 경로에서 404를 반환 — 오래된 링크/멘션으로 진입하면 없는 노트처럼 동작 (`NotFound` 페이지).
- **다른** 노트 안의 멘션 링크와 페이지 링크 블록은 휴지통 보관 중에는 그대로 유지한다 (제거는 영구 삭제 시점으로 연기). 복원 시 모든 것이 원래대로 돌아온다.

### 3.4 복원 시맨틱

- 삭제 루트를 복원하면 함께 휴지통에 들어간 서브트리 전체가 복원된다.
- 복원할 노트의 부모가 이미 없거나 부모도 휴지통에 있으면, 보이지 않는 위치로 부활하는 것을 막기 위해 **루트 노트**로 재부착한다 (`ParentNoteId = null`). 원래 부모의 콘텐츠(페이지 링크 블록)는 건드리지 않는다 — 블록은 자식이 아닌 부모에 존재하므로 제거된 부모를 가리키는 깨진 페이지 링크는 발생할 수 없다.

### 3.5 영구 삭제

영구 삭제(수동 또는 퍼지 잡)는 현재의 하드 삭제 로직을 그대로 수행한다: 남은 노트에서 멘션 링크와 페이지 링크 블록 제거 → 행 삭제(cascade) → 고아 파일 정리.

## 4. 데이터 모델

### 4.1 스키마 변경

`NoteItem`(`Vanadium.Note.REST/Models/NoteItem.cs`)에 추가:

```csharp
public DateTime? DeletedAt { get; set; }   // null = active

[JsonIgnore]
public bool IsDeletionRoot { get; set; }   // true only on the note the user deleted
```

- `DeletedAt`은 휴지통 플래그와 퍼지 기준 시각을 겸한다. 기존 `UtcNowMicroseconds()` 절삭 방식으로 설정.
- `IsDeletionRoot`는 사용자가 직접 삭제한 노트와 함께 휩쓸려 들어간 하위 노트를 구분한다. 휴지통 페이지는 루트만 표시하고 복원 대상이 모호해지지 않는다.
- 한 번의 작업으로 휴지통에 들어간 서브트리 전체는 **동일한** `DeletedAt` 값을 공유한다.

### 4.2 인덱스 / 마이그레이션

- `DeletedAt`에 B-tree 인덱스 (퍼지 잡과 휴지통 목록 조회용). 부분 인덱스(`WHERE "DeletedAt" IS NOT NULL`)가 이상적이며 일반 인덱스도 무방.
- 마이그레이션: `dotnet ef migrations add AddNoteRecycleBin --project Vanadium.Note.REST` (기존 마이그레이션 수정 금지).
- 기존 행: `DeletedAt = NULL`(활성) — 데이터 백필 불필요.

### 4.3 쿼리 필터링 전략

어떤 읽기 경로에서도 휴지통 노트가 실수로 노출되지 않도록 **EF Core global query filter**를 사용한다:

```csharp
modelBuilder.Entity<NoteItem>().HasQueryFilter(n => n.DeletedAt == null);
```

휴지통을 다루는 코드 경로는 `IgnoreQueryFilters()`로 opt-out한다.

> **치명적 함정 1:** `FileCleanupService`는 업로드 파일의 고아 여부를 판단하기 위해 `db.Notes` 콘텐츠를 스캔한다 — `DeleteAllOrphansAsync()`(일일 잡)와 `DeleteOrphanedFromContentAsync()`(영구 삭제 시) **둘 다**. global filter가 적용되면 휴지통 노트의 콘텐츠가 보이지 않게 되어, 30일 유예가 끝나기 **전에** 첨부 파일이 고아로 오판되어 삭제된다. 두 스캔 모두 반드시 `IgnoreQueryFilters()`를 사용해야 한다.
>
> **치명적 함정 2:** `AccountService`("Delete all data", `SettingsController.DeleteAllData`)는 `db.Notes.Where(n => n.UserId == userId).ExecuteDeleteAsync()`로 노트를 삭제한다. global query filter는 `ExecuteDeleteAsync`에도 적용되므로, 휴지통 노트가 계정 전체 삭제에서 살아남게 된다. 여기에도 `IgnoreQueryFilters()`를 추가해야 한다.

opt-out이 필요한 경로: 휴지통 목록/복원/영구 삭제, 퍼지 잡, 두 고아 파일 스캔, 계정 전체 삭제. 그 외 전부(GetPaged, GetAllSummaries, GetChildren, Get, SearchForMention, 하위 노트 수, 제목 변경 전파, 순환 참조 검사)는 필터를 상속하므로 쿼리별 수정이 필요 없다.

## 5. API 설계

모든 엔드포인트는 `NotesController`의 기존 `GetUserId()` 패턴으로 사용자 범위가 제한된다.

| 메서드 & 라우트 | 동작 | 응답 |
|---|---|---|
| `DELETE /api/notes/{id}` | **변경**: soft delete. 해당 노트에 `DeletedAt` + `IsDeletionRoot=true`, 모든 활성 하위 노트에 동일 타임스탬프의 `DeletedAt` 설정. 참조 제거·파일 정리 없음. | 204 / 404 |
| `GET /api/notes/recycle-bin?page=&pageSize=` | 삭제 루트(`IsDeletionRoot && DeletedAt != null`)의 페이징 목록, `DeletedAt` 내림차순. `PagedResult<RecycleBinNoteSummary>` 반환. | 200 |
| `POST /api/notes/{id}/restore` | 삭제 루트와 동일 `DeletedAt`을 공유하는 모든 하위 노트를 복원. `DeletedAt`/`IsDeletionRoot` 초기화. 부모가 없거나 휴지통이면 루트로 재부착. | 204 / 404 |
| `DELETE /api/notes/{id}/permanent` | 휴지통 노트의 하드 삭제 (현재 `Delete()` 로직: 참조 제거, 삭제, 파일 정리). 휴지통 노트에만 유효 — 활성 노트는 휴지통 우회 방지를 위해 409. | 204 / 404 / 409 |
| `DELETE /api/notes/recycle-bin` | 휴지통 비우기: 사용자의 모든 휴지통 노트를 영구 삭제. | 204 |

새 DTO `RecycleBinNoteSummary` (`Vanadium.Note.REST/Models`에 생성, 컨벤션에 따라 `Vanadium.Note.Web/Models`에 미러): `Id`, `Title`, `DeletedAt`, `ChildCount`.

### 하위 노트 수집

하위 노트 깊이는 무제한이므로 서브트리 수집에는 재귀 탐색이 필요하다. `HasCircularReference`의 반복 패턴(`ParentNoteId` 기반 BFS, 깊이 제한 100)을 재사용하거나 raw 재귀 CTE를 쓸 수 있다. 단일 사용자 데이터 규모에서는 `(Id, ParentNoteId)` 쌍에 대한 인메모리 BFS로 충분하며 새 의존성도 피할 수 있다.

## 6. 백엔드 변경 (파일별)

| 파일 | 변경 |
|---|---|
| `Models/NoteItem.cs` | `DeletedAt`, `IsDeletionRoot` 추가 |
| `Models/RecycleBinNoteSummary.cs` | 신규 DTO |
| `Data/NoteDbContext.cs` | global query filter, `DeletedAt` 인덱스 구성 |
| `Migrations/` | 신규 `AddNoteRecycleBin` 마이그레이션 |
| `Services/NoteService.cs` | `Delete()` → soft delete 변경. 신규 `GetRecycleBin()`, `Restore()`, `DeletePermanent()`, `EmptyRecycleBin()`. 현재 하드 삭제 본문을 private `HardDeleteAsync(NoteItem)`으로 추출해 영구 삭제·휴지통 비우기·퍼지 잡이 공유 |
| `Services/FileCleanupService.cs` | 두 콘텐츠 스캔에 `IgnoreQueryFilters()` 추가 (4.3 함정 참조) |
| `Services/AccountService.cs` | 노트 `ExecuteDeleteAsync`에 `IgnoreQueryFilters()` 추가 (4.3 함정 참조) |
| `Services/RecycleBinPurgeJob.cs` | 신규 hosted service (7장 참조) |
| `Controllers/NotesController.cs` | 신규 엔드포인트, 구조화 로그 (`"Note {NoteId} moved to recycle bin by {UserId}"` 등) |
| `Program.cs` | `AddHostedService<RecycleBinPurgeJob>()`, `RecycleBin:RetentionDays` 바인딩 |

참고:

- `Update()`는 필터된 집합에서 노트를 조회하므로, 다른 세션에서 휴지통으로 보낸 노트에 대한 자동 저장은 자연스럽게 404 → 에디터의 기존 "not found" 처리로 이어진다. 추가 동시성 작업 불필요.
- 휴지통 노트를 가리키는 `ParentNoteId`로 `Create()` 호출 시 거부해야 한다 (404/400) — 검증만 하면 필터된 부모 조회가 자동으로 처리해 준다.

## 7. 퍼지 잡

신규 `RecycleBinPurgeJob` (`BackgroundService`, file-per-class, 기존 `OrphanFileCleanupJob` 다음에 등록). 기존 잡의 구조(시작 지연, 실행마다 scoped 서비스, Serilog 구조화 로깅, 취소 처리)를 따른다:

- 주기: 6시간마다 (퍼지 정밀도는 엄격할 필요 없음).
- 작업: `IgnoreQueryFilters()` 쿼리로 `DeletedAt < UtcNow - RetentionDays`인 노트를 찾아 삭제 루트별 서브트리를 `HardDeleteAsync`로 하드 삭제.
- 설정: `appsettings.json`의 `RecycleBin:RetentionDays` (int, 기본 `30`). 기존 설정 컨벤션에 따라 `docker-compose.yml` 환경 변수로 재정의 가능 (선택).
- 퍼지 후 파일 정리는 수동 경로와 동일한 호출을 사용하며, 일일 고아 파일 잡이 안전망으로 유지된다.

## 8. 프론트엔드 변경 (파일별)

| 파일 | 변경 |
|---|---|
| `Models/NoteSummary.cs` (Web) | 변경 불필요 (휴지통은 전용 DTO 사용) |
| `Models/RecycleBinNoteSummary.cs` (Web) | 신규 DTO 미러 |
| `Services/NoteService.cs` (Web) | 기존 `ServiceResult<T>` 패턴을 따라 신규 `GetRecycleBinAsync()`, `RestoreAsync(id)`, `DeletePermanentAsync(id)`, `EmptyRecycleBinAsync()` |
| `Pages/RecycleBin.razor` | `/recycle-bin` 신규 페이지: 목록, 복원, 영구 삭제, 휴지통 비우기. `ConfirmDialog.razor` 재사용 |
| `Pages/Home.razor` | 다이얼로그 문구 → "Move to the Recycle Bin", Undo Snackbar (`RestoreAsync` 호출) |
| `Pages/NoteEditor.razor` | 동일한 문구 변경 + Undo Snackbar, 휴지통 이동 후 홈으로 이동 |
| `Layout/NavMenu.razor` | 휴지통 메뉴 항목 추가 |

UI 텍스트는 영문만 사용 (리포지토리 컨벤션). 새 최상위 의존성 없음 — 목록, 버튼, 다이얼로그는 기존 MudBlazor 컴포넌트로 충분하다.

## 9. 엣지 케이스 & 결정 사항

1. **하위 노트를 먼저 휴지통에 보낸 뒤 부모를 삭제**: 하위 노트는 자신의 더 이른 `DeletedAt`과 `IsDeletionRoot=true`를 유지하며, 부모의 복원 그룹에 휩쓸리지 않는다 (타임스탬프가 다름). 둘 다 휴지통에 독립적으로 표시된다. 부모가 휴지통에 있는 상태에서 자식을 복원하면 루트 노트로 재부착된다.
2. **복원 대상의 부모가 이미 퍼지됨**: 동일한 폴백 — 루트로 재부착.
3. **휴지통 노트를 가리키는 멘션/페이지 링크**: 다른 노트에서 계속 렌더링되며, 클릭 시 NotFound 페이지. v1에서는 수용 (조기 제거는 복원을 손실 있게 만든다). v2 후보: "휴지통 id" 조회로 회색 처리 스타일.
4. **라벨 상호 배제**: 영향 없음 — `NoteLabels` 행은 휴지통 기간 동안 살아 있고 복원 시 그대로 돌아온다.
5. **저장 공간**: 휴지통 노트는 최대 30일 추가로 파일 첨부를 유지한다 (파일당 100 MB 상한). 수용. 휴지통 비우기로 사용자가 수동 제어 가능.
6. **전문 검색 인덱스**: `ContentText`/GIN 인덱스 변경 없음. 필터링은 인덱스가 아닌 쿼리에서 수행. 인덱스 없는 컬럼에 대한 `ILIKE`는 도입되지 않는다.
7. **`DeleteAllDataDialog` 흐름**: `AccountService`가 필터된 집합에 `ExecuteDeleteAsync`를 사용 — `IgnoreQueryFilters()` 없이는 계정 전체 삭제에서 휴지통 노트가 살아남는다 (4.3/6장에서 처리).
8. **기존 Delete 키 일괄 삭제 흐름**: 이제 기본적으로 안전. 목적지 변경 외 동작 변화 없음.

## 10. 구현 순서

1. 마이그레이션 + 엔티티 + global filter + `FileCleanupService`/`AccountService` opt-out (함정 수정은 필터와 원자적으로 함께 배포).
2. `NoteService` soft delete / 복원 / 영구 삭제 / 휴지통 조회 + 컨트롤러 엔드포인트. Swagger로 검증.
3. `RecycleBinPurgeJob` + 설정.
4. Web DTO + `NoteService` 클라이언트 메서드 + `RecycleBin.razor` + 내비게이션 + 다이얼로그 문구/Undo.
5. 문서: `CLAUDE.md` 갱신 (삭제는 이제 soft, 퍼지 잡 존재, 고아 스캔 필터 주의 사항).

## 11. 검증 체크리스트

- [ ] `dotnet build Vanadium.slnx` 클린 빌드, 신규 경고 없음.
- [ ] dev DB에 `dotnet ef database update --project Vanadium.Note.REST`.
- [ ] Swagger: 삭제 → 휴지통 목록에 표시. 복원 → `GET /api/notes`에 재등장. 영구 삭제 → 이후 404. 활성 노트 영구 삭제 → 409.
- [ ] 하위 노트가 있는 부모를 휴지통으로 → 검색에서 자식 사라짐. 복원 → 계층 구조 유지.
- [ ] 첨부 파일이 있는 노트를 휴지통으로 → 고아 스캔 대기(또는 수동 트리거) → 파일 유지 확인. 영구 삭제 → 파일 제거 확인.
- [ ] UI: 홈(일괄 + Delete 키)과 에디터에서 삭제, Undo Snackbar, 휴지통 페이지 액션, 휴지통 노트로의 멘션 링크 → NotFound.
- [ ] 퍼지 잡: dev에서 `RecycleBin:RetentionDays`를 0으로 임시 설정 후 퍼지 동작과 로그 출력 확인.

## 12. 미해결 질문

- 보드 뷰(`/board`)에 "휴지통" 드롭 타깃을 추가할 것인가? (v2 후보)
- 보존 기간을 Settings UI(`UserSettings`)로 노출할 것인가, 서버 설정만 둘 것인가? v1은 서버 설정만 가정.
