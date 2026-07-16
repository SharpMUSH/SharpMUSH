# Area 1: Authentication & Identity — TODO

## Pre-Implementation
- [x] Review & confirm decisions (1.1–1.5) with project owner
- [x] Identify any decisions that need revision based on current codebase state — full ASP.NET Identity was replaced with a custom `SharpAccount` model + JWT/session stores (simpler fit for the game DB); the decision intent (account-level auth) is preserved

## Implementation Tasks
- [x] Account model + auth wiring (custom `SharpAccount` + JWT bearer / DebugAuth schemes in `Startup.cs`; full ASP.NET Identity intentionally not used)
- [x] Account endpoints: register (`POST /api/auth/account-register`), login (`account-login`), refresh (`jwt-refresh`), logout (`POST /api/account/logout`)
- [x] JWT generation with character claims (role, character_key, account id) — `JwtService.cs`
- [x] Refresh token rotation (httpOnly cookie) — single-use rotation in `InMemoryRefreshTokenStore`; refresh token now also issued as an httpOnly `sharpmush_refresh` cookie (path `/api/auth`), accepted as fallback on `jwt-refresh`, cleared on logout/invalid refresh
- [x] Character-switching endpoint (new JWT with different character claims) — `POST /api/auth/jwt-switch-character`
- [x] Role claim derivation from game flags (WIZARD→Wizard, ROYALTY→Royalty, #1→God) — `RoleDerivationService.cs`
- [x] Account-level role = max role among linked characters — `RoleDerivationService.DeriveAccountRole`
- [x] Registration flow (account creation + first character link) — register then `POST /api/account/characters` (two calls by design)
- [x] Add "link existing character" flow (character password verification) — `POST /api/account/link-character`; verifies the character's MUSH password, rejects characters linked to another account (409)
- [x] First-run setup wizard claims pre-generated admin (ServerState.SetupCompleted; no bootstrap credentials)
- [x] Login matrix: character passwords/names accepted as account credentials
- [x] Server-side MustChangePassword enforcement; account disable; net logins/player_creation enforcement
- [x] debug-ott gated to Development; admin accounts API + /admin/accounts page; @account command

## Testing
- [x] Unit tests: JWT generation (`JwtServiceTests`), role derivation (`RoleDerivationServiceTests`), token refresh (`InMemoryRefreshTokenStoreTests`), account service (`AccountServiceTests`), session store (`InMemoryAccountSessionStoreTests`)
- [x] Integration tests: register/login (success, duplicate, wrong password), character switch, refresh rotation + reuse rejection, refresh cookie issuance, OTT via account session, link-character (wrong password / conflict / relink) — `SharpMUSH.Tests.Integration/Auth/AuthHttpControllerTests.cs`
- [x] Verify no character password required after account auth — `AccountLogin_CorrectPassword_ReturnsSessionAndCharacters` and `MushToken_ViaAccountSession_IssuesOttWithoutCharacterPassword`

## Follow-ups
- ~~Persistent (DB-backed) refresh-token / session stores for multi-instance deployments (currently in-memory)~~ — done: see auth-consolidation below
- ~~Expired-JWT handling test at the API level (clock-skew window makes this awkward in-process; covered by `ValidateLifetime` config)~~ — moot: JWT retired, see below

## Auth Consolidation & Ban Enforcement (done)
- [x] DB-backed account-session store replaces the in-memory session/refresh-token stores; sessions are durable and revocable across restarts
- [x] JWT + httpOnly refresh cookie retired — the DB-backed account-session token is the single web credential, authenticating both REST (`AccountSession` scheme) and SignalR (`GameHub`); `JwtService` / `IRefreshTokenStore` / `jwt-refresh` / `jwt-switch-character` removed — see `docs/design/architectural-decisions.md` §1.2 for the reversal rationale
- [x] Roles/permissions resolve server-side per request/connection (FusionCache-cached) instead of being embedded in a token payload
- [x] `BanEnforcementService`: `@account/disable` and matching `@sitelock` rules revoke the account session and immediately drop live telnet/WebSocket handles (via NATS) and abort in-progress SignalR connections
- [x] `SitelockMatcher` (glob + CIDR) gates auth surfaces (register/login/etc.); anonymous browsing remains open
- [x] Trusted forwarded-headers handling for client IP resolution (spoof-resistant, proxy-aware)
- [x] `@sitelock` in-game command for mutating sitelock rules
