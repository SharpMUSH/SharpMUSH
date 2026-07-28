# Area 5: Wiki / Shared Content — TODO

## Pre-Implementation
- [x] Review & confirm decisions (5.1–5.5) with project owner
- [x] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [x] Define wiki page schema (collection: title, namespace, markdown, rendered_html, text_plain, metadata) — `SharpMUSH.Library/Models/Wiki/WikiPage.cs`, `node_wiki_pages` / `node_wiki_revisions` collections
- [x] Implement Markdig pipeline (extensions, DisableHtml, wiki-link resolver) — `WikiMarkdigPipeline.cs`
- [x] Implement wiki-link extension (`[[Page Name]]` → resolved links, redlink CSS) — `WikiLinkExtension.cs`; redlink detection resolved at VIEW time: `WikiDisplay` batch-checks link targets via `POST /api/wiki/exists` and tags missing ones with `.wiki-redlink` (always fresh; no stale-cache invalidation needed)
- [x] Wiki CRUD: create, read, update (with revision history) — `IWikiService` + ArangoDB/Memgraph/SurrealDB/in-memory implementations
- [x] Revision history storage (full snapshots) — `WikiRevision.cs`
- [x] Page protection/locking (Royalty+ can protect pages) — `IsProtected` flag + `PUT /api/wiki/{slug}/protection` (Wizard role)
- [x] @wiki in-game commands — `WikiCommands.cs` + `Commands/WikiCommand/`: view/list/search/recent/history, create/edit/append, delete/protect/unprotect/publish/unpublish/category/tag (+ `/noeval`, `/source`); plus `wiki()`, `wikilist()`, `wikisearch()`, `wikirecent()` softcode functions (`WikiFunctions.cs`); helpfile `sharpwiki.md`
- [x] Markdown → MString custom renderer (for in-game wiki display) — `RecursiveMarkdownHelper` pipeline extended with wiki links, generic attributes, directives, task lists
- [x] HTTP handler: serve wiki pages for portal — `WikiController.cs` (CRUD, recent, namespace listing, revisions, protection, cache invalidation)
- [ ] NATS event on wiki edit (`portal.wiki.changes`)
- [x] Rendered HTML cache (invalidate on edit) — `PrerenderCacheService` + `BotPrerenderMiddleware`; invalidated on PUT/DELETE
- [x] Plain text extraction for search index (on write) — `WikiMarkdigPipeline.ExtractPlainText`

## Web UI
- [x] Wiki page view component (`/wiki/Page_Name`, `/wiki/{ns}/Page_Name`) — `WikiPage.razor`, `WikiView.razor`, `WikiDisplay.razor`
- [x] Wiki editor component (Markdown textarea + preview) — `WikiEdit.razor`, `/wiki/{slug}/edit`
- [x] Wiki history/diff view — `WikiHistoryDialog.razor` (revision list + line diff vs current via `LineDiff.cs`); per-revision Restore button → `POST /api/wiki/{slug}/rollback` (restore is a new revision; history preserved); also `@wiki/rollback <page>=<rev>` in-game
- [x] Recent changes list — `/wiki` index (`WikiIndex.razor`) via `GET /api/wiki/recent`
- [x] Namespace browsing (all Character: pages, all Help: pages) — `/wiki` index tabs via `GET /api/wiki/ns/{ns}`
- [x] Wiki content CSS — links, redlinks, headings, tables, code, blockquotes in `wwwroot/css/custom.css`

