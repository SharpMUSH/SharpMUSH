# Wiki localization with per-locale fallback

**Date:** 2026-07-26
**Status:** Approved design, not yet implemented

## Problem

Portal chrome is localized through `IStringLocalizer<SharedResource>` and the
`SharedResource.resx` family. Wiki *content* is not. A page has exactly one
`Title` and one `MarkdownSource`, so a French-speaking game can translate every
button in the portal and still serve English help pages.

The immediate payoff is the `Help:` namespace: the pages behind `/help` and
`/help/{slug}` are ordinary wiki pages, so making the wiki locale-aware makes
help translatable without a second mechanism.

## Scope

In scope: `IWikiService` and the wiki pages it serves, across all four
implementations (ArangoDB, Memgraph, SurrealDB, InMemory), plus the portal
reading and authoring surfaces, the SEO prerender path, and the in-game `@wiki`
command and `wiki()` function family.

Out of scope, explicitly:

- The in-game helpfiles under `SharpMUSH.Documentation/Helpfiles/`, indexed by
  `Helpfiles.cs` and served to the telnet `help` command. These are on-disk
  Markdown that never touches `IWikiService` and keep working unchanged.
- `mush-defs.json`, the generated function/command reference behind the client's
  `HelpDrawer`. Its text is extracted from `[SharpFunction]`/`[SharpCommand]`
  attributes in C# and would need the generator's source of truth localized.
- Translating any seeded content. See "Seeding" below.

## Decisions

These were settled before design and constrain everything downstream.

1. **Surface.** Localize the wiki; the `Help:` namespace is the payoff. The
   telnet `help` command is untouched.
2. **Fallback, not 404.** When a reader's locale has no translation, serve the
   fallback page and show a visible notice. A read can never fail for locale
   reasons.
3. **URL.** One canonical slug. Locale comes from the reader's stored
   preference; `?lang=<tag>` overrides it. No locale path prefix, no per-locale
   slugs — `[[WikiLink]]` resolution and the unique slug index stay as they are.
4. **Translation independence.** A translation owns its `Title`,
   `MarkdownSource`, revision history and `Published` flag. It inherits
   `Category`, `Tags` and `IsProtected` from the source page.
5. **Fallback target.** A game-wide configured default locale
   (`Wiki.DefaultLocale`, defaulting to `en`).

Decision 4 is what rules out the obvious implementation of adding a `Locale`
column to `WikiPage`: per-row metadata drifts between languages and would need
enforcement code to prevent it.

## Approach

Translations are **overlay rows hanging off the page**. `WikiPage` keeps
identity and all metadata and keeps holding the source-locale content; a new
`WikiTranslation` holds each translation. Metadata inheritance is structural —
there is nowhere for a translation to store a conflicting category.

Two properties of this shape matter:

- **No schema migration and no content rewrite.** Existing pages already *are* their
  source-locale rows; nothing is restructured or re-rendered. There is one
  additive-column backfill (`SourceLocale`, `WikiRevision.Locale`) stamped once — see
  "`SourceLocale` is materialised once" for why re-deriving it on read is unsafe.
- **Additive service contract.** `GetBySlugAsync` and friends keep their
  signatures, so `WikiController`, `SeoController`, `BotPrerenderMiddleware`,
  `WikiCommands` and `WikiFunctions` compile untouched and adopt localization
  one at a time.

Rejected alternatives:

- **`Locale` as a fourth identity dimension** on `WikiPage`. Cheapest to write,
  but breaks decision 4 structurally, changes every unique index and query in
  three backends, and makes `GetAllPagesAsync`/`GetByCategoryAsync`/
  `GetByTagAsync`/`GetRecentChangesAsync` each return N rows per page unless
  every one learns to collapse by locale.
- **Locale-suffixed slugs** (`markdown-guide.fr`) with a resolution layer. Zero
  DB work, but pollutes the slug space, breaks `[[WikiLink]]`, shows every page
  N times in the admin list, and makes decision 4 impossible.

