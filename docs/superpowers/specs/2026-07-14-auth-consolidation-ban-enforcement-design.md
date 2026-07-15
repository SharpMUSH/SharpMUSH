# Auth Consolidation & Immediate Ban Enforcement — Design

**Date:** 2026-07-14
**Status:** Approved by project owner (pending spec review)
**Branch context:** builds on `feature/account-login-setup` (PR #691).

## Goal

Make `@sitelock` host/IP bans and account disables take effect **immediately**
across every connection surface — killing live telnet, websocket, and SignalR
connections within seconds, revoking sessions, and refusing re-entry — by
consolidating the web credential model onto a single revocable server-side
session token (retiring the JWT + refresh-token hybrid).

## Scope decisions (from brainstorming)

- **Ban vectors in scope:** `@sitelock` host/IP rules; account disable
  (`@account/disable` + admin API). Character-level bans (`@boot` as a ban) are
  out of scope, though the disconnect plumbing this builds also fixes `@boot`'s
  socket teardown in passing.
- **"Immediate" = kill live connections too:** matching live connections drop
  within seconds; sessions revoked; reconnection refused.
- **Credential model:** consolidate to sessions (retire JWT + refresh).
- **Web reach:** auth surfaces only. Sitelocked hosts may still view public
  pages; they cannot register, log in, mint OTTs, claim setup, or open game
  connections. No per-request IP block on anonymous browsing.
- **`@sitelock` trigger:** implement the currently-stubbed in-game `@sitelock`
  mutation as part of this work (option (a)).

## Findings that shaped the design

From an exploration of the current code:

1. **Prod JWT + SignalR is effectively unwired.** `GameHub` authenticates on a
   `character_dbref` claim that only `DebugAuthenticationHandler` and
   `MushBasicAuthenticationHandler` emit — `JwtService` never sets it. There is
   also no `OnMessageReceived` handler reading the SignalR `?access_token=`
   query param that WebSocket transport requires. So the JWT→GameHub path only
   works in dev (where DebugAuth is the default scheme). Retiring JWT does not
   remove working production functionality.
2. **No account-session auth scheme exists.** Session tokens are validated
   ad-hoc inside controller bodies via `IAccountSessionStore.ValidateAsync`.
   Consolidation introduces a proper `AccountSession` `AuthenticationHandler`.
3. **`@sitelock` in-game mutation is stubbed** (`WizardCommands.cs` ~2163–2211
   return `NotImplemented`). Only the REST `SitelockController` persists rules
   today. Host matching is glob-only (`WildcardMatch`, `WizardCommands.cs:2222`)
   — no CIDR.
4. **No forwarded-headers handling.** Web-side client IP
   (`HttpContext.Connection.RemoteIpAddress`) is the proxy IP behind
   Caddy/Cloudflare. Telnet/websocket client IPs arrive truthfully via the
   ConnectionServer path.
5. **Sessions/OTTs/refresh tokens are 100% in-memory.** A DB-backed session
   store is a prerequisite (also fixes "server restart logs everyone out").

## Section 1 — Architecture

Two cooperating pieces: **credential consolidation** and a **ban-enforcement
core**.

The single web credential becomes the opaque **account-session token**, moved
from in-memory to DB-backed storage. Each session record gains an **origin IP**
so host-based sitelock rules can target live sessions. Roles/permissions are
resolved server-side per request via `AccountClaimsService` behind FusionCache
— no claims baked into a token, so a privilege change or ban is visible on the
next request, not at token expiry.

A **`BanEnforcementService`** is the chokepoint every ban flows through. When a
sitelock host rule is added or an account is disabled, it runs three fan-outs
(revoke sessions, drop live game connections, drop live portal connections) —
detailed in Section 2. Separately, **sitelock checks** guard each auth *entry*
surface, resolving the real client IP through forwarded headers. Anonymous web
browsing is never gated.

## Section 2 — Ban-enforcement data flow

`BanEnforcementService` (server-side) is invoked the instant a ban is recorded
— from the sitelock persist path (`SitelockController` and the new `@sitelock`
mutation) and from `AccountService.DisableAccountAsync`. It runs three
fan-outs:

1. **Sessions.** `IAccountSessionStore` gains `RevokeAllForIpAsync(ip)` beside
   the existing `RevokeAllForAccountAsync`. The session `Entry` gains
   `OriginIp`, captured at token creation from the forwarded-header-resolved
   client IP. Because roles/permissions now resolve server-side per request,
   the next request from a revoked session is unauthenticated immediately.

2. **Live game connections (telnet/websocket).** The server already knows every
   handle's IP and player via `ConnectionService` metadata
   (`InternetProtocolAddress`, bound `DBRef`/`AccountId`). `BanEnforcementService`
   resolves matching handles and publishes the **existing**
   `DisconnectConnectionMessage(handle, reason)` on the Server→ConnectionServer
   NATS channel per handle; `DisconnectConnectionConsumer` tears down the
   socket. **Bonus fix:** `@boot` currently calls `ConnectionService.Disconnect`
   (server-side notification only) and never publishes this NATS message, so it
   doesn't truly close the socket — this work routes `@boot` through the same
   publish so it does.

3. **Live portal connections (SignalR).** `GameHub` gains a lightweight
   `connectionId → (accountId, originIp)` registry populated in
   `OnConnectedAsync` and cleared in `OnDisconnectedAsync`.
   `BanEnforcementService` aborts matching connections via the hub lifetime
   manager (`Context.Abort()` / `HubLifetimeManager`). SignalR auto-reconnect
   then fails the `[Authorize]` re-check, which is now backed by the revoked
   session.

**Match resolution:** for an account disable, match by `accountId`. For a
sitelock host rule, match by origin IP/hostname using the shared
`SitelockMatcher` (Section 4) against session `OriginIp`, connection metadata
IP, and the SignalR registry IP.

## Section 3 — Credential consolidation blast radius

The opaque account-session token becomes the single web credential.

- **New `AccountSessionAuthenticationHandler`** (scheme `AccountSession`)
  validates the bearer token against the session store and builds the principal
  by resolving role/permissions server-side (Section 5's cache) plus the
  `character_dbref` claim `GameHub` needs. Replaces ad-hoc in-controller
  validation; becomes the default scheme in dev-JWT-off and production.
  `DebugAuth` remains the dev default when no real credential is present.
- **SignalR handshake** authenticates via the session token: an
  `OnMessageReceived`-style query reader pulls the token from `?access_token=`
  (the gap that made prod JWT-over-WebSocket non-functional), so REST and the
  hub authenticate uniformly. `GameHub` gets a real `character_dbref` in prod.
- **Retired:** `JwtService`/`IJwtService`, `JwtOptions`,
  `InMemoryRefreshTokenStore`/`IRefreshTokenStore`, the `jwt-login` /
  `jwt-switch-character` / `jwt-refresh` endpoints, the `sharpmush_refresh`
  cookie, and `Jwt:SigningKey` config/ops. Character-switching becomes a
  session-based `POST api/auth/switch-character` that re-scopes the existing
  session's active character (no new token family).
- **Client:** `GameHubConnectionFactory` takes the session token instead of a
  JWT access token; `AccountAuthService` drops JWT/refresh handling.
- **Docs:** `docs/design/architectural-decisions.md` §1.2 is formally revised to
  record the reversal and its rationale (single-instance deployment,
  revocation-heavy domain, JWT+hub path never wired in prod).

## Section 4 — Sitelock matching & client IP

- **Forwarded headers:** add `UseForwardedHeaders`
  (`X-Forwarded-For` / `CF-Connecting-IP`) before `UseAuthentication`, with a
  configured known-proxy/known-network allowlist so a client cannot spoof its
  origin. Makes web-side origin IP trustworthy behind Caddy/Cloudflare;
  telnet/websocket IPs already arrive truthfully via the ConnectionServer.
- **Matching:** promote the glob-only `WildcardMatch` into a `SitelockMatcher`
  that also understands CIDR (`10.0.0.0/8`) and bare-IP rules, evaluated
  against the resolved client IP and hostname. One matcher, used by both the
  connect-time checks and `BanEnforcementService`.
- **Auth-surface checks (anonymous browsing stays open):** the matcher gates
  telnet `connect`/`register`/`guest`, the REST auth endpoints
  (login/register/setup-complete/OTT-mint), and the SignalR/websocket
  handshake. Sitelock flags map to surfaces: `!connect` → game connections +
  login, `!create` → register/setup, `!guest` → guest. Public page rendering is
  never gated.
- **`@sitelock` mutation:** implement the stubbed BAN/REGISTER/REMOVE/NAME
  paths — persist via `ISharpDatabase.SetExpandedServerData(nameof(SharpMUSHOptions), …)`
  then `ConfigurationReloadService.SignalChange()` (the pattern
  `SitelockController` already uses), and invoke `BanEnforcementService` on
  add.

## Section 5 — Role/permission caching & testing

- **Caching:** wrap `AccountClaimsService` resolution in FusionCache keyed by
  account, short TTL, with explicit invalidation on the events that change it
  (role/flag change, account disable, sitelock affecting the account). Keeps
  per-request server-side resolution near-free while staying instantly correct
  — a disabled account is invalidated then session-revoked, so it is locked out
  immediately.
- **DB-backed session store (prerequisite):** new `ISharpDatabase` region (or a
  dedicated store) persisting `{token, accountId, expiry, ttl, originIp}` with
  revoke-by-token, revoke-all-by-account, revoke-all-by-ip; implemented across
  ArangoDB, SurrealDB, Memgraph. OTT/refresh stay in-memory (refresh is being
  retired; OTT is 60s single-use).

### Testing (TUnit + Testcontainers via Podman; bUnit for client)

- DB-backed session store round-trips across all three providers (create,
  validate-slides-expiry, revoke-by-token/account/ip).
- `AccountSessionAuthenticationHandler`: accepts valid, rejects
  revoked/expired; principal carries role/permission/`character_dbref` claims.
- **Enforcement matrix** (integration): disable an account → (i) next REST
  request 401, (ii) `DisconnectConnectionMessage` published for its handles,
  (iii) matching SignalR connection aborted. Add a sitelock host rule → live
  matching telnet/websocket handles dropped and new connects refused, while
  anonymous page GETs still 200.
- `SitelockMatcher` unit tests: glob, CIDR, bare IP, hostname, negatives.
- Forwarded-header spoof-resistance test (untrusted proxy IP ignored).
- `@sitelock BAN`/`REMOVE` mutation → persist → reload → enforcement
  end-to-end.
- Regression: `jwt-*` endpoints removed; client authenticates the hub via
  session token; `@boot` now publishes `DisconnectConnectionMessage`.

## Out of scope

- Character-level ban flags (a character-ban system distinct from `@boot`).
- Multi-instance session sharing beyond DB-backing (the DB store is the
  prerequisite; cross-instance SignalR backplane is a separate concern).
- Re-introducing JWT for third-party API consumers (a future, audience-specific
  addition if ever needed).
- Email/2FA/OAuth (unchanged from the account-login work).

## Key files

- Retire: `SharpMUSH.Server/Authentication/JwtService.cs`, `IJwtService`,
  `JwtOptions`, `JwtTokenResult`, `InMemoryRefreshTokenStore`,
  `IRefreshTokenStore`; JWT endpoints in
  `SharpMUSH.Server/Controllers/AuthController.cs`.
- Add: `SharpMUSH.Server/Authentication/AccountSessionAuthenticationHandler.cs`,
  `SharpMUSH.Server/Services/BanEnforcementService.cs`,
  `SharpMUSH.*/SitelockMatcher` (shared), a DB-backed session store
  (`ISharpDatabase` + three providers), forwarded-headers wiring in
  `SharpMUSH.Server/Program.cs`/`Startup.cs`.
- Modify: `IAccountSessionStore` + impl (origin IP, `RevokeAllForIpAsync`),
  `SharpMUSH.Server/Hubs/GameHub.cs` (session auth, connection registry,
  force-disconnect), `AccountService.DisableAccountAsync` (invoke enforcement),
  `SharpMUSH.Server/Controllers/SitelockController.cs` (invoke enforcement),
  `WizardCommands.cs` (`@sitelock` mutation; `@boot` NATS publish),
  `AccountClaimsService` (FusionCache), `SharpMUSH.Client` GameHub factory +
  `AccountAuthService`, `docs/design/architectural-decisions.md`.