## Admin & Semantic Layer
- [x] Page metadata: Category / Tags / Published(draft) on `WikiPage` — normalised (lower-case, de-duped), `SetMetadataAsync` in all four providers (metadata changes do not create revisions)
- [x] Listing APIs — `GET /api/wiki/pages` (X-Total-Count header), `GET /api/wiki/category/{cat}`, `GET /api/wiki/tag/{tag}`; anonymous callers only see Published pages (drafts 404/are filtered)
- [x] Batch administration — `POST /api/wiki/batch/protect` + `batch/delete` (Wizard), `{Succeeded, Failed}` result; `/admin/wiki` is a full multi-select grid (paging, namespace filter, protect/unprotect/delete, per-row metadata dialog)
- [x] Editor metadata — category / tag chips / published switch in `WikiEdit.razor`, saved via the metadata endpoint only when changed
- [x] Asset uploads — `POST/GET/DELETE /api/wiki-assets` (`WikiAssetController.cs`): 10 MB cap, image whitelist, SVG script-scan; filesystem store with sha256 + sidecar metadata (`FileSystemWikiAssetService.cs`); `/admin/wiki/assets` manager; `WikiAssetPicker.razor` + "Insert image" button in the editor
- [x] Markdown directives — `WikiDirectiveExtension.cs`: `::: category X`, `::: tag X`, `::: pagelist NS`, `::: recent N` render live listings client-side (`WikiDirectiveBlock.razor`); args validated/escaped, unknown containers keep default rendering
- [x] SEO — `/sitemap.xml` (published pages only) + `/robots.txt` (`SeoController.cs`); JSON-LD schema.org Article in bot prerender HTML

## Localization
- [x] Per-locale content via `WikiTranslation` overlay rows keyed `(PageId, Locale)` — a translation owns Title / MarkdownSource / Published / revisions and inherits Category / Tags / IsProtected structurally; no schema migration and no content rewrite (one additive-column backfill)
- [x] `Wiki.DefaultLocale` (`wiki_default_locale`, default `en`, validated at startup) in `/admin/config/wiki`; `WikiPage.SourceLocale` is materialised once by the migration and never re-derived, so changing the default cannot relabel existing pages
- [x] Fallback, never 404 — `IWikiLocaleResolver` (pure, 5-step chain) + `IWikiLocalizationService` (visibility filtering, the only `LocalizedWikiPage` factory)
- [x] Drafts do not leak — the candidate set is filtered before resolution; an unpublished translation is unreachable for readers without edit permission
- [x] Drafts do not leak **in-game either** — every discovery surface (`@wiki/list`, `@wiki/search`, `@wiki/recent`, `wikilist()`, `wikisearch()`, `wikirecent()`) filters unpublished pages. `WikiCommandHelper.CanSeeDrafts` (wizard-only, matching the wizard-only `@wiki/publish`/`@wiki/unpublish`) is the in-game counterpart of the portal's `wiki.read` scope; softcode functions never see drafts, exactly as `wiki()` never sees draft translations
- [x] `?lang=` on the page read, all five listings and `{slug}/revisions`; translation CRUD at `/api/wiki/{slug}/translations[/{locale}]`, with `expectedRevisionNumber` optimistic concurrency answering 409 on a conflict (never retried)
- [x] Unique `(PageId, Locale, RevisionNumber)` constraint on all three DB backends, which disagreed before this change; asserted by a cross-backend test that checks the constraint *rejects* duplicates
- [x] Reader UI — dismissible fallback notice (per-session) + language chip row in `WikiDisplay.razor`
- [x] Authoring — locale selector in `WikiEdit.razor` with inherited Category/Tags visibly disabled; `/wiki/{ns}/{cat}/{slug}/edit?lang=`
- [x] Per-locale history and diff (`?lang=` on `WikiPageHistory` / `WikiPageDiff`)
- [x] Staff — translation-coverage column and locale filter (incl. "missing only") on `/admin/wiki`
- [x] SEO — `hreflang` alternates + `x-default` + `<html lang>` in the bot prerender, `xhtml:link` in the sitemap; canonical unchanged
- [x] In-game — `@wiki` reads the executor's `LOCALE`, `/SOURCE` forces the source; `wiki()` takes an optional third locale argument and a `locale` field
- [x] Locale-aware in-game search — `@wiki/search` and `wikisearch()` scan translation bodies as well as source bodies via `IWikiService.GetAllTranslationsAsync` (a plain paged bulk fetch, symmetric with `GetAllPagesAsync`, four mechanical implementations, no query-language work). Results dedupe by page, so a page matching in source *and* translation is one row; the reported locale prefers the reader's own and renders as `@wiki/view`'s `[xx]` marker when it is not the page's source locale; `/SOURCE` matches source bodies only. `wikisearch()` still returns bare references — that contract is pinned by `WikiList_ReturnsReferencesAndIsLocaleIndependent`
- [x] Seeded pages are stamped `SourceLocale = "en"`; no translations are seeded
- [x] Tests — `WikiLocaleResolverTests`, `WikiLocalizationServiceTests` (draft visibility first-class), `WikiHelpersLocaleTests`, `LocalizedWikiPageTests`, `WikiTranslationIntegrationTests` (cross-backend, including the negative constraint and concurrency cases), `WikiDisplayFallbackTests` / `WikiEditLocaleTests` (bUnit), `WikiStartupSeedingTests` (seed stamping, seed idempotency, no seeded translations, no unstamped seeded page after the migration)