## Data model

In `SharpMUSH.Library/Models/Wiki/`.

### `WikiPage`

Gains one init-only property:

```csharp
/// <summary>
/// Canonical BCP-47 locale the page was authored in. Never empty on a page read back
/// from storage: Migration_AddWikiTranslations stamps every pre-existing page once.
/// </summary>
public string SourceLocale { get; init; } = string.Empty;
```

This follows the convention the record's own comment already establishes for
`Category`/`Tags`/`Published`: init-only rather than positional, so existing
construction sites keep working.

#### `SourceLocale` is materialised once, never re-derived

An earlier draft of this design had readers treat an empty `SourceLocale` as
"whatever `Wiki.DefaultLocale` currently is". **That was a data-integrity bug.**
Under that rule, an admin changing `wiki_default_locale` silently changes the
authored locale of every page that predates the field: an English page starts
claiming to be French, `UpsertTranslationAsync` begins rejecting `fr` as
"shadowing the source" while accepting `en`, and existing revision history changes
meaning — all with no migration, no audit trail, and nothing to alert on.

Instead, `Migration_AddWikiTranslations` performs a one-time idempotent backfill,
stamping `SourceLocale = Wiki.DefaultLocale` on every row where it is absent or
empty. After that migration the field is authoritative and immutable per page, and
the configured default only affects *new* pages and fallback resolution — never the
interpretation of existing ones.

This costs the design its "no data migration" property, which was worth less than
it sounded: it is a single `UPDATE`-shaped pass over one collection, run once, and
it is the only way the field can mean anything stable. The claim is now narrower
and true: **no schema migration and no rewrite of page content** — one additive
column stamped once.

The backfill cannot infer the authored language of pre-existing prose — it can only
stamp the configured default. **SharpMUSH is pre-production, so that is not a
problem worth engineering around:** a game whose existing content is not in the
configured default can set `wiki_default_locale` first, or simply wipe and reseed.
The migration therefore stays deliberately simple — stamp the default, log the value
and the row count — with no attempt at language detection, no interactive prompt and
no per-page override. Revisit only if this ships to a live game with content that
predates it.

Note that materialising `SourceLocale` is *not* itself a legacy-data concern and does
not become unnecessary pre-production. The bug it fixes is an admin changing
`wiki_default_locale` at any point after pages exist, which happens just as readily
in development.

### `WikiTranslation` (new)

```csharp
public record WikiTranslation(
    string Id,
    string PageId,
    string Locale,
    string Title,
    string MarkdownSource,
    string RenderedHtml,
    string PlainText,
    string LastEditorDbref,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool Published,
    int RevisionNumber);
```

Note what it deliberately lacks: no `Category`, no `Tags`, no `IsProtected`, no
`Slug`. That absence *is* the enforcement of decision 4.

### `WikiRevision`

Gains an init-only `Locale`, so history is a stream per `(PageId, Locale)`.
Existing rows read back as source-locale revisions.

### `WikiTranslationSummary` (new)

```csharp
public record WikiTranslationSummary(
    string Locale, string Title, bool Published, DateTimeOffset UpdatedAt, int RevisionNumber);
```

Enough for the editor's locale list and `hreflang` generation without loading
bodies.

### `LocalizedWikiPage` (new, read model — never stored)

```csharp
public sealed record LocalizedWikiPage(
    WikiPage Page,              // identity + inherited metadata ONLY
    string Locale,              // locale actually served
    string RequestedLocale,
    string Title,               // resolved
    string MarkdownSource,      // resolved
    string RenderedHtml,        // resolved
    string PlainText,           // resolved
    bool Published,
    int RevisionNumber)
{
    public bool IsFallback =>
        !string.Equals(
            CultureInfo.GetCultureInfo(Locale).TwoLetterISOLanguageName,
            CultureInfo.GetCultureInfo(RequestedLocale).TwoLetterISOLanguageName,
            StringComparison.OrdinalIgnoreCase);
}
```

