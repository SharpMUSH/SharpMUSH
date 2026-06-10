# Area 5: Wiki / Shared Content — TODO

## Pre-Implementation
- [x] Review & confirm decisions (5.1–5.5) with project owner
- [x] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [x] Define wiki page schema (collection: title, namespace, markdown, rendered_html, text_plain, metadata) — `SharpMUSH.Library/Models/Wiki/WikiPage.cs`, `node_wiki_pages` / `node_wiki_revisions` collections
- [x] Implement Markdig pipeline (extensions, DisableHtml, wiki-link resolver) — `WikiMarkdigPipeline.cs`
- [x] Implement wiki-link extension (`[[Page Name]]` → resolved links, redlink CSS) — `WikiLinkExtension.cs`; `.wiki-redlink` styled in `custom.css` (redlink *detection* still always false — needs existence check at render time)
- [x] Wiki CRUD: create, read, update (with revision history) — `IWikiService` + ArangoDB/Memgraph/SurrealDB/in-memory implementations
- [x] Revision history storage (full snapshots) — `WikiRevision.cs`
- [x] Page protection/locking (Royalty+ can protect pages) — `IsProtected` flag + `PUT /api/wiki/{slug}/protection` (Wizard role)
- [x] @wiki in-game commands — `WikiCommands.cs` + `Commands/WikiCommand/`: view/list/search/recent/history, create/edit/append, delete/protect/unprotect/publish/unpublish/category/tag (+ `/noeval`); plus `wiki()`, `wikilist()`, `wikisearch()`, `wikirecent()` softcode functions (`WikiFunctions.cs`); helpfile `sharpwiki.md`
- [x] Markdown → MString custom renderer (for in-game wiki display) — `RecursiveMarkdownHelper` pipeline extended with wiki links, generic attributes, directives, task lists
- [x] HTTP handler: serve wiki pages for portal — `WikiController.cs` (CRUD, recent, namespace listing, revisions, protection, cache invalidation)
- [ ] NATS event on wiki edit (`portal.wiki.changes`)
- [x] Rendered HTML cache (invalidate on edit) — `PrerenderCacheService` + `BotPrerenderMiddleware`; invalidated on PUT/DELETE
- [x] Plain text extraction for search index (on write) — `WikiMarkdigPipeline.ExtractPlainText`

## Web UI
- [x] Wiki page view component (`/wiki/Page_Name`, `/wiki/{ns}/Page_Name`) — `WikiPage.razor`, `WikiView.razor`, `WikiDisplay.razor`
- [x] Wiki editor component (Markdown textarea + preview) — `WikiEdit.razor`, `/wiki/{slug}/edit`
- [x] Wiki history/diff view — `WikiHistoryDialog.razor` (revision list + line diff vs current via `LineDiff.cs`); rollback not yet implemented
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

## Testing
- [x] Markdig pipeline: all extensions render correctly — `WikiMarkdigPipelineTests.cs`
- [x] Wiki-link resolution: existing pages, broken links (redlinks) — `WikiMarkdigPipelineTests.cs`
- [x] Revision history: create, view diff — `InMemoryWikiServiceTests.cs`, `WikiHttpControllerTests.cs` (revisions endpoints), `LineDiffTests.cs`; rollback untested (no rollback feature yet)
- [x] Permission checks: wizard delete, protected-page edit enforcement — DELETE/protection require Wizard role; protected pages reject non-Wizard edits (`WikiControllerProtectionTests.cs`). Finer-grained owner/royalty edit semantics remain a follow-up
- [x] Metadata/listing/batch/visibility: `WikiMetadataServiceTests.cs`, `WikiControllerVisibilityTests.cs`, `WikiAdminApiTests.cs` (integration)
- [x] Assets: `FileSystemWikiAssetServiceTests.cs`, `WikiAssetControllerTests.cs` (whitelist, SVG script rejection, cache headers)
- [x] Directives: `WikiMarkdigPipelineTests.cs` (placeholders, arg validation, injection rejection), `WikiDirectiveBlockTests.cs` (bUnit)
- [x] SEO: `SeoControllerTests.cs`, `SeoEndpointTests.cs` (integration)
- [x] @wiki commands produce correct MString output — `WikiCommandTests.cs` (10 tests: create/view/list/search/append/history/protection/tags + helpfile loads), `WikiFunctionUnitTests.cs` (10 tests), `WikiSyntaxInGameRenderingTests.cs` (13 tests)

## Remaining (out of portal scope or follow-up)
- NATS `portal.wiki.changes` event on edit
- Redlink existence detection in the wiki-link renderer
- Revision rollback action in the history dialog
- Owner-edit / royalty-edit-any permission tiers (currently: any authenticated user edits unprotected pages, Wizard edits everything)
