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

**9. `PLAYER\`CREATE` fires from one of five call sites.** The event is documented in `SharpMUSH.Documentation/Helpfiles/SharpMUSH/sharpevents.md` with args `(objid, name, how, descriptor, email)`, and `how` is documented as taking `pcreate|create|register` — sharpevents.md:44 even shows a worked example gating on it. But it is triggered only from `@pcreate` (WizardCommands.cs:1964). Not from telnet `make` (AccountSocketCommands.cs:234), not from the portal's `POST /api/account/characters` (AccountController.cs:86), not from `pcreate()` (UtilityFunctions.cs:49). **The documented `how=create` and `how=register` values are emitted by nothing.**

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

### 9. Admin-hookable character application

An admin must be able to gather extra information before a character exists, without touching C# or rebuilding the client. **The mechanism already exists and is implemented** — this spec wires it into creation rather than inventing anything.

**What exists (Area 21, Dynamic Applications — `docs/design/dynamic-applications.md`).** Phases 1–8 landed and are green; the design doc's "No code in this pass" non-goal is **stale**. A Portal Schema Document (`kind: form|view`, pages → sections → elements) is emitted by softcode and rendered by a passive client. `SchemaAppService`, `SchemaFormRenderer`/`SchemaViewRenderer`, `DynamicApplication.razor` (`@page "/apps/{Slug}"`), and a DB-backed `RegisteredApplication` registry behind `ApplicationsController` (Wizard+) all ship today, on all three providers. Actions are `/http/...` round-trips dispatched by `HttpHandlerCommandService` to softcode `<POST>` attributes. The governing principle is **"the portal renders; softcode decides"** — no game policy in the client, no client-side branching (`show_if` is deliberately absent; progression is a returned replacement `schema`).

`examples/packages/chargen/` and `examples/packages/chargen-app/` already ship a working "Character Application" as two `@ainstall`-able YAML files. Compiled components are *also* hookable without a client rebuild: `PluginComponentLoader.cs` loads plugin UI assemblies over HTTP via `Assembly.Load` in Mono-WASM and resolves types by reflection, and `DynamicApplication.razor` branches to `DynamicComponent` for them.

**Registry refinements vs. the design doc** (per `docs/todo/area-21-applications.md`): access is a single hierarchical `MinimumRole`, not `allowedRoles[]`; writes are `[Authorize(Roles = Wizard)]`, which notably excludes God (#1) — a known portal-wide quirk.

**Designation.** A registered Application may be designated as *the* character-creation application. When one is designated, the Create Character flow (§6) renders it instead of the plain name+password dialog.

**Sequence.** Creation stays in C# — softcode cannot link a character to an account (`LinkCharacterAsync` has no softcode surface, and exposing one is out of scope):

1. Portal renders the designated Application's schema and collects fields.
2. **Validate POST** → softcode replies with the existing envelope: `{ok: false, errors}` binds to fields and **nothing is created**; `{ok: true}` proceeds.
3. C# creates the character, sets the password, and links it to the account — the §3 validation still applies, so an application cannot smuggle in a duplicate or banned name.
4. **Apply POST** → the new character's DBRef plus the gathered data go to softcode, which decides what to do with them.
5. `PLAYER\`CREATE` fires normally. It carries no application payload and gets no special casing.

The veto in step 2 is load-bearing: softcode reacting only after creation could not undo it, and since nothing is ever truly deleted (`BuildingCommands.cs:451`), a rejected application would otherwise leave a permanent half-character.

**Contract: convention, not new grammar.** The two endpoints require **no change to the Portal Schema Document**. `PortalSchemaDocument.Actions` is already `IReadOnlyDictionary<string, SchemaAction>` — an arbitrary named map, not a fixed `submit` slot — and `SchemaActionResult` already carries `Ok` + `Errors`. The design doc's `"submit"` is an example key, not a constraint. So a character-creation application simply declares two entries:

```jsonc
"actions": {
  "validate": { "transport": "http", "method": "POST", "route": "/http/chargen/validate",
                "payload": "fields", "on_error": { "bind_field_errors": true } },
  "apply":    { "transport": "http", "method": "POST", "route": "/http/chargen/apply",
                "payload": "fields" }
}
```

`schema_version` stays at 1; no renderer, registry, or provider migration is needed, and applications that don't opt in are untouched.

What *is* new is that the **portal invokes these programmatically** rather than from a user-clicked button — the creation flow calls `validate`, performs the C# create/link, then calls `apply`, passing the new DBRef alongside the fields. Well-known action names are therefore part of the character-creation contract: an application designated for creation **must** declare both, and registration should reject one that doesn't.

Rejected alternatives: first-class `ValidateRoute`/`ApplyRoute` on `RegisteredApplication` (duplicates what `actions` already expresses, and costs a migration on all three providers); and expanding the schema grammar to `schema_version: 2` (pushes a two-phase lifecycle policy into the client, which contradicts Area 21's "the portal renders; softcode decides" stance, and burdens every existing app with a contract only one flow needs).

This inverts the shipped `chargen` package, whose submit handler `@create`s the character itself. That package's contract changes accordingly — it becomes validate + apply, not create.

**Fix defect 9 at the source.** All four creation paths funnel through `CreatePlayerCommand` → `CreatePlayerCommandHandler` (CreatePlayerCommandHandler.cs:11) → `CreatePlayerAsync`. Trigger `PLAYER\`CREATE` **in the handler**, with `how` carried on the command as provenance, rather than adding a call at each of the four sites. A character created from anywhere then fires the event for the Event Object (`#9`) to act on, and no future creation path can forget to. Event dispatch already swallows handler exceptions (EventService.cs:160), so a broken handler cannot break creation.

## Testing

- **Validation:** all four creation paths reject malformed, duplicate, and banned names; `@pcreate` parity preserved; sitelock enforced on the HTTP route.
- **Config:** `account_creation` and `player_creation` gate their own side independently across all four combinations; `register.txt` renders on web and telnet; `@pcreate` still bypasses `player_creation`.
- **Max characters:** enforced at HTTP and telnet; unlimited default preserves current behavior.
- **Destruction:** `@nuke` unlinks the edge; `GOING` characters absent from `GetCharactersForAccountAsync` in all three providers; `switch-character`/`link-character` reject `GOING`; a pre-existing dangling edge is filtered retroactively.
- **N=0:** all three config modes render correctly; terminals stay disconnected; creation from the panel links, activates, and connects; login succeeds during a login freeze.
- **Application hook:** with no Application designated, creation falls back to the plain dialog; with one designated, its schema renders; a `{ok: false}` validate response binds errors and creates **nothing**; a successful flow creates, links, and posts DBRef + data to the apply route; §3 name validation still rejects duplicate/banned names submitted through an application; a throwing softcode handler does not break creation; registering an application for creation without both `validate` and `apply` actions is rejected.
- **Area 21 regression:** existing applications (including `kind: view` and widget apps) are unaffected — `schema_version` stays 1 and the actions map is unchanged for anything that doesn't opt in.
- **`PLAYER\`CREATE`:** fires once per creation from all four paths (`@pcreate`, `make`, portal, `pcreate()`) with the correct `how`; existing `@pcreate` behavior unchanged.
- **Regression:** account registration still returns `Characters: []` and login still handles an empty list.

Per project convention: config-sensitive tests toggling `Net.PlayerCreation`/`Net.Logins` **must** carry `NotInParallel("ConfigMutation")` — this suite is flaky under TUnit parallelism via shared session state. Integration/Explicit suites run under Podman; clear stale containers first.

## Open questions

None.