Resolved content sits on the wrapper, never on `Page`. If `Page.Title` stayed
authoritative-looking, a caller would eventually render the English title beside
French body text and nobody would notice for months.

`Published` is the *served* row's flag — the translation's when a translation is
served, the page's when the source is served.

`IsFallback` compares *languages*, not tags: serving `fr` to an `fr-CA` reader
must not raise "showing English", which would banner every Canadian visit.

Both `Locale` and `RequestedLocale` are already-normalised, parseable tags, so the
`GetCultureInfo` calls in `IsFallback` cannot throw. That rests on two things, not
one: `IWikiLocalizationService` is the only thing that constructs this record and it
normalises the *requested* tag first, **and** no unparseable locale can be in the
store to begin with because every write boundary rejects one. The second half is
what makes this an invariant rather than a convention a future caller can break —
see "canonicalised and validated at the write boundary".

## Storage

- `DatabaseConstants.WikiTranslations = "node_wiki_translations"`.
- `Migration_AddWikiTranslations`, modelled on the existing
  `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddWiki.cs`: create the
  collection, unique persistent index on `(PageId, Locale)`, non-unique index on
  `Locale` for listings.
- `InMemoryWikiService` uses `Dictionary<(string PageId, string Locale), WikiTranslation>`.

### The revision index must be corrected, and the three backends disagree today

Translation revisions share a `PageId` with the source page and restart numbering at
1, so `(PageId, RevisionNumber)` is no longer unique. That collides with what is
already deployed — differently in each store:

| Backend | Current revision index | Effect of the first translation revision |
|---|---|---|
| SurrealDB | `wiki_revision_page_rev ON wiki_revision FIELDS pageId, revisionNumber UNIQUE` (`SurrealDatabase.Migration.cs:97`) | **Rejected.** Translation revision 1 collides with the source's revision 1. |
| ArangoDB | `Fields = ["PageId", "RevisionNumber"]`, `Persistent`, no `Unique` (`Migration_AddWiki.cs:101`) | Accepted; no constraint. |
| Memgraph | two *separate* non-unique indexes, on `pageId` and on `revisionNumber` (`MemgraphDatabase.Migration.cs:124`) | Accepted; no constraint. |

This is worse than a single store needing a fix. A numbering bug would fail loudly
on SurrealDB, pass on ArangoDB and pass silently on Memgraph — and since CI runs a
three-backend matrix, the symptom is a baffling one-of-three red that looks like
flakiness.

So the requirement is not "fix SurrealDB" but **make all three agree**:

- Every backend defines a unique constraint on `(PageId, Locale, RevisionNumber)`.
  SurrealDB must *drop* `wiki_revision_page_rev` before redefining it; ArangoDB's
  index becomes `Unique = true` over the three fields; Memgraph gains a real
  composite uniqueness constraint rather than two independent indexes.
- Pre-existing revision rows have no `Locale`, so the backfill that stamps
  `WikiPage.SourceLocale` must stamp `WikiRevision.Locale` in the same migration —
  before the new unique constraint is created, or creation fails on the null column.
- The cross-backend integration test asserts the constraint *rejects* a duplicate
  `(PageId, Locale, RevisionNumber)` on all three, not merely that the happy path
  works. A test that only writes valid data cannot tell a real constraint from a
  missing one, which is precisely how these three drifted apart.

## Configuration

New `WikiOptions` record in `SharpMUSH.Configuration/Options/`, added to
`SharpMUSHOptions`:

```csharp
public record WikiOptions(
    [property: SharpConfig(
        Name = "wiki_default_locale",
        Category = "Content",
        Description = "Locale wiki pages fall back to when a reader's locale has no translation",
        Group = "Wiki",
        Order = 1,
        ValidationPattern = @"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    string DefaultLocale = "en");
```

Because the admin config pages are schema-driven off `[SharpConfig]`, this
appears in `/admin/config` with no UI work.

