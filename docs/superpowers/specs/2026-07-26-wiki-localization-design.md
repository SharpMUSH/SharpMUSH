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

- **No data migration.** Existing pages already are their source-locale rows.
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
/// Locale the page was authored in. Empty on documents predating this field;
/// readers must treat empty as Wiki.DefaultLocale.
/// </summary>
public string SourceLocale { get; init; } = string.Empty;
```

This follows the convention the record's own comment already establishes for
`Category`/`Tags`/`Published`: init-only rather than positional, so existing
construction sites and stored documents keep working.

The default is `string.Empty`, not `"en"` — a property initializer cannot read
configuration, and hardcoding `"en"` would silently mislabel every pre-existing
page on a non-English game. `IWikiLocalizationService` normalises empty to
`Wiki.DefaultLocale` on read, which is what keeps "no data migration" true.

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

Both `Locale` and `RequestedLocale` are already-normalised, parseable tags. That
is guaranteed rather than defended: `IWikiLocalizationService` is the only thing
that constructs this record and it normalises first, so the `GetCultureInfo`
calls in `IsFallback` cannot throw.

## Storage

- `DatabaseConstants.WikiTranslations = "node_wiki_translations"`.
- `Migration_AddWikiTranslations`, modelled on the existing
  `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddWiki.cs`: create the
  collection, unique persistent index on `(PageId, Locale)`, non-unique index on
  `Locale` for listings.
- `WikiRevisions` gains a `(PageId, Locale, RevisionNumber)` index. The existing
  `(PageId, RevisionNumber)` index stays so pre-existing reads keep working.
- Equivalent collections and constraints in SurrealDB and Memgraph.
- `InMemoryWikiService` uses `Dictionary<(string PageId, string Locale), WikiTranslation>`.

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
        Order = 1)]
    string DefaultLocale);
```

Because the admin config pages are schema-driven off `[SharpConfig]`, this
appears in `/admin/config` with no UI work.

**Which locales may a translation use?** Any tag `CultureInfo.GetCultureInfo`
accepts — *not* only `ILocalizationService.AvailableLocales`. A game should be
able to translate its wiki into Spanish even though the portal chrome has no
Spanish resx: the chrome falls back to English, the content does not. The
editor's locale dropdown offers `AvailableLocales` ∪ locales that already have
translations, plus a free-text field for anything else.

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
    string editorDbref, string? editSummary, bool published);
Task<OneOf<None, NotFound>>                  DeleteTranslationAsync(string pageId, string locale, string editorDbref);
Task<IReadOnlyList<WikiRevision>>            GetRevisionsForLocaleAsync(string pageId, string locale, int skip, int take);
```

`GetRevisionsForLocaleAsync` is a distinct name rather than an overload of the
existing `GetRevisionsAsync(pageId, skip, take)`. An overload differing only by an
inserted `string?` invites a silent mis-bind at a call site that passes positional
ints, and the compiler would not complain.

`UpsertTranslationAsync` mirrors `UpdateAsync`: bump `RevisionNumber`, write a
`WikiRevision` carrying the `Locale`, re-render HTML and plain text through the
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
category/tags/protection fields render **visible but disabled** with an
"inherited from source" hint — decision 4 made legible rather than mysterious.
`/wiki/{slug}/edit?lang=fr` is the deep link. `WikiPageHistory` and
`WikiPageDiff` take `?lang=` and show that locale's stream.

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
| Malformed or unknown `lang` tag | treated as absent; falls to configured default. Never a 400. Logged at Debug. |
| Upsert on a nonexistent page | `Error<string>` |
| Upsert where `locale == page.SourceLocale` | `Error<string>` — no row may shadow the source; edit the page itself |
| Protected source page, non-admin editor | 403, same gate as page edit |
| Concurrent upsert on same `(PageId, Locale)` | unique index rejects; surfaced as `Error<string>`, retried once |
| Deleting the last translation | allowed |
| Deleting the source page | cascades to translations and their revisions |

## Testing

- **`WikiLocaleResolverTests`** — table-driven over all five chain steps: exact,
  neutral-parent, region-to-neutral, configured default, source-locale terminal,
  unparseable tag. Plus the `IsFallback` language-vs-tag rule (`fr-CA` served
  `fr` is not a fallback; `fr-CA` served `en` is). No DB, no HTTP.
- **`InMemoryWikiServiceTests`** — translation CRUD, per-locale revision
  streams, cascade delete, source-shadow rejection.
- **`WikiServiceIntegrationTests`** — the same CRUD and index-uniqueness
  behaviour parameterised across all three real backends, matching the
  cross-backend shape the slug-normalisation fix used.
- **Draft visibility** — an unpublished `fr` translation is invisible to an
  anonymous reader (who gets the fallback plus banner) and visible to an editor
  at `?lang=fr`. This is the test most likely to catch a regression that leaks
  unfinished content, so it is a first-class case, not an afterthought.
- **bUnit** — `WikiDisplay` renders the fallback banner iff `isFallback`;
  `WikiEdit` disables inherited metadata when a non-source locale is selected.
- **`WikiStartupSeedingTests`** — seeding stays idempotent with `SourceLocale`
  present.

## Risks

- **Three hand-written backends.** The five new CRUD methods are mechanical, but
  index semantics differ per store. The cross-backend integration test is the
  mitigation and should be written before the backend implementations.
- **Listing performance.** Localized listings need the translation title per
  page. For Arango this is a single `LET` subquery per row; measure before
  adding a denormalized title cache, and do not add one pre-emptively.
- **`Category` is part of page identity.** Since a translation cannot carry its
  own category, a game cannot localize category names via this mechanism.
  Category display names are a separate concern and out of scope.
