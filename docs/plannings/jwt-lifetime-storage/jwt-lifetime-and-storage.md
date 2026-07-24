# Decision Record: JWT Lifetime & Token Storage

Status: Decided
Author: smoh (written with Claude)
Date: 2026-07-20
Issue: #215 — localStorage JWT 수명 단축 / httpOnly 쿠키 검토
Related: #199 (Content-Security-Policy)

## 1. Context

Vanadium is a single-user, password-only app. A successful login mints a JWT
(`AuthController.GenerateJwtToken`) that the Blazor WASM frontend stores in
browser `localStorage` (`TokenStore`) and parses client-side
(`JwtAuthenticationStateProvider`) to expose auth state.

Two properties of this design compound each other:

- **No server-side revocation.** There are no refresh tokens and no server session
  store (a deliberate constraint, see CLAUDE.md "No refresh tokens"). Once a JWT is
  issued it is valid until it expires; nothing can invalidate it early.
- **Readable by JavaScript.** A token in `localStorage` is reachable by any script
  running on the origin, so a single XSS foothold can exfiltrate it.

Together these mean **the JWT lifetime is the entire theft-exposure window**: if a
token leaks, an attacker has full owner access until it expires, with no way to cut
it off. The prior default was **1440 minutes (24h)**.

## 2. Decision

### 2.1 Shorten the JWT lifetime (adopted)

The default `Auth:JwtExpirationMinutes` is reduced **1440 → 480 (8 hours)**, in
`appsettings.json`, `appsettings.Development.json`, and the code fallback in
`AuthController.GenerateJwtToken`.

Rationale: 8 hours covers a normal working session without forcing constant
re-login, while cutting the un-revocable exposure window to a third of the previous
value. Because there are no refresh tokens, the lifetime cannot be pushed much
lower without a materially worse UX (the user must fully re-login at each expiry).
The value stays configuration-driven, so a deployment with stricter requirements
can lower it further without a code change.

### 2.2 httpOnly cookie storage (considered, deferred)

Moving the token to an `HttpOnly` cookie would put it out of reach of page
JavaScript and largely neutralise the `localStorage` XSS-exfiltration path. It is
**not adopted now** for the following reasons:

- **The frontend depends on reading the token.** `JwtAuthenticationStateProvider`
  parses the JWT in the browser to derive auth state, and `AuthTokenHandler`
  attaches it as a bearer header per request. An `HttpOnly` cookie is by definition
  unreadable by that code, so this is not a config toggle — it requires reworking
  how the WASM client learns it is authenticated and how the API authenticates
  requests (cookie auth scheme server-side).
- **Cookies reintroduce CSRF surface.** Bearer headers are immune to CSRF; an
  ambient cookie is not, so adopting it pulls in anti-forgery handling that the
  current stateless bearer model does not need.
- **Revocation is the deeper gap.** An `HttpOnly` cookie hides the token from
  script but still cannot be revoked server-side under the no-refresh-token
  constraint, so it does not by itself close the "can't invalidate a leaked token"
  hole — that would need the refresh-token / session work that is explicitly out of
  scope here (CLAUDE.md, and issue #215 범위 밖).

The decision is therefore recorded here rather than implemented. Revisiting it is
best coupled with a future refresh-token / session discussion, since the two share
the same server-side surface.

### 2.3 CSP to shrink the XSS surface (adopted elsewhere: #199)

The most effective mitigation for the `localStorage` token is to prevent the XSS in
the first place. A Content-Security-Policy that constrains script sources is tracked
separately in **#199** and is the primary follow-up; it is out of scope for this
change, which is limited to the token-lifetime knob and this record.

## 3. Outcome vs. acceptance criteria (#215)

- **JWT lifetime adjusted to a reasonable level vs. risk** — 24h → 8h default,
  still configuration-overridable.
- **httpOnly cookie method or its decision rationale documented** — deferral and
  rationale recorded in §2.2 above.
- **Build clean** — no code-shape change beyond the default constant and a comment;
  `dotnet build Vanadium.slnx` stays clean.