**The default is a real default, not a `required` member.** `DefaultLocale = "en"`
is supplied on the parameter, so a configuration file that omits
`wiki_default_locale` binds to `en` rather than to null or empty. Every other
`SharpMUSHOptions` member is `required`; this one deliberately is not, because
resolution's terminal step depends on it always having a usable value.

**It is validated at startup, not at first use.** `ValidateSharpOptions` rejects a
`DefaultLocale` that `CultureInfo.GetCultureInfo` cannot parse, failing startup with
the offending value named. The `ValidationPattern` above gives the admin UI a
client-side check; the startup validation is the authority, because the pattern
cannot know which tags actually exist. Deferring this to first use would surface a
typo as a `CultureNotFoundException` inside a page render, long after the admin who
made it has moved on.

**Which locales may a translation use?** Any tag `CultureInfo.GetCultureInfo`
accepts — *not* only `ILocalizationService.AvailableLocales`. A game should be
able to translate its wiki into Spanish even though the portal chrome has no
Spanish resx: the chrome falls back to English, the content does not. The
editor's locale dropdown offers `AvailableLocales` ∪ locales that already have
translations, plus a free-text field for anything else.

### Locales are canonicalised and validated at the write boundary

That free-text field is why this matters. "Any parseable tag" is a permissive
*input* rule, not a licence to persist whatever arrives.

```csharp
// SharpMUSH.Library/Services/WikiHelpers.cs, beside the existing NormalizeCategory
/// <summary>
/// Canonical form of a locale tag, or Error when it is not a locale at all.
/// Canonical means CultureInfo's own casing — "pt-br" and "PT-BR" both become "pt-BR" —
/// so the unique (PageId, Locale) index cannot be defeated by casing.
/// </summary>
public static OneOf<string, Error<string>> NormalizeLocale(string? locale);
```

Applied at every point a locale enters storage or configuration:

| Entry point | Behaviour on an invalid tag |
|---|---|
| `UpsertTranslationAsync(locale)` | `Error<string>`; nothing written |
| `CreateAsync(sourceLocale)` | `Error<string>`; no page created |
| `Migration_AddWikiTranslations` backfill | fails the migration loudly |
| `WikiOptions.DefaultLocale` | fails startup validation |
| `?lang=` on a read | **not** an error — treated as absent, per Error handling |

The read path is deliberately the odd one out: a reader typing a bad `?lang=`
should get the default page, not a 400. A *writer* persisting a bad locale is a
different thing entirely, because it corrupts the store for every later read.

This is what actually makes `LocalizedWikiPage.IsFallback`'s no-throw claim true.
Without it, the invariant rested on "the only construction point normalises first",
which is a convention a future caller can break; with it, an unparseable locale
cannot be in the database to begin with. Case canonicalisation also closes a
quieter hole: without it `pt-BR` and `pt-br` are two rows that the unique index
happily accepts and the resolver treats as unrelated.

## Resolution

`IWikiService` stays a *storage* contract and learns nothing about fallback.
Backends get five mechanical CRUD methods each; the rules live in one place.

```csharp
public sealed record LocaleResolution(string Locale, bool IsFallback);

public interface IWikiLocaleResolver   // no DB, no HTTP
{
    LocaleResolution Resolve(string? requested, string sourceLocale, IReadOnlyCollection<string> available);
}
```

Chain, in order:

1. Normalise `requested` — null, blank or unparseable becomes `Wiki.DefaultLocale`.
2. Exact match against available translations, case-insensitive.
3. Neutral-parent match: `fr-CA` finds an `fr` translation.
4. `Wiki.DefaultLocale`, if a translation exists for it.
5. The page's `SourceLocale` — the `WikiPage` row itself, which always exists.

Step 5 is the terminal guarantee. Decision 5 names the configured default as the
fallback target, but a page authored only in French on an `en`-default game would
then have nothing to serve, and decision 2 ruled out 404.

### Draft translations must not leak