## Testing
- [x] Markdig pipeline: all extensions render correctly — `WikiMarkdigPipelineTests.cs`
- [x] Wiki-link resolution: existing pages, broken links (redlinks) — `WikiMarkdigPipelineTests.cs`
- [x] Revision history: create, view diff, rollback — `InMemoryWikiServiceTests.cs`, `WikiHttpControllerTests.cs`, `LineDiffTests.cs`, `WikiRollbackAndRedlinkApiTests.cs` (rollback + exists endpoints), `WikiRedlinkRenderingTests` (bUnit), `WikiCommandTests` (@wiki/rollback)
- [x] Permission checks: wizard delete, protected-page edit enforcement — DELETE/protection require Wizard role; protected pages reject non-Wizard edits (`WikiControllerProtectionTests.cs`). Finer-grained owner/royalty edit semantics remain a follow-up
- [x] Metadata/listing/batch/visibility: `WikiMetadataServiceTests.cs`, `WikiControllerVisibilityTests.cs`, `WikiAdminApiTests.cs` (integration)
- [x] Assets: `FileSystemWikiAssetServiceTests.cs`, `WikiAssetControllerTests.cs` (whitelist, SVG script rejection, cache headers)
- [x] Directives: `WikiMarkdigPipelineTests.cs` (placeholders, arg validation, injection rejection), `WikiDirectiveBlockTests.cs` (bUnit)
- [x] SEO: `SeoControllerTests.cs`, `SeoEndpointTests.cs` (integration)
- [x] @wiki commands produce correct MString output — `WikiCommandTests.cs` (18 tests: create/view/list/search/append/history/protection/tags + locale, `/source` and draft-translation visibility + helpfile loads), `WikiFunctionUnitTests.cs` (15 tests), `WikiSyntaxInGameRenderingTests.cs` (13 tests)

## Remaining (out of portal scope or follow-up)
- NATS `portal.wiki.changes` event on edit
- Owner-edit / royalty-edit-any permission tiers (currently: any authenticated user edits unprotected pages, Wizard edits everything)
- Translating the seeded Help pages — content work needing native review, deliberately not machine-translated
- Locale-aware search for the web omnisearch box — the in-game side is done (see Localization above); the portal's search surface still matches source `PlainText` only
- Search cost: `ListWiki.SearchPagesAsync` is a full in-process scan of both content streams, capped at 100 results, and scanning translations roughly doubles the rows read. Deliberate at in-game wiki sizes — no index, no query-language work in any of the four backends, and the same code path for all of them. Measure before replacing it with the area-14 full-text index
- Localized category *display* names — Category is part of page identity, so it cannot be translated through the overlay
- Listing performance: localized listings resolve per row. Measure before adding a denormalized title cache
- `@wiki/list`'s header count still comes from `CountPagesAsync`, which counts drafts. The rows are filtered, so no draft title or reference is disclosed, but a mortal comparing "N page(s)" against the rows can infer how many drafts exist in the first 100. Fixing it properly means a published-aware `CountPagesAsync` in all four providers — a cross-cutting change wanting its own review, not a rider on the visibility fix
- `WikiEdit` collects an edit summary and a minor-edit flag and discards both (predates localization)
- The `SourceLocale` backfill carries no rollback path, no language detection and no per-page override. That is deliberate while SharpMUSH is pre-production, because wiping and reseeding is acceptable recovery; the migration logs the locale it stamped and the row count, which is enough to notice a wrong default. **Revisit this first if a live game with existing wiki content ever adopts SharpMUSH.**
