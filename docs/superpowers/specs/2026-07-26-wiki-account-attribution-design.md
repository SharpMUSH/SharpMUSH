# Wiki attribution by account, with safe references to departed accounts

Date: 2026-07-26

## Problem

Wiki attribution is stored as a raw dbref string: `WikiPage.AuthorDbref` and
`LastEditorDbref`, `WikiRevision.EditorDbref`, `WikiAsset.UploaderDbref`. Three
things are wrong with that.

**The stored value is often not a dbref.** `WikiController.CallerDbref` reads
`ClaimTypes.NameIdentifier` and documents it as the caller's character dbref, but
`AccountSessionAuthenticationHandler` and `DebugAuthenticationHandler` both put the
*account* id there. Every portal-authored page therefore stores
`node_accounts/<key>` in a field named and typed as a dbref. The consequence is not
cosmetic: `WikiController.IsAuthor` compares the caller's account id against that
stored value, so **the author check never matches for a portal caller**.
`GalleryController` has the same miswiring, and reports "Missing character identity."
while holding an account id.

**A character is the wrong anchor.** One human may own several characters, and a wiki
edit is the human's act, not a persona's. Attribution keyed to a character splits one
contributor's history across their characters and breaks when a character is renamed
or destroyed.

**Nothing survives the account going away.** `DeleteAccountAsync` hard-deletes the
account document and strips its `edge_account_owns_character` edges first. The moment
wiki records point at accounts, that becomes a dangling reference.

## Goal

Wiki pages, revisions, and assets are attributed to an **account**. An account that is
closed or deleted remains a valid, resolvable reference forever, so historical
attribution never dangles and never has to be rewritten.

## Decisions

| # | Decision |
|---|---|
| 1 | Wiki attribution is **account-only**. Characters do not appear in wiki records. |
| 2 | Account documents are **never deleted**. Close and delete are status transitions. |
| 3 | Closed and deleted accounts **retain** username, email, and password hash. |
| 4 | A reserved, unclaimable **system account** owns seeded content; the account reference is non-nullable. |
| 5 | Scope is wiki **pages, revisions, and assets**. |
| 6 | The UI shows the account's **current** username; no tombstone marker. |
| 7 | Attribution is stored as a **graph edge**, not a field. |
| 8 | Asset metadata **moves into the database** so it can be an edge target. |
| 9 | Prefer **objids** (`#123:1700000000`) over bare dbrefs wherever an object is referenced by string. |
| 10 | No data migration — SharpMUSH is pre-production. |

## Account lifecycle

`SharpAccount.IsDisabled` (bool) is replaced by a new `AccountStatus` enum in
`SharpMUSH.Library/Models/`:

```csharp
public enum AccountStatus { Active, Disabled, Closed, Deleted }
```

- **Active** — normal.
- **Disabled** — admin suspension; reversible.
- **Closed** — the holder has left. Reversible by an admin, not self-service.
- **Deleted** — removal requested. The row is retained regardless.

Replacing rather than adding avoids a bool and an enum that can contradict each other
(`IsDisabled = false` with `Status = Deleted`), which would force every read site to
know which one wins. Login succeeds only when `Status == Active`, replacing today's
`account.IsDisabled` checks.

### Retention is intentional

`Username`, `Email`, and `PasswordHash` all survive `Closed` and `Deleted`. Three
consequences, all deliberate:

- The username and email keep blocking re-registration — a ban-evasion guard.
- Reopening an account is a status flip, with no credential reset.
- **There is no name-erasure path.** If one is ever required, it is a separate
  rename-then-close feature, not part of this design.

### Service and command surface

`ISharpDatabase.DeleteAccountAsync` becomes
`SetAccountStatusAsync(accountId, status)`. Two behavior changes ride along: the
document is never removed, and `edge_account_owns_character` edges are **not** severed
— today's delete strips them first, which would erase exactly the history this design
preserves. `DeleteAccountAsync` has no production caller (only tests), so no caller
migration is needed.

`DisableAccountAsync` and `EnableAccountAsync` remain as named wrappers over the
setter, since they also revoke live sessions. `CloseAccountAsync` and
`MarkAccountDeletedAsync` join them, and revoke sessions the same way.

`@account` gains `/close` and `/delete` alongside `/disable` and `/enable`.
`AdminAccountRow.IsDisabled` becomes `Status`.

### The system account

Username `system`. Created by `BootstrapService` next to the existing pre-generated
admin, via `CreateUnclaimedAccountAsync`. It is unreachable by construction rather than
by a flag: the empty password hash can never authenticate at the account level, and
with no linked characters there is no character password that could authenticate it
either.

- Its username joins the reserved-name check so it cannot be registered.
- `SetAccountStatusAsync` refuses to change its status — a closable system account
  would strand every seeded page's attribution.

## Attribution model

### One edge collection

`edge_account_contributed`, `From [node_accounts]`,
`To [node_wiki_pages, node_wiki_revisions, node_wiki_assets]`. The target collection
implies the role unambiguously — a page means "created it", a revision means "made
it", an asset means "uploaded it" — so the edge carries no discriminator property.
This follows the existing `edge_account_has_role` shape.

