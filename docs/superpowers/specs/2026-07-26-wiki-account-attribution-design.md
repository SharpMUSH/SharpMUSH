# Wiki attribution by account, with safe references to departed accounts

Date: 2026-07-26

## Problem

Wiki attribution is stored as a character dbref string: `WikiPage.AuthorDbref` and
`LastEditorDbref`, `WikiRevision.EditorDbref`, `WikiAsset.UploaderDbref`. Two things
are wrong with that.

**A character is the wrong anchor.** One human may own several characters, and a wiki
edit is the human's act, not a persona's. Attribution keyed to a character splits one
contributor's history across their characters, and asks a question with no good answer
when the character is destroyed: the edit still happened, and someone is still
accountable for it, but the thing the record points at is gone.

**Nothing survives the account going away.** `DeleteAccountAsync` hard-deletes the
account document and strips its `edge_account_owns_character` edges first. The moment
wiki records point at accounts, that becomes a dangling reference.

> **Revised 2026-07-26.** This section originally claimed the stored value was often
> not a dbref at all — that `WikiController.CallerDbref` read
> `ClaimTypes.NameIdentifier` (the account id) and so `IsAuthor` never matched for a
> portal caller. That was true of the branch the spec was drafted against, and is
> **already fixed on `main`**: `CharacterClaimsExtensions.GetActingCharacter` reads the
> `character_dbref` claim, and `WikiController`, `GalleryController`,
> `WikiAssetController`, and `MailController` all go through it. The stored value is now
> a genuine character reference. That removes a bug from the motivation but not the
> design: a character is still the wrong anchor, which is the argument this spec rests
> on.

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

**Every non-`Active` status is admin-reversible**, including `Deleted`. That follows from
retaining credentials: the row keeps everything needed to restore the account, so a
one-way `Deleted` would be a rule with nothing behind it. `Deleted` therefore means
"treated as deleted" — unusable for login, presented as gone — not "unrecoverable".
Service, API, and UI allow the same transitions, and the admin UI offers *Reactivate* on
all three non-active states. The only irreversible operation is the one this design
removes: deleting the row.

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

### Account ids stay out of wiki DTOs

Wiki DTOs carry a username only; account ids live on the Library models for the
`IsAuthor` comparison. The rule is scoped to wiki attribution rather than global — the
admin account surfaces legitimately need an account id, since `AdminAccountRow.Id` is what
the status endpoint is addressed by. The point is that a reader of a wiki page is never
handed an internal account key, not that the identifier is a secret.

One cosmetic consequence: `WikiPageHistory.razor` derives its avatar colour from
`EditorDbref.GetHashCode()`, which will hash a username instead. Worth noting these
colours were never stable to begin with — .NET randomizes string hash codes per process,
so they already change on every server restart. A stable per-contributor colour would need
an explicit hash, which is out of scope; this design only changes what gets fed to an
already-unstable one.

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

**Write ordering.** Three steps — bytes, metadata, uploader edge — and each needs its own
rollback, because the uploader is non-nullable on the model:

1. Write bytes. On failure, nothing to undo.
2. Insert metadata. On failure, delete the bytes (the rollback the current implementation
   already performs).
3. Create the uploader edge. **On failure, delete the metadata and the bytes.** A metadata
   row without its edge is not merely untidy — it violates the non-nullable
   `UploaderAccountId` contract, so every later read of that asset fails to resolve.
   Leaving it behind is worse than never having written it.

Delete goes the other way: metadata and edge first, then bytes. The asymmetry is
deliberate: an orphaned `.bin` is invisible and reclaimable, whereas metadata pointing at
missing bytes is a live-looking asset that 404s.

Both partial-write boundaries get a test — bytes-without-metadata and
metadata-without-edge are the two states a reader cannot cope with.

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

- `WikiController.CallerDbref` → `CallerAccountId`, reading `NameIdentifier` instead of
  the character claim. `IsAuthor` then compares account id to account id.
- `GalleryController` and `WikiAssetController` take an account id for the uploader.
- In-game: `WikiCommandHelper.EditorDbref(executor)` becomes an async resolve through
  `IAccountService.GetAccountForCharacterAsync`. No linked account means the edit is
  refused with a localized message.
- Seeding: `StartupHandler`'s three `authorDbref: "#1"` call sites use the system
  account id.

### Objids over dbrefs — done in PR #722

> Reviewers reading this against `main` alone will find `GameHub.CharacterGroupName` taking a
> `string` and the handlers emitting a bare `#N`. That is correct: this section describes PR
> #722, which is open alongside this document. Everything below is true once #722 merges, and
> nothing in phase 1 or phase 2 depends on it landing first — the two are independent.

This was specified here and has since been implemented separately, because it is a
cross-cutting transport concern rather than attribution work. Recorded for context:

An object reference crossed four boundaries as a bare string — a claim, a client hub
argument, a NATS payload, a SignalR group name — with nothing forcing the spellings to
agree, so `"42"`, `"#42"`, and `"#42:1700000000"` named three different groups. A
producer and consumer that disagreed would deliver nothing at all, silently, because
SignalR accepts any group name and publishing to an empty group is not an error.

Rather than normalizing at the group-name chokepoint — which would have preserved the
divergence — the contract is now typed: `CharacterGroupName`, `RoomGroupName`,
`SendToCharacterAsync`, and `SendToRoomAsync` take a `DBRef`, so a wrong spelling is
unrepresentable; `DBRef.ToString()` is the only serialization and `DBRef.TryParse` the
only way in. The handlers emit `character_dbref` as an objid,
`CharacterClaimsExtensions.GetActingCharacter` returns a parsed `DBRef`, the NATS bridge
drops payloads that do not parse, and `JoinRoom`/`LeaveRoom` reject unparseable client
input rather than joining a group nothing publishes to.

Objid rather than bare dbref is the point: it makes delivery recycle-safe, so a stale
reference to a destroyed object cannot leak to whatever now occupies that slot.

`MailController` and `GalleryController` were each hand-stripping the objid suffix and
rebuilding `new DBRef(n, null)` — the recycle-unsafe pattern — and now use the parsed
accessor.

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
  for every non-`Active` status; the fail-closed status parse (a missing stored value reads
  as `Active`, an unparseable one as `Disabled`); the derived last-editor calculation.
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
