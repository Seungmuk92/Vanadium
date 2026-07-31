# 배포 및 운영 가이드

Docker Hub에 게시된 이미지로 Vanadium Note를 서버에 올리고 운영하는 방법을 정리한 문서입니다.

> **이 문서에는 실제 시크릿을 적지 마세요.** 공개 리포지토리에 포함되므로 연결 문자열, JWT
> 시크릿, 실제 password hash는 절대 커밋하지 않습니다. 예시는 모두 자리표시자입니다.

- 이미지 게시(빌드/푸시) 절차는 이 문서 마지막 [이미지 게시](#이미지-게시-관리자) 절을 참고하세요.
- REST API 계약은 `docs/api-specification.md`를 참고하세요.

## 구성

배포 단위는 컨테이너 두 개입니다. **소스 코드나 .NET SDK는 서버에 필요 없습니다.**

| 서비스 | 이미지 | 포트 | 내용 |
|---|---|---|---|
| `rest` | `smoh92/vanadium-rest` | `5000:8080` | ASP.NET Core API. 기동 시 DB 마이그레이션 자동 적용 |
| `web` | `smoh92/vanadium-web` | `80:80` | Blazor WASM 정적 파일 + nginx |

두 이미지는 `publish.ps1`이 항상 한 쌍으로 게시하므로 **같은 태그로만 배포**합니다
(`docker-compose.yml`이 둘 다 `${VANADIUM_VERSION}` 하나로 묶어둡니다).

PostgreSQL은 compose에 포함되어 있지 않습니다. 호스트나 별도 서버에서 운영 중인 인스턴스를
사용합니다.

### 요청 흐름

```
브라우저 ──(정적 파일)──▶ web (nginx, :80)
   └────(REST 호출)─────▶ rest (:5000) ──▶ PostgreSQL
```

Blazor WASM은 **브라우저에서** 실행되므로 API를 브라우저가 직접 호출합니다. 컨테이너 사이의
내부 통신이 아니라는 점이 설정에서 가장 중요한 부분입니다 ([환경 변수](#환경-변수) 참고).

## 사전 준비

- Docker Engine + Compose 플러그인
- PostgreSQL 인스턴스와 접속 계정
  - 스키마는 앱이 기동하면서 `Database.Migrate()`로 자동 생성합니다.
  - 데이터베이스 자체가 없으면 생성을 시도하므로 계정에 `CREATEDB` 권한이 필요합니다.
    미리 `createdb vanadium` 해두는 편이 확실합니다.
- 방화벽에서 열 포트: `80`(웹), `5000`(API). 리버스 프록시를 쓴다면
  [HTTPS / 리버스 프록시](#https--리버스-프록시) 절을 참고하세요.

## 설치

### 1. 파일 배치

서버에는 `docker-compose.yml`과 `.env` 두 개만 있으면 됩니다.

```bash
mkdir -p ~/vanadium && cd ~/vanadium
curl -O https://raw.githubusercontent.com/Seungmuk92/Vanadium/main/docker-compose.yml
curl -o .env https://raw.githubusercontent.com/Seungmuk92/Vanadium/main/.env.example
chmod 600 .env
```

### 2. 오너 비밀번호 해시 생성

앱은 단일 사용자(owner) 비밀번호 인증만 사용하고, 서버에는 **평문이 아니라 PBKDF2 해시**를
넣습니다. 해시를 만들어주는 `POST /api/auth/hash`는 **Development 환경에서만** 열리므로
(운영에서는 404), 다음 중 한 방법으로 뽑습니다.

**방법 A — 서버에서 REST 컨테이너를 잠깐 Development로 기동**

```bash
docker run --rm -p 5001:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e ConnectionStrings__DefaultConnection="Host=<db-host>;Port=5432;Database=vanadium;Username=<user>;Password=<pw>" \
  -e Auth__JwtSecret="$(openssl rand -base64 48)" \
  smoh92/vanadium-rest:<version>
```

다른 터미널에서:

```bash
curl -X POST http://localhost:5001/api/auth/hash \
  -H 'Content-Type: application/json' \
  -d '{"password":"<사용할 비밀번호>"}'
# → {"hash":"<base64salt>:<base64hash>:<iterations>"}
```

출력된 `hash` 값을 `.env`의 `AUTH_PASSWORD_HASH`에 넣고 이 임시 컨테이너는 종료합니다.

**방법 B — 개발 머신에서 `dotnet run`으로 REST를 띄우고 같은 엔드포인트 호출**

두 방법 모두 비밀번호 정책(15자 이상, 컨텍스트 단어 금지, 유출 이력 검사)을 통과해야 하며,
유출 검사는 외부 네트워크(HaveIBeenPwned) 접근을 사용합니다. 정책 위반은 400과 함께 사유가
반환되고 해시는 생성되지 않습니다.

### 3. `.env` 작성

```bash
VANADIUM_VERSION=0.2.0

DB_HOST=host.docker.internal
DB_PORT=5432
DB_NAME=vanadium
DB_USER=<user>
DB_PASSWORD=<password>

AUTH_JWT_SECRET=<32바이트 이상의 랜덤 문자열>
AUTH_PASSWORD_HASH=<2단계에서 생성한 값>

CORS_ALLOWED_ORIGINS=http://<서버주소>
API_BASE_URL=http://<서버주소>:5000
```

`AUTH_JWT_SECRET`은 `openssl rand -base64 48` 등으로 생성합니다.

> **값에 쓰지 말아야 할 문자가 있습니다.** 특히 `DB_PASSWORD`에서 자주 걸립니다.
>
> - **`$`** — Compose가 변수 참조로 해석합니다. `DB_PASSWORD=pa$word123`이면 `word123`이라는
>   변수를 찾다 실패하고, 컨테이너에는 `Password=pa`까지만 전달됩니다(경고만 뜨고 기동은 되므로
>   원인을 찾기 어렵습니다). `$$`로 이스케이프할 수 있지만 피하는 편이 안전합니다.
> - **`;`** — Npgsql 연결 문자열의 구분자입니다. `pa;ss`는 `Password=pa;ss;Keepalive=30`이 되어
>   `ss`가 알 수 없는 키워드로 파싱됩니다.
>
> DB 비밀번호는 영숫자와 `-`, `_` 위주로 만들고, 아래 `docker compose config`로 최종 치환 결과를
> 확인하세요.

### 4. 기동

먼저 컨테이너를 띄우지 않고 최종 설정을 확인합니다. Compose는 같은 디렉터리의 `.env`를 자동으로
읽으므로 `--env-file` 옵션은 필요 없습니다.

```bash
docker compose config
```

이 명령은 변수 치환이 끝난 결과를 그대로 보여줍니다. 다음을 확인하세요.

- `The "X" variable is not set` 경고가 없는지 (필수 변수 누락)
- `ConnectionStrings__DefaultConnection`의 `Password=` 값이 의도한 그대로인지 (특수문자로 잘리지 않았는지)
- `image:` 태그가 배포하려는 버전인지

확인이 끝나면 기동합니다.

```bash
docker compose pull
docker compose up -d
docker compose logs -f rest
```

로그에 `Applying database migrations...` → `Database migrations applied.`가 보이면 정상입니다.
브라우저에서 `http://<서버주소>` 접속 후 비밀번호로 로그인합니다.

## 환경 변수

| 변수 | 필수 | 기본값 | 설명 |
|---|:--:|---|---|
| `VANADIUM_VERSION` | | `latest` | 두 이미지에 공통 적용되는 태그 |
| `DB_HOST` | | `host.docker.internal` | 같은 호스트의 PostgreSQL을 가리키는 기본값 |
| `DB_PORT` | | `5432` | |
| `DB_NAME` | | `vanadium` | |
| `DB_USER` | | `postgres` | |
| `DB_PASSWORD` | ✅ | | |
| `AUTH_JWT_SECRET` | ✅ | | HS256 서명 키. **UTF-8 기준 32바이트 미만이면 기동 시 예외로 종료** |
| `AUTH_PASSWORD_HASH` | ✅ | | `salt:hash:iterations` 형식. 미설정 시 로그인이 500 |
| `CORS_ALLOWED_ORIGINS` | | `http://localhost` | 웹이 서비스되는 origin. 여러 개면 콤마 구분 |
| `API_BASE_URL` | | `http://localhost:5000` | **브라우저가** API를 호출할 주소 |
| `RECYCLE_BIN_RETENTION_DAYS` | | `30` | 휴지통 자동 영구삭제 기준일 |
| `SEQ_URL` / `SEQ_API_KEY` | | (없음) | 설정 시 Seq로 구조적 로그 전송, 미설정 시 콘솔만 |

### `API_BASE_URL`과 `CORS_ALLOWED_ORIGINS`

이 둘이 가장 흔한 실수 지점입니다.

- `API_BASE_URL`은 컨테이너 내부 주소(`http://rest:8080`)가 **아닙니다.** Blazor WASM이
  브라우저에서 실행되므로 사용자의 브라우저가 도달할 수 있는 주소여야 합니다. 이 값은
  이미지 안의 `appsettings.template.json`에 컨테이너 기동 시 `envsubst`로 주입됩니다.
- 웹 페이지의 origin이 `CORS_ALLOWED_ORIGINS`에 없으면 브라우저가 모든 API 호출을 차단합니다.

즉 두 값은 항상 짝으로 맞춰야 합니다.

| 배포 형태 | `API_BASE_URL` | `CORS_ALLOWED_ORIGINS` |
|---|---|---|
| IP 직접 노출 | `http://203.0.113.10:5000` | `http://203.0.113.10` |
| 도메인 + 리버스 프록시 | `https://note.example.com/api` | `https://note.example.com` |

## `docker-compose.yml` 수정이 필요한 경우

접속 정보는 전부 `.env`로 빠져 있으므로 **보통은 파일을 그대로 사용**하면 됩니다. 다음 세 경우만
YAML을 직접 손봐야 합니다.

**1. 리버스 프록시 뒤에 둘 때** — `ForwardedHeaders__KnownProxies__0` 주석을 해제합니다
([HTTPS / 리버스 프록시](#https--리버스-프록시) 참고).

이 항목만 환경 변수로 빼두지 않은 이유가 있습니다. 값이 IP 목록(배열)이라 빈 문자열이 들어가면
ASP.NET이 파싱에 실패합니다. `${KNOWN_PROXY:-}` 형태로 만들면 프록시를 쓰지 않는 배포에서 오히려
기동이 깨지므로, 필요할 때만 주석을 해제하는 편이 안전합니다.

**2. 포트가 이미 사용 중일 때** — 호스트에서 `80`이나 `5000`을 쓰고 있으면 `ports`를 바꿉니다.
프록시를 앞에 두는 경우 API는 아예 외부에 노출하지 않는 편이 낫습니다.

```yaml
  rest:
    # ports:
    #   - "5000:8080"      ← 제거하고
    expose:
      - "8080"             ← 프록시(같은 네트워크)에서만 접근
```

**3. PostgreSQL도 컨테이너로 띄우고 싶을 때** — compose에 DB 서비스가 없습니다. 외부 인스턴스를
사용하는 전제이므로 직접 서비스를 추가해야 합니다.

## HTTPS / 리버스 프록시

앱은 컨테이너 안에서 HTTP 전용이며 HTTPS 리다이렉트를 **의도적으로 하지 않습니다**(프록시 뒤에서
리다이렉트 루프가 생기므로). TLS 종료는 앞단의 nginx/Caddy 등이 담당합니다.

프록시를 둘 때는 `docker-compose.yml`의 주석 처리된 항목을 반드시 활성화하세요.

```yaml
    environment:
      ForwardedHeaders__KnownProxies__0: "172.20.0.5"      # 프록시 IP
      # 또는
      ForwardedHeaders__KnownNetworks__0: "172.16.0.0/12"  # 프록시 서브넷(CIDR)
```

- 설정하지 않으면 `X-Forwarded-For`를 신뢰하지 않으므로 모든 요청이 프록시 IP 하나로 묶여
  로그인 rate limiter가 정상 동작하지 않습니다.
- 반대로 아무 출처나 신뢰하면 앱 포트에 직접 접근한 클라이언트가 헤더를 위조해 IP 단위 제한을
  우회할 수 있습니다. 그래서 기본값은 "아무도 신뢰하지 않음"입니다.

프록시를 쓰는 경우 `5000` 포트 직접 노출을 막고 API도 프록시 뒤로 넣는 것을 권장합니다.
`ForwardedHeaders`가 scheme까지 복원한 뒤에만 HSTS가 적용됩니다.

## 업데이트와 롤백

```bash
# .env 의 VANADIUM_VERSION 을 새 버전으로 변경한 뒤
docker compose pull
docker compose up -d
```

- 업로드 파일은 `vanadium_uploads` 볼륨에 남으므로 컨테이너를 재생성해도 유지됩니다.
- DB 마이그레이션은 기동 시 자동 적용됩니다.
- 롤백은 `VANADIUM_VERSION`을 이전 값으로 되돌리고 다시 `up -d` 하면 됩니다. 다만
  **마이그레이션은 자동으로 되돌아가지 않습니다.** 스키마 변경이 포함된 버전에서 내려올 때는
  이전 버전 코드가 새 스키마와 호환되는지 먼저 확인하세요.

## 백업

백업 대상은 두 가지입니다.

```bash
# 1) 데이터베이스
pg_dump -h <db-host> -U <user> vanadium | gzip > vanadium-$(date +%F).sql.gz

# 2) 업로드 파일 볼륨 (실제 볼륨명은 docker volume ls 로 확인 — compose 프로젝트명이 접두사로 붙습니다)
docker run --rm -v vanadium_vanadium_uploads:/data -v "$PWD":/backup alpine \
  tar czf /backup/uploads-$(date +%F).tar.gz -C /data .
```

`.env`도 함께 안전한 곳에 보관하세요. `AUTH_PASSWORD_HASH`와 `AUTH_JWT_SECRET`을 잃어버리면
로그인이 불가능해집니다(해시는 재생성 가능하지만, `AUTH_JWT_SECRET`이 바뀌면 발급된 JWT는 모두
무효가 됩니다).

## 운영 메모

- **로그**: `docker compose logs -f rest`. `SEQ_URL`을 설정하면 Seq로 구조적 로그가 전송됩니다.
  Seq 포트(5341)는 외부에 노출하지 마세요.
- **로그인 제한**: 로그인 엔드포인트는 IP당 1분 10회 제한이 있고, 실패가 누적되면 전역 잠금이
  걸립니다. 잠금 중에도 올바른 비밀번호는 통과하므로 스스로 잠기지 않습니다. 429 응답에는
  `Retry-After` 헤더가 포함됩니다.
- **API 직접 호출**: 자동화 용도로는 로그인 JWT 대신 개인 액세스 토큰(PAT)을 발급해 사용합니다.
  엔드포인트와 인증 방식은 `docs/api-specification.md`를 참고하세요.
- **Swagger UI**는 Development에서만 열립니다. 운영 배포에는 노출되지 않습니다.

## 문제 해결

| 증상 | 원인 / 조치 |
|---|---|
| `rest` 컨테이너가 기동 직후 종료, 로그에 `Auth:JwtSecret must be at least 32 bytes` | `AUTH_JWT_SECRET`이 짧습니다. 32바이트 이상으로 교체 |
| 로그인 시 500, 로그에 `Auth:PasswordHash is not configured` | `AUTH_PASSWORD_HASH` 미설정 |
| 비밀번호가 맞는데 계속 401 | 해시 형식(`salt:hash:iterations`) 확인. 따옴표나 줄바꿈이 섞이지 않았는지 확인 |
| 429와 `Retry-After` | 로그인 실패 누적에 의한 잠금. 헤더의 초만큼 대기 |
| 웹 화면은 뜨는데 모든 API 호출 실패, 브라우저 콘솔에 CORS 오류 | `CORS_ALLOWED_ORIGINS`에 실제 접속 origin이 없음 |
| 브라우저 콘솔에 `ERR_CONNECTION_REFUSED` | `API_BASE_URL`이 브라우저에서 도달 불가능한 주소(예: `rest`, `localhost`) |
| DB 비밀번호가 맞는데 인증 실패, 또는 `Keyword not supported` 류의 연결 오류 | `DB_PASSWORD`에 `$`(Compose가 변수로 해석) 또는 `;`(연결 문자열 구분자)가 포함됨. `docker compose config`로 실제 전달값 확인 |
| `up -d` 시 `port is already allocated` | 호스트에서 80/5000 사용 중. [수정이 필요한 경우](#docker-composeyml-수정이-필요한-경우) 참고 |
| 기동 시 DB 연결 실패 | `DB_HOST` 확인. 호스트의 PostgreSQL을 쓰는 경우 `listen_addresses`와 `pg_hba.conf`가 컨테이너 네트워크를 허용하는지 확인 |
| 마이그레이션 단계에서 권한 오류 | DB 계정 권한 부족. 데이터베이스를 미리 생성하거나 `CREATEDB` 부여 |
| 리버스 프록시 뒤에서 rate limit이 모든 사용자에게 동시에 걸림 | `ForwardedHeaders__KnownProxies__0` 미설정 |
| `POST /api/auth/hash`가 404 | Development 환경이 아님. 위 [비밀번호 해시 생성](#2-오너-비밀번호-해시-생성) 참고 |

## 이미지 게시 (관리자)

개발 머신(Windows)에서 실행합니다.

```powershell
docker login
.\publish.ps1 -Version 0.2.0
```

`publish.ps1`은 두 이미지를 한 쌍으로 빌드·푸시한 뒤 `v0.2.0` git 태그를 생성·푸시합니다.
Docker 작업을 시작하기 전에 다음을 먼저 검사하고 실패하면 즉시 중단합니다.

- Docker Hub 로그인 여부
- 동일한 git 태그가 로컬/원격에 이미 있는지
- 워킹 트리가 깨끗한지
- `dotnet test Vanadium.slnx` 통과 여부

| 스위치 | 용도 |
|---|---|
| `-Platform` | 기본 `linux/amd64`. 콤마 목록(`linux/amd64,linux/arm64`)이면 멀티아치 매니페스트를 만듭니다(QEMU 에뮬레이션이라 느립니다) |
| `-NoLatest` | `:latest`를 이 버전으로 옮기지 않음. 구버전 라인을 재게시할 때 사용 |
| `-SkipTests` | 테스트 게이트 생략 |
| `-SkipLoginCheck` | 로그인 사전 검사 생략 |
| `-NoGitTag` | 이미지만 게시하고 git 태그는 만들지 않음 |
| `-AllowDirty` | 워킹 트리가 더러워도 태그 생성 허용 |

빌드와 푸시는 `docker buildx build --push` 한 번으로 처리되므로, 한 이미지만 푸시되고 다른
하나는 실패하는 상태가 생기지 않습니다.

> **`.dockerignore`는 보안 장치입니다.** 두 Dockerfile 모두 `COPY . .`을 사용하고 Web SDK는
> `**/*.json`을 기본 publish 대상에 포함하므로, `Vanadium.Note.REST/.dockerignore`의
> `appsettings.Development.json` 항목을 제거하면 로컬 개발용 DB 비밀번호와 JWT 시크릿이 공개
> 이미지에 그대로 포함됩니다. 시크릿이 담긴 파일을 빌드 컨텍스트에 추가할 때는 반드시 함께
> 무시 목록에 넣으세요.