The edge definition belongs to the `graph_accounts` graph, declared in
`Migration_AddAccounts` — see [Schema changes](#schema-changes).

While in `DatabaseConstants`: the `GraphRoles` doc comment describes
`verticesAll -> IsObject -> Objects`, but the constant's only use is
`Migration_AddRoles` creating `Accounts -> AccountHasRole -> Roles`. The comment is
stale and gets corrected alongside the new constants.

### Stored versus derived

All three attributions are immutable: an author, a revision's editor, and an uploader
are each set once. The only mutable attribution, `LastEditor`, is derivable — create
writes revision 1 and update writes revision N, so every page state has a matching
revision, and the last editor is the editor of the revision numbered
`page.RevisionNumber`.

| Record | Attribution | Storage |
|---|---|---|
| `node_wiki_pages` | author | edge |
| `node_wiki_revisions` | editor | edge |
| `node_wiki_assets` | uploader | edge |
| `WikiPage.LastEditor` | — | derived from revision `RevisionNumber` |

The author is stored even though it too is derivable (as revision 1's editor), for two
reasons. It is a permission anchor consulted on every edit attempt, so it should be one
hop rather than a history lookup. And it must survive any future revision-pruning
feature: pruning would destroy a derived author, but never the newest revision, so a
derived *last* editor stays safe under pruning while a derived author would not.

Page listings need no attribution at all — neither `WikiIndex` nor
`RecentWikiActivityWidget` displays an editor, and `WikiPageSummary` has no such
field. Only the page view, history, and diff views resolve attribution.

### Models carry resolved values

Storage uses edges; models carry what the traversal resolved, following the Scene
plugin's precedent.

- `WikiPage`: `AuthorAccountId`, `AuthorUsername`, `LastEditorAccountId`,
  `LastEditorUsername` — replacing `AuthorDbref` and `LastEditorDbref`.
- `WikiRevision`: `EditorAccountId`, `EditorUsername` — replacing `EditorDbref`.
- `WikiAsset`: `UploaderAccountId`, `UploaderUsername` — replacing `UploaderDbref`.

All non-nullable. The never-delete invariant guarantees the traversal resolves, which
is the payoff of the lifecycle work above.

`InMemoryWikiService` implements the same `IWikiService` contract and must change with
it. It has no production registration — only tests construct it — so it tracks the new
shape as a test double rather than needing edge semantics of its own.

Because the traversal projects `account.Username` in the same query, live resolution
falls out for free: a rename propagates to all historical attribution, and no separate
batch resolver is needed.

### Account ids stay server-side

Public DTOs carry a username only. Account ids live on the Library models for the
`IsAuthor` comparison and never reach the browser.

One cosmetic consequence: `WikiPageHistory.razor` derives its avatar colour from
`EditorDbref.GetHashCode()`, which will hash a username instead, so existing avatar
colours change.

## Asset metadata in the database

Assets are currently `{id}.bin` plus an `{id}.json` sidecar holding a serialized
`WikiAsset`, so there is no document for an edge to point at.

**New collection** `node_wiki_assets` (`DatabaseConstants.WikiAssets`), keyed by the
existing asset id: `FileName`, `ContentType`, `SizeBytes`, `Sha256`, `UploadedAt`,
with the uploader as an `edge_account_contributed` edge. An index on `UploadedAt`
descending replaces `ListAsync`'s current full directory scan and in-memory sort.

**The service splits in two, keeping the public surface:**

- `IWikiAssetBlobStore` — bytes only: write (returning size and SHA-256, computed
  while streaming, as today), open-for-read, delete. `FileSystemWikiAssetBlobStore`
  keeps `{id}.bin` and the existing `IsValidId` path guard.
- `IWikiAssetService` keeps all four current methods, now composing the blob store with
  `ISharpDatabase`. `WikiAssetController` and `GalleryController` change only in the
  uploader parameter, from a dbref to an account id.

**Write ordering.** Bytes first, then metadata; if metadata insertion fails, delete the
bytes — the rollback the current implementation already performs. Delete goes the
other way: metadata and edge first, then bytes. The asymmetry is deliberate: an
orphaned `.bin` is invisible and reclaimable, whereas metadata pointing at missing
bytes is a live-looking asset that 404s.

`Sha256` moves across unchanged. Nothing reads it for deduplication today, and no
dedup behavior is introduced or removed.

## Identity plumbing

### `NameIdentifier` means "account id", uniformly

Two of the three handlers already do this. `MushBasicAuthenticationHandler` is the
outlier, emitting `#{player.Object.Key}`; it changes to emit the owning account's id
(resolved via `GetAccountForCharacterAsync`) while keeping the character in
`character_dbref`. A character with no owning account still authenticates — it is
character-password basic auth — but emits no `NameIdentifier`, and account-anchored
writes then return 403. This makes `ApiControllerBase.CurrentAccountId`'s existing doc
comment true.

### Write paths

- `WikiController.CallerDbref` → `CallerAccountId`: same claim, correct name and doc
  comment. `IsAuthor` then compares account id to account id, fixing the check that
  currently never matches for portal callers.
- `GalleryController` takes an account id, and its "Missing character identity."
  message becomes accurate.
- In-game: `WikiCommandHelper.EditorDbref(executor)` becomes an async resolve through
  `IAccountService.GetAccountForCharacterAsync`. No linked account means the edit is
  refused with a localized message.
- Seeding: `StartupHandler`'s three `authorDbref: "#1"` call sites use the system
  account id.

### Objids over dbrefs

`SharpObject.DBRef` already builds `new(Key, CreationTime)` and its `ToString()`
already emits `#N:ms`, so this is mostly wiring:

- All three handlers emit `character_dbref` as `player.Object.DBRef.ToString()`.
- `AccountSessionAuthenticationHandler` gains the `character_creation_time` claim it
  currently omits; the other two already send it.
- `CharacterIdentity` returns a `DBRef` carrying both parts, instead of an `int` that
  deliberately parses the objid suffix off and then discards it.

This closes a recycle hole: a stale session pointing at a reused dbref currently
resolves to whatever object now occupies the slot.

- `MailController.ResolvePlayerAsync` reads `NameIdentifier` as a dbref and builds
  `new DBRef(dbref, null)`. It is wrong twice — portal sessions hand it an account id,
  and the null creation time makes it recycle-unsafe. It uses `CharacterIdentity`
  instead. Mail stays character-scoped; this is not attribution work, but it is the
  same claim misread in the same way.

## Schema changes

SharpMUSH is pre-production. The database is deleted and setup re-run, so there is no
data to migrate: no resolution ladder, no backfill service, no sidecar import, and no
special-casing of the seeded pages.

The migration files are therefore just the code that defines the schema, and they are
edited directly:

- `Migration_AddAccounts` — the `edge_account_contributed` edge collection and its
  `graph_accounts` edge definition.
- `Migration_AddWiki` — the `node_wiki_assets` collection with its `UploadedAt` index;
  `AuthorDbref` and `LastEditorDbref` leave the schema rule.
- The Memgraph and Surreal `*.Migration.cs` files get the equivalent edits.

`AuthorDbref`, `LastEditorDbref`, `EditorDbref`, and `UploaderDbref` leave the models.

Deleting the database is required, not incidental: migrations are gated by
`MigrationHistory`, so an edited migration does not re-run against a database that
already recorded it.

## API and UI surface

**Server DTOs.** `WikiController.RevisionDto.EditorDbref` becomes `EditorUsername`;
page DTOs gain `AuthorUsername` and `LastEditorUsername`. `WikiAssetController`'s asset
DTO swaps `UploaderDbref` for `UploaderUsername`. No account ids in any DTO.

**Client.** `WikiRevisionInfo.EditorDbref` → `EditorUsername`, rendered by
`WikiPageDiff.razor` and `WikiPageHistory.razor`. `AdminWikiAssets.razor` shows the
uploader username. `AdminAccountRow.IsDisabled` becomes `Status`, and
`AdminAccounts.razor` gains close and delete actions alongside disable and enable —
with the system-account row's actions disabled, matching the server-side guard rather
than relying on it alone.

**Strings.** All new user-visible text goes through resources, not literals: admin
status labels (`Active`, `Disabled`, `Closed`, `Deleted`) and close/delete
confirmations into `SharedResource.resx`; the `+wiki/edit` "not linked to a web
account" refusal into `Notifications.resx`. Both need their `.fr` counterparts
populated — a missing key falls back to the raw key.

## Error handling

| Condition | Outcome |
|---|---|
| In-game edit, character has no linked account | Refused with a localized message |
| Portal write with no `NameIdentifier` claim | 403 |
| `SetAccountStatusAsync` on the system account | Error return, not a silent success |
| Asset metadata insert fails after bytes are written | Bytes rolled back |
| Login against a non-`Active` account | Rejected |

## Testing

- **Unit** — `AccountStatus` transitions and the system-account guard; login rejected
  for every non-`Active` status; `CharacterIdentity` round-tripping an objid, including
  the recycled-timestamp mismatch; the derived last-editor calculation.
- **Wiki service** — edge creation on create, update, and upload; author preserved
  across edits; last editor tracking the newest revision.
- **Integration, per backend** — the traversal projects the current username; a rename
  propagates to old revisions; a `Closed` account and a `Deleted` account still resolve
  and render. This is the central requirement, so it gets an explicit test on all three
  backends, not only Arango.
- **Controller** — `IsAuthor` matching for a portal caller (a regression test for the
  currently-broken behavior); 403 without an account; asset list ordering from the
  indexed read.
- **BUnit** — `AdminAccounts` rendering the four statuses and hiding actions on the
  system row.

## Out of scope

- Name erasure for a departed account. Retention is deliberate; erasure would be a
  separate rename-then-close feature.
- Character-level attribution or display personas for wiki content. Wiki records are
  account-only.
- `ApplicationsController`, `SuggestionController`, and `LayoutsController`, which
  record no identity at all.
- Mail attribution. Mail is character-scoped by design; only its claim misreading is
  fixed here.