Decision 4 exists so a translator can draft French while English stays live. That
means `available` is **not** simply "every translation row". The caller filters it
by visibility before calling `Resolve`:

- A reader without edit permission sees only translations with `Published == true`.
- A reader with edit permission on the page sees unpublished ones too, so they can
  preview their own draft at `?lang=fr`.

The resolver itself stays permission-blind — it takes the already-filtered set.
That keeps the fallback rules unit-testable without an auth graph, and puts the
visibility decision in `IWikiLocalizationService`, which already has the caller's
identity. An unpublished French translation therefore falls through to step 4 or
5 for an ordinary reader, exactly as if it did not exist, banner included.

### `IWikiLocalizationService` (new)

One implementation, no per-backend variants. Depends on `IWikiService`,
`IWikiLocaleResolver` and `IOptions<SharpMUSHOptions>`. Owns
`GetLocalizedBySlugAsync(slug, category, ns, locale)` and is the only thing that
constructs `LocalizedWikiPage`, so the "resolved content lives on the wrapper"
invariant has exactly one enforcement point. Controllers and pages inject this,
not `IWikiService`.

## `IWikiService` additions

Purely additive:

```csharp
Task<IReadOnlyList<WikiTranslationSummary>>  GetTranslationsAsync(string pageId);
Task<OneOf<WikiTranslation, NotFound>>       GetTranslationAsync(string pageId, string locale);
Task<OneOf<WikiTranslation, Error<string>>>  UpsertTranslationAsync(
    string pageId, string locale, string title, string markdown,
    string editorDbref, string? editSummary, bool published,
    int? expectedRevisionNumber);
Task<OneOf<None, NotFound>>                  DeleteTranslationAsync(string pageId, string locale, string editorDbref);
Task<IReadOnlyList<WikiRevision>>            GetRevisionsForLocaleAsync(string pageId, string locale, int skip, int take);
```

`GetRevisionsForLocaleAsync` is a distinct name rather than an overload of the
existing `GetRevisionsAsync(pageId, skip, take)`. An overload differing only by an
inserted `string?` invites a silent mis-bind at a call site that passes positional
ints, and the compiler would not complain.

### `UpsertTranslationAsync` needs optimistic concurrency

The unique `(PageId, Locale)` index protects concurrent *inserts* and nothing else.
Two translators editing the same French page both read `RevisionNumber = 4`, both
compute 5, and both write it: one translator's prose is silently lost and the
revision stream now has two different revision 5s — or one, depending on which
write landed last. The index is perfectly happy; it is the same row either way.

So the write is a compare-and-swap:

- **`expectedRevisionNumber` is the revision the editor loaded.** The update applies
  only if the stored `RevisionNumber` still matches, and the revision append happens
  in the same transaction as the row update. Backends that cannot span both in one
  transaction must instead make the update conditional on the expected value and
  treat "zero rows affected" as the conflict signal.
- **`null` means "create only".** If a translation already exists, that is an
  `Error<string>` rather than a blind overwrite, which is what a caller who believed
  it was creating a new translation should get.
- **A detected conflict returns `Error<string>` and is not retried automatically.**
  Retrying would re-apply the loser's stale markdown on top of the winner's, which is
  exactly the data loss this exists to prevent. The editor reloads and the human
  decides. (The single automatic retry mentioned under Error handling applies only to
  the insert race, where no content can be lost.)

Otherwise `UpsertTranslationAsync` mirrors `UpdateAsync`: bump `RevisionNumber`, write
a `WikiRevision` carrying the `Locale`, re-render HTML and plain text through the
same `WikiMarkdigPipeline`. `DeleteAsync` on the source page extends its existing
revision cleanup to cascade over translations and their revisions.

## HTTP surface

The API already threads `?ns=` and `?category=` as query params, so `?lang=`
follows the existing convention.

