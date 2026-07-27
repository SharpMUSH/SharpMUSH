# Binding the acting character to the session token

**Date:** 2026-07-27
**Supersedes:** PR #718 (closed unmerged)

## Problem

The portal decided which character a request acted as from an `X-Acting-Character` header, with
`AccountSessionAuthenticationHandler.ResolveActingCharacter` validating it against the roster and
silently falling back to the primary when it didn't match. The character itself lived only in
`AccountAuthService.ActiveCharacter`, in memory.

Two consequences:

- **Reload amnesia.** Nothing persisted the acting character, so after F5 `SetCharacters` reseated
  the account on `characters.FirstOrDefault()`. Gallery uploads, mail and wiki edits were then
  attributed to a character the player was not acting as, with nothing on screen to say so.
- **Client-asserted identity.** The authoritative answer to "who am I acting as?" arrived in a
  header the client wrote on every request. Not a privilege hole — ownership was checked — but the
  failure mode was silent, and the mechanism is the one the field explicitly warns against.

PR #718 fixed the first by persisting the character to sessionStorage. That made ambient client
state durable rather than removing it, and was closed in favour of this design.

## Prior art

- [WorkOS, multi-tenant session management](https://workos.com/blog/multi-tenant-session-management):
  mint a fresh token carrying the new org claim when the user switches, and *"do not read the active
  org from a request header, query string, or client-supplied cookie on protected routes."*
- [Auth0 Organizations](https://auth0.com/docs/manage-users/sessions/manage-multi-site-sessions):
  `org_id` is a token claim.
- [RFC 8693](https://www.rfc-editor.org/info/rfc8693/): the `act` claim names the acting party inside
  the token; `may_act` says who may become one. The authorization server decides, not the client.

## Design

**The session token is the acting identity.** `SharpSession` gains `CharacterKey` and
`CharacterCreationTime`; `IAccountSessionStore.ValidateAsync` returns a `SessionIdentity`
(account + character) instead of a bare account id.

| Flow | Behaviour |
|---|---|
| Login / register | Mints a token bound to the primary character (unbound when the roster is empty), so there is never a "has characters, token names none" state |
| Switch character | Validates ownership, mints a **new** token bound to the target, returns it beside the OTT; the caller adopts it |
| Every request | `AccountSessionAuthenticationHandler` emits the character claims from the session record; membership is re-checked against the live roster, and a character the account no longer owns acts as **nobody** rather than falling back |
| Reload | sessionStorage is tab-scoped and survives F5; restoring the credential restores the identity. Nothing to lose, re-derive, or race |
| Two tabs | Two tokens, two characters, structurally |
| SignalR | The hub reads the character from the access token; the `?character=` query is gone |

**Mint-and-let-expire.** Switching does not revoke the previous token; it lapses on its own 15-minute
sliding TTL. `window.open` hands a new tab a *copy* of its opener's sessionStorage, so the new tab
consuming an `?as=` hint calls the switch endpoint while its opener is still using the token it
inherited — revoking there would log the opener out. `RevokeAllForAccountAsync` still backs bans.

**How a reloaded tab learns who it is.** The token is opaque to the client, so the server says:
`CharacterSummary` gains `IsActing`, set from the caller's session when the roster is served. The
roster call already runs during hydration, so this costs no extra round trip.

## What this deletes

`ActingCharacterHeaderHandler`, the `X-Acting-Character` header, the SignalR `?character=` query, the
hint-reading half of `ResolveActingCharacter`, and the entire client-side persistence path proposed
in #718.

## Carried over from #718

`SetCharacters` re-binds a still-present active character to the roster's copy, so a rename between
roster reads isn't rendered stale. That fix stands on its own merits.

## Testing

- `AccountSessionAuthHandlerTests`: a session bound to a non-primary acts as it; a client-supplied
  hint cannot change it; a session bound to a character the account no longer owns acts as nobody.
- `CharacterSwitchServiceTests`: the tab adopts the server-minted token; a refused switch leaves the
  identity untouched.
- `AccountAuthServiceHubTokenTests`: the hub URL advertises no character; the switch adopts the token.
- Store tests cover the round-trip of the binding through both implementations.

## Deferred

- **URL-scoping** (`/as/{name}/…`). Per-tab tokens fix the correctness problem; URL-scoping buys
  bookmarkability and history coherence at the cost of a second source of truth needing
  reconciliation. Revisit if links that open as a specific character become a real use case.
- **Role/permission caching.** `sharpmush.account.role` / `.permissions` are still client-cached
  copies of server-authoritative state — the same anti-pattern, out of scope here.
