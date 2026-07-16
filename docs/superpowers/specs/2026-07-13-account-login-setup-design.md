# Account / Login Experience: First-Run Setup & Hardening — Design

**Date:** 2026-07-13
**Status:** Approved by project owner (pending spec review)

## Goal

Remove the default bootstrap admin credentials from production deployments
(e.g. Docker at mush.sharpmush.com) and replace them with a first-run setup
wizard, while hardening the account/login stack and adding admin-driven
account management.

## Background & constraints

- `BootstrapService` pre-generates an admin account linked to God (#1) on
  first startup. Today its password comes from `SHARPMUSH_BOOTSTRAP_PASSWORD`
  (required by `deploy/docker-compose.prod.yml`, `devpassword` in Development
  appsettings) or a random password printed to the log. **Constraint: the
  first admin account stays pre-generated** — only the credential mechanism
  changes.
- God (#1) is seeded with an empty character password hash. **Constraint:
  this is intentional PennMUSH-default behavior** — `connect God <anything>`
  works until a password is set. It must be preserved.
- A first-run wizard (`SetupController` + `Setup.razor`) already exists but is
  dead code: it gates on "no accounts exist", and `BootstrapService` has
  always created one before anyone loads the portal.
- `@password` / `@newpassword` change **character** passwords. The project
  wants character-like login: character passwords are also valid account
  credentials (full matrix below).
- Decisions made during brainstorming: setup wizard is **first visitor wins**
  (no claim token); `SHARPMUSH_BOOTSTRAP_*` env vars are **removed entirely**;
  password recovery is **admin-driven only** (no email infrastructure yet).

### Security findings driving the hardening scope

1. `GET api/auth/debug-ott` is guarded only by `[Authorize]`. In production
   the default scheme is JWT bearer, so **any logged-in player** can call it
   and receive an OTT for God #1 plus the bootstrap admin's account session
   token — privilege escalation to God.
2. `MustChangePassword` is advisory: it is returned to the client but never
   enforced server-side; a flagged account's session token works everywhere.
3. `Net.PlayerCreation` and `Net.Logins` config options are defined but
   enforced nowhere.
4. `DisableAccountAsync` is a TODO stub; `IsDisabled` is checked at login but
   nothing can set it.

## Section 1 — Global setup state & first-run flow

### ServerState

A new MUSH-wide `ServerState` document (one per game) with
`SetupCompleted: bool`. `ISharpDatabase` gains:

- `GetServerStateAsync()`
- `SetServerSetupCompletedAsync(bool)`

implemented in all three providers (ArangoDB, Memgraph, SurrealDB). A
migration creates the document. **Upgrade inference:** the migration sets
`SetupCompleted = true` when any account with a non-empty password hash
already exists, so a live game (mush.sharpmush.com) never re-opens its
wizard on upgrade.

The flag is game state, not account state (deliberately not an
`IsUnclaimed` field on `SharpAccount`).

### Credential-free bootstrap

`BootstrapService` keeps pre-generating the `admin` account linked to #1 when
no accounts exist — but with an **empty password hash**, mirroring God's
empty character password. Removed entirely: `SHARPMUSH_BOOTSTRAP_USERNAME` /
`SHARPMUSH_BOOTSTRAP_PASSWORD`, `BootstrapOptions.AdminUsername` /
`AdminPassword`, the `devpassword` Development default, password generation,
and the log banner.

Empty hashes never match in account login (see Section 2), so the
pre-generated account cannot be logged into through the account stack until
claimed. Telnet keeps PennMUSH first-login behavior untouched.

### The wizard claims the game

`SetupController`:

- `GET api/setup/status` → `NeedsSetup = !SetupCompleted`.
- `POST api/setup/complete` (first visitor wins): takes username + password,
  renames the #1-linked account, sets its password, flips `SetupCompleted`.
  Atomically guarded (single-flight) so concurrent claims cannot both
  succeed; the loser receives 409. A username collision with an existing
  account is rejected (409) without consuming the claim. If no #1-linked
  account exists (edge case), it is created and linked.

`Setup.razor` is reworked onto this, and the portal redirects to `/setup`
from **any** route while setup is incomplete (today only `/` is checked).

### Telnet-only escape hatch

If an operator never opens the portal (classic `connect God x` →
`@password`), the wizard must not stay open forever. Setup **also**
completes when:

- God's (#1) **character** password is set (`@password`, `@newpassword`) —
  the natural telnet-only claim. Note: pre-setup, no account-level path can
  authenticate to the admin account (empty hashes never match), so the
  character-password path is the only organic non-wizard trigger; or
- a wizard runs the explicit in-game command `@account/setupcomplete`
  (Section 4).

## Section 2 — Login matrix

`AccountService.AuthenticateAsync` is extended to:

| Identifier | Accepted passwords |
|---|---|
| Account username | Account password, or any linked character's password |
| Account email | Same |
| Character name | That character's password → logs into the owning account |

Rules:

- Identifier resolution order: username → email → character name.
- **Empty character hashes never match** in account login. God's
  empty-password behavior remains a telnet-`connect` special case only;
  otherwise, pre-setup, any password would open the admin account from the
  web.
- A successful match against a legacy PennMUSH-format character hash
  triggers the existing transparent rehash.
- Telnet `login`, web `account-login`, and `jwt-login` all inherit this via
  `AuthenticateAsync`. `connect <name> <pw>` is unchanged.

## Section 3 — Hardening

- **debug-ott:** explicit `IHostEnvironment.IsDevelopment()` guard returning
  404 in production, independent of auth scheme. `DebugAuthenticationHandler`
  registration remains dev-gated; the endpoint guard is defense in depth.
- **MustChangePassword enforcement:** while set, an account session token is
  accepted only by change-password and logout endpoints. The portal shows a
  forced change-password screen (Section 4).
- **Config enforcement:**
  - `Net.PlayerCreation = false` refuses telnet `register`/`make`, web
    `account-register`, and `POST api/account/characters`.
  - `Net.Logins = false` refuses connection/login for non-wizard characters
    and accounts with no wizard-linked character (PennMUSH semantics: staff
    can still get in).
- **DisableAccountAsync:** implemented via new
  `UpdateAccountDisabledAsync` on `ISharpDatabase` + all three providers.
  Disabling also revokes the account's active sessions. `IsDisabled` is
  already checked at every login path.
- **Deployment cleanup:** drop `BOOTSTRAP_*` from
  `deploy/docker-compose.prod.yml` and `.env.example`; remove
  `AdminUsername`/`AdminPassword` from appsettings and `BootstrapOptions`;
  update CLAUDE.md env-var list and deploy docs to describe the wizard.

## Section 4 — Admin tooling & portal polish

- **In-game `@account` command family** (wizard-locked, like `@pcreate`):
  - `@account/list [pattern]`
  - `@account <name>` — details + linked characters
  - `@account/newpassword <name>=<password>` — sets password +
    `MustChangePassword` (the admin-driven reset)
  - `@account/disable <name>` / `@account/enable <name>`
  - `@account/setupcomplete` — Section 1 escape hatch
- **Portal admin page** `/admin/accounts` (gated on Wizard/God portal role):
  searchable list (username, email, disabled state, linked characters);
  actions: reset password, disable/enable, unlink character.
- **Replace vestigial OIDC client wiring:** production Blazor client gets an
  `AccountAuthenticationStateProvider` backed by the account session instead
  of OIDC bound to the unconfigured `"Local"` section, so
  `[Authorize]`/role-gated portal UI works identically in dev and prod. Dev
  keeps `DebugAuthStateProvider`.
- **Forced change-password screen** honoring the Section 3 enforcement.

## Section 5 — Testing

- **Integration (TUnit, Testcontainers via Podman):**
  - Setup lifecycle: fresh DB → status true → complete → status false →
    second complete 409; concurrent-claim race; upgrade-migration inference
    for existing claimed games.
  - Login-matrix table tests: account password, linked character password,
    character-name identifier, empty-hash never matches, legacy-hash rehash.
  - `debug-ott` returns 404 in Production environment.
  - `MustChangePassword` lockout of non-exempt endpoints.
  - `PlayerCreation`/`Logins` enforcement on every affected surface.
  - Disable/enable + session revocation.
- **Provider parity:** `ServerState` and `UpdateAccountDisabledAsync` tested
  against ArangoDB and SurrealDB containers; Memgraph mirrors the same set.
- **bUnit:** reworked `Setup.razor` (validation, 409 handling), forced
  change-password screen, login page smoke.
- **Socket-command tests:** `@account` family and telnet
  `register`/`make`/`login` enforcement, following the `GuestLoginTests`
  pattern.

## Out of scope

- Email infrastructure (SMTP, verification, self-service password reset) —
  explicitly deferred; `SharpAccount.Email`/`IsVerified` stay reserved.
- Persistent (DB-backed) session/refresh/OTT stores — existing follow-up in
  `docs/todo/area-01-auth.md`, unchanged by this design.
- Discord/Google OAuth (architectural-decisions.md §1.5 phases 2–3).

## Key files

- `SharpMUSH.Server/Services/BootstrapService.cs`,
  `SharpMUSH.Server/Controllers/SetupController.cs`,
  `SharpMUSH.Server/Controllers/AuthController.cs`,
  `SharpMUSH.Server/Controllers/AccountController.cs`
- `SharpMUSH.Library/Services/AccountService.cs`, `SharpMUSH.Library/ISharpDatabase.cs`
- `SharpMUSH.Database.{ArangoDB,Memgraph,SurrealDB}/*` (ServerState, disabled flag)
- `SharpMUSH.Implementation/Commands/{SocketCommands,AccountSocketCommands}.cs`
  + new `@account` command
- `SharpMUSH.Client/Pages/Setup.razor`, `Login.razor`, new
  `/admin/accounts`, `SharpMUSH.Client/Services/AccountAuthService.cs`,
  `SharpMUSH.Client/Program.cs`
- `deploy/docker-compose.prod.yml`, `.env.example`, `CLAUDE.md`