| Method | Route | Behaviour |
|---|---|---|
| GET | `/api/wiki/ns/{ns}/{category}/{slug}?lang=` | localized DTO + `locale`, `requestedLocale`, `isFallback`, `availableLocales` |
| GET | `/api/wiki/{slug}/translations?ns=&category=` | `WikiTranslationSummary[]` |
| PUT | `/api/wiki/{slug}/translations/{locale}?ns=&category=` | upsert |
| DELETE | `/api/wiki/{slug}/translations/{locale}?ns=&category=` | delete one translation |
| GET | `/api/wiki/{slug}/revisions?lang=` | that locale's revision stream |

Writes are gated on the same permission as editing the page, and on the *source*
page's `IsProtected`.

The listing endpoints (`recent`, `ns/{ns}`, `pages`, `category/{c}`, `tag/{t}`)
gain `?lang=` and return localized titles. They still return one row per page.

## Portal surfaces

**Reading.** Client `WikiService` sends `lang` from the same `localStorage`
`"locale"` key `LanguagePicker` writes, with a `?lang=` route override winning.
`WikiDisplay.razor` shows a dismissible `MudAlert` above the body when
`isFallback`, text from `SharedResource.resx`, with a link to create the
translation if the reader may edit. Dismissal is per-session only — a
translation gap should keep nagging. A language chip row lists available
translations; clicking one sets `?lang=`.

**Authoring.** `WikiEdit.razor` gains a locale selector: source locale, each
existing translation, and "Add a translation…". On a non-source locale the
category and tags fields render **visible but disabled** with an "inherited from
source" hint — decision 4 made legible rather than mysterious. `Published` stays
editable, because a translation owns its own flag; that is the whole mechanism
behind drafting French while English stays live. (There is no protection control on
this page to disable — `IsProtected` is page-level and surfaced only through
`/admin/wiki`'s batch actions.)

`/wiki/{slug}/edit?lang=fr` is the deep link. `WikiPageHistory` and `WikiPageDiff`
take `?lang=` and show that locale's stream.

The editor holds the loaded `RevisionNumber` and passes it as
`expectedRevisionNumber` on save. On a conflict it surfaces "this translation
changed while you were editing" and offers a reload — it must not silently retry,
for the reason given under `UpsertTranslationAsync`.

**Staff.** `/admin/wiki` gains a translations-coverage column (`en · fr`) and a
locale filter. This is what makes untranslated Help pages findable, and is the
difference between a feature people use and one they forget exists.

## SEO

`BotPrerenderMiddleware` resolves through `IWikiLocalizationService`, emits
`<link rel="alternate" hreflang="…">` for every available translation plus
`x-default` at the configured default, and keeps the canonical at the unsuffixed
slug. `docs/design/url-strategy.md` gains a short "Locale" subsection recording
that `?lang=` is the only locale mechanism and never changes the canonical slug.

## In-game

- `@WIKI` reads the executor's existing `LOCALE` attribute (already read at
  `SharpMUSH.Implementation/Commands/MoreCommands.cs:2987`) rather than
  inventing a switch, with a new `/SOURCE` switch to force the source locale.
- `wiki()` goes `MaxArgs` 2 → 3, the third argument an optional locale
  defaulting to the executor's `LOCALE`.
- `wikilist()` and `wikirecent()` return localized titles for the executor's
  locale.

## Seeding

`StartupHandler.SeedWikiPagesAsync` keeps seeding English source pages and gains
`SourceLocale` on them. **No translations are seeded.** Machine-quality French
help is worse than a visible gap, and the fallback banner makes the gap
actionable. Translating the seeded Help pages is content work with native
review, tracked separately from this change.

## Error handling

