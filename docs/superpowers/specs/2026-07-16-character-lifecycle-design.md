# Character Lifecycle

**Date:** 2026-07-16
**Status:** Draft, pending review
**Follows:** `2026-07-16-session-state-nav-panel-design.md`
**Followed by:** account-protection spec (Turnstile, Cloudflare, invite codes, rate limits, email verification, password reset)

## Framing

Account registration and character creation are **semi-separate**. Registration is a one-time entry event; character creation is a **standing capability** available at any N ≥ 0. They merely happen back-to-back the first time.

The driving insight: **characterless is not a new-user state, it is just N=0** — reachable by signing up *or* by losing your last character. Modeling it as "registration" would produce a one-time wizard that the returning zero-character user falls straight through.

The data model already agrees. `SharpAccount` (SharpAccount.cs:8-31) has **no dbref and no in-game presence**; characters attach by graph edge (`edge_account_owns_character` / `account_owns_character`), with `LinkCharacterAsync`/`UnlinkCharacterAsync` as first-class operations. `AuthController.cs:271` deliberately returns `Characters: []` on registration. N=0 is already an intended, representable state. Only the UI assumes otherwise.

## What already exists

Registration is **not** greenfield. Verified:

| Capability | Entry | File:line |
|---|---|---|
| Account create (web REST) | `POST /api/auth/account-register` | AuthController.cs:249-275 |
| Account create (web UI) | `/register` page **and** a duplicate Register tab on `/login` | Register.razor:1, Login.razor:50 |
| Account create (telnet) | `register <username> [email] <password>` | AccountSocketCommands.cs:22-93 |
| Character create + link (web) | `POST /api/account/characters` | AccountController.cs:70-101 |
| Character create + link (telnet) | `make <character-name> <password>` | AccountSocketCommands.cs:179 |
| Character create (in-game, wizard) | `@pcreate` | WizardCommands.cs:1929 |
| Adopt existing character | `POST /api/account/link-character` (verifies the character's MUSH password) | AccountController.cs:110-159 |
| Unlink | `DELETE /api/account/characters/{n}` | AccountController.cs:168 |

Telnet has a first-class account concept: `ConnectionState.AccountMode`, `ConnectionService.BindAccount`, and the four verbs `register`/`login`/`make`/`play`.

## Defects this spec fixes

**1. Nuked characters stay listed and selectable.** `@destroy` (BuildingCommands.cs:297) and `@nuke` (:791, `@destroy` with `override_: true`) only set `GOING`/`GOING_TWICE`. Nothing is ever deleted — BuildingCommands.cs:451 notes real deletion "requires a garbage collection system." `@nuke` never unlinks the account edge (no `account`/`unlink` reference in `BuildingCommands.cs` outside exit/room unlinks), and `GetCharactersForAccountAsync` (ArangoDatabase.Accounts.cs:161) does not filter `GOING`. Net effect: a nuked character is a live row wearing a flag, still listed in the portal and still resolvable by `switch-character`.

**2. Account-facing creation validates nothing.** `@pcreate` validates name format *and* uniqueness (WizardCommands.cs:1942-1955). `make` and `POST /api/account/characters` call `CreatePlayerCommand` directly with **zero** name validation and **zero** uniqueness check — `AccountController.cs:79-89` checks only `IsNullOrWhiteSpace`. There is no DB-level uniqueness constraint either (Migration_CreateDatabase.cs:155 requires only `PasswordHash` and `Quota`). **Duplicate player names are creatable today via HTTP or telnet.**

`ValidatePlayerName` (ValidateService.cs:253) cannot be reused as-is: it requires an existing `AnySharpObject` target, making it rename-only — which is why `@pcreate` comments around it and does its own uniqueness stream check.

**3. Banned names are dead config.** `BannedNamesOptions` (SitelockOptions.cs:7) has a CRUD controller (`ConfigAdmin` policy) and `@sitelock/list` display (WizardCommands.cs:2100, 2124), but is **never consulted by any creation path**. ValidateService.cs:272-273 carries the TODO.

**4. `player_creation` gates two unrelated things.** `Net.PlayerCreation` (NetOptions.cs:43-44, default `true`) gates account registration (AuthController.cs:256, AccountSocketCommands.cs:39) *and* character creation (AccountController.cs:76, AccountSocketCommands.cs:197). Gating account registration on a *player*-creation flag is a misuse.

**5. `register.txt` is honored on telnet only.** `Message.RegisterCreateFile` (MessageOptions.cs:37-42) is served on telnet refusal (AccountSocketCommands.cs:344-358); the web endpoints return a hardcoded string (AuthController.cs:257).

**6. No portal UI creates a character.** The endpoint exists; nothing calls it for the N=0 case.

**7. Sitelock gap.** `POST /api/account/characters` has no sitelock check; telnet `make` does (AccountSocketCommands.cs:185).

**8. Zero-character lockout during a login freeze.** `AnyStaffCharacterAsync` (AuthController.cs:322) returns false over an empty list, so when `Net.Logins` is disabled a 0-character account gets 403 (AuthController.cs:185, :229) — meaning someone who lost their last character cannot log in to make a replacement.

## Non-goals

Deferred to the account-protection spec: Turnstile, Cloudflare proxy hardening, dedicated rate limits, invite codes, email verification, SMTP, password reset. `IsVerified` (SharpAccount.cs:26) stays a dead field until then.

Not addressed: garbage collection of `GOING` objects. This spec filters them rather than collecting them.

## Design

### 1. Config split

- **`player_creation`** (existing, default `true`) — keeps its PennMUSH meaning, gates **character** creation only.
- **`account_creation`** (new, `Net` category, default `true`) — gates **account** registration. Repoint `AuthController.cs:256` and `AccountSocketCommands.cs:39` at it.

No tri-state mode is needed. PennMUSH's "registration required" *is* `player_creation = false` plus `register.txt` explaining how to ask — which telnet already implements. Honoring `RegisterCreateFile` on the web (fixing defect 5) yields registration mode for free.

Resulting modes, per side, independently:

| `*_creation` | Behavior |
|---|---|
| `true` | Open — self-serve |
| `false` + `register.txt` present | Registration mode — render the file's instructions |
| `false`, no file | Closed — plain refusal |

`@pcreate` continues to bypass `player_creation` (wizards, PennMUSH-consistent).

### 2. Max characters per account

New config option (`Limit` category), enforced at `POST /api/account/characters` and telnet `make`. **Default preserves today's behavior: unlimited.** Admins opt in.

Explicitly **not** built on `SharpPlayer.Quota` — that is PennMUSH *object* quota (per-player, starting 20, spent building), and it currently gates nothing at all (`GetOwnedObjectCountQuery` appears only in display paths: WizardCommands.cs:646, 2052, 2075 and InformationFunctions.cs:676). Conflating object quota with character count would surprise anyone who knows Penn.

### 3. Creation validation

Extract the validation `@pcreate` performs into a service method callable **without an existing target** (the gap that makes `ValidatePlayerName` rename-only), covering:

- name format (`NameRegex`, magic-cookie rejection, ASCII runes — ValidateService.cs:283-286)
- `Limit.PlayerNameLen`, `Cosmetic.PlayerNameSpaces`
- uniqueness (`GetPlayerQuery` stream check)
- **banned names** — wire `BannedNamesOptions` in at last, resolving the ValidateService.cs:272 TODO

All four creation paths (`@pcreate`, `make`, HTTP, `pcreate()`) route through it, so validation cannot be bypassed by choosing an entry point. Add the missing sitelock check to `POST /api/account/characters` (defect 7).

A DB-level uniqueness constraint on player name is **out of scope** — `GOING` rows would collide with live names and the interaction needs GC decisions this spec doesn't make. Application-level uniqueness across all paths is the boundary here.

### 4. Destruction and linkage

- **Unlink on destroy.** `DestroyObjectAsync` (BuildingCommands.cs:326) calls `UnlinkCharacterFromAccountAsync` when the target is a player, so the edge never dangles.
- **Filter `GOING`.** `GetCharactersForAccountAsync` excludes `GOING`/`GOING_TWICE` in all three providers, so already-nuked characters stop appearing for existing accounts — the retroactive half of the fix, needed because existing games already have dangling edges.
- `switch-character` and `link-character` reject `GOING` targets.

Together these make N=0 genuinely reachable by nuking, which today it is not.

### 5. The N=0 experience

The account **stays logged in and the portal adapts** — no forced logout, no exile screen. `CanUseTerminal` (from spec #1) is already false at N=0, so terminals stay disconnected without new gating.

The nav card renders the `NoCharacters` state; its panel offers, per `player_creation`:

- **open** → **Create Character**
- **registration mode** → the rendered `register.txt` content
- **closed** → a plain "character creation is closed" message

### 6. Creation UI

A dialog reached from the account panel: character name + password → `POST /api/account/characters`. On success the character is linked, becomes `ActiveCharacter`, and the terminal connects per spec #1's switch flow.

**The same affordance appears in the character submenu at N ≥ 1.** One standing capability, not an onboarding wizard — this is the semi-separate model made real.

### 7. Login freeze and zero characters

Fix defect 8: a 0-character account must be able to log in while `Net.Logins` is disabled *if* it could otherwise create a character, or it is permanently stuck. Treat `AnyStaffCharacterAsync` over an empty list as "not staff" (correct) but stop conflating that with "may not log in" for accounts with no characters at all.

### 8. UI cleanup

Collapse the duplicate registration UI — `/register` and the `Login.razor:50` Register tab — into one implementation.

## Testing

- **Validation:** all four creation paths reject malformed, duplicate, and banned names; `@pcreate` parity preserved; sitelock enforced on the HTTP route.
- **Config:** `account_creation` and `player_creation` gate their own side independently across all four combinations; `register.txt` renders on web and telnet; `@pcreate` still bypasses `player_creation`.
- **Max characters:** enforced at HTTP and telnet; unlimited default preserves current behavior.
- **Destruction:** `@nuke` unlinks the edge; `GOING` characters absent from `GetCharactersForAccountAsync` in all three providers; `switch-character`/`link-character` reject `GOING`; a pre-existing dangling edge is filtered retroactively.
- **N=0:** all three config modes render correctly; terminals stay disconnected; creation from the panel links, activates, and connects; login succeeds during a login freeze.
- **Regression:** account registration still returns `Characters: []` and login still handles an empty list.

Per project convention: config-sensitive tests toggling `Net.PlayerCreation`/`Net.Logins` **must** carry `NotInParallel("ConfigMutation")` — this suite is flaky under TUnit parallelism via shared session state. Integration/Explicit suites run under Podman; clear stale containers first.

## Open questions

None.