| Case | Behaviour |
|---|---|
| Malformed `lang` on a **read** | treated as absent; falls to configured default. Never a 400. Logged at Debug. |
| Malformed locale on a **write** | `Error<string>`; nothing persisted. See "canonicalised and validated at the write boundary". |
| Invalid `Wiki.DefaultLocale` | startup fails, naming the value |
| Upsert on a nonexistent page | `Error<string>` |
| Upsert where `locale == page.SourceLocale` | `Error<string>` — no row may shadow the source; edit the page itself |
| Protected source page, non-admin editor | 403, same gate as page edit |
| Concurrent **insert** race on `(PageId, Locale)` | unique index rejects; `Error<string>`, retried once — no content can be lost |
| Concurrent **update** (stale `expectedRevisionNumber`) | `Error<string>`, **never** retried; the editor reloads and the human decides |
| `expectedRevisionNumber` null and a translation exists | `Error<string>` — create-only was requested |
| Deleting the last translation | allowed |
| Deleting the source page | cascades to translations and their revisions |

## Testing

- **`WikiLocaleResolverTests`** — table-driven over all five chain steps: exact,
  neutral-parent, region-to-neutral, configured default, source-locale terminal,
  unparseable tag. Plus the `IsFallback` language-vs-tag rule (`fr-CA` served
  `fr` is not a fallback; `fr-CA` served `en` is). No DB, no HTTP.
- **`InMemoryWikiServiceTests`** — translation CRUD, per-locale revision
  streams, cascade delete, source-shadow rejection.
- **`WikiServiceIntegrationTests`** — the same CRUD parameterised across all three
  real backends, and critically the *negative* cases: a duplicate
  `(PageId, Locale, RevisionNumber)` must be **rejected** on all three, and a
  translation revision numbered 1 must be **accepted** alongside a source revision
  1. Only the negative assertion distinguishes a real constraint from a missing
  one, which is how the three backends drifted apart in the first place.
- **Concurrency** — two upserts with the same `expectedRevisionNumber` produce one
  success and one `Error<string>`, and the losing markdown does not appear in any
  revision. Needs a real backend, so it lives here rather than in the in-memory
  tests, whose dictionary cannot reproduce the race.
- **`NormalizeLocale`** — `pt-br`, `PT-BR` and `pt-BR` all canonicalise to `pt-BR`
  and therefore collide on the unique index; `not-a-locale` is rejected at every
  write entry point.
- **Backfill migration** — running it twice is a no-op, it stamps both
  `WikiPage.SourceLocale` and `WikiRevision.Locale`, and it runs before the new
  unique constraint is created.
- **Draft visibility** — an unpublished `fr` translation is invisible to an
  anonymous reader (who gets the fallback plus banner) and visible to an editor
  at `?lang=fr`. This is the test most likely to catch a regression that leaks
  unfinished content, so it is a first-class case, not an afterthought.
- **bUnit** — `WikiDisplay` renders the fallback banner iff `isFallback`;
  `WikiEdit` disables inherited metadata when a non-source locale is selected.
- **`WikiStartupSeedingTests`** — seeding stays idempotent with `SourceLocale`
  present.

## Risks

- **Three hand-written backends, already disagreeing.** The five new CRUD methods
  are mechanical, but the existing revision indexes differ across the three stores
  today — unique on SurrealDB, non-unique on ArangoDB, absent on Memgraph — so a
  numbering bug fails on one and passes on two. The cross-backend integration test,
  including its negative cases, is the mitigation and must be written **before** the
  backend implementations.
- **The backfill is the only step that writes to existing rows** — but it is not a
  one-way door while SharpMUSH is pre-production, because wiping and reseeding the
  database is an acceptable recovery. That is the reason this design does not carry
  a rollback path, a language-detection heuristic or a per-page override for it: all
  three would be speculative complexity for a scenario the project does not have
  yet. The migration logs the locale it stamped and the row count, which is enough
  to notice a wrong default. **This bullet is the one to revisit first if a live
  game ever adopts SharpMUSH with existing wiki content.**
- **Listing performance.** Localized listings need the translation title per
  page. For Arango this is a single `LET` subquery per row; measure before
  adding a denormalized title cache, and do not add one pre-emptively.
- **`Category` is part of page identity.** Since a translation cannot carry its
  own category, a game cannot localize category names via this mechanism.
  Category display names are a separate concern and out of scope.
