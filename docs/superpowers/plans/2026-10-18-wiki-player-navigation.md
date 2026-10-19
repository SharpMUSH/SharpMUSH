# Recoverable wiki browsing and paged category navigation implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement task-by-task. No implementation, build, Git mutation, network work or delegation was performed while preparing this read-only plan.

**Goal:** Preserve a reader's collection/search/category/locale/page journey through URLs, Back and return links; expose authored category introductions; remove client loops that fetch every category member or sibling.

**Architecture:** Keep canonical article identity and existing package-owned collections. Put browse state in explicit local query fields, render authored category pages through the existing article route, and use the existing collection paging API for member lists. Use a narrow related-pages endpoint to retain the current namespace/newest sibling behavior without downloading the category to the client. Server work budgets require separate explicit treatment below.

**Tech Stack:** Blazor WASM, NavigationManager, existing WikiService/API result unions, ASP.NET controllers, Mediator/store architecture, Lightning list indexes, TUnit/bUnit and actual browser acceptance.

**Spec:** `docs/superpowers/specs/2026-10-16-semantic-wiki-architecture-design.md` section14; `docs/reports/2026-10-18-pf2e-player-readiness.md`. Package PR5 merged as 99e0bfe; engine table PR2043 merged as bd345778. Navigation issue #2044 remains open until its actual full-app and request-work gates pass.

## Constraints and review focus

- No semantic activation, query language, new metadata vocabulary, collection-qualified article identities, prerequisite inference or content rewriting.
- Package ownership remains stable page ID based. The existing `package:<id>`, `game`, `all` collection IDs remain browsing scope, never page identity or reader authority.
- Preserve explicit requested locale even when the served article falls back to another language. Never serialize tokens, permission decisions or trusted result snapshots into URLs.
- Missing/hidden introductions and revoked access must not leak titles or bypass normal wiki authorization. Existing page/translation/draft checks remain authoritative.
- Handle stale async responses, blank/out-of-range offsets, changing totals, encoded search/category values, forged contexts and direct article visits. Test each explicitly.
- All temporary build/browser data must use `/temp/codex/<task>/` when writable, otherwise workspace `work/`; set TMPDIR/TMP/TEMP accordingly. Keep PR2043's table change out of this navigation diff until coordinated with root.

## Actual source baseline

- `SharpMUSH.Client/Pages/WikiCollection.razor` accepts `lang`, `q`, `category`, but no offset. `WikiCollectionBrowser.razor` uses PageSize50 and resets `_skip=0` on every parameter change; paging/search/category mutate only private state. Article links retain only locale. The existing version/disposal guard is worth keeping.
- `WikiSidebar.razor` loads collection navigation from collection route or page ownership, but Search sends `/wiki?q=...`, losing collection/category. Its category links filter members rather than opening the authored body.
- `/wiki/{Ns}/{Slug}` is canonical article identity (`WikiPage.razor`, `WikiRoutes`). Category articles already render their authored HTML before `WikiCategoryMembers`. Missing category articles still show members. The latter drains every200-row batch before rendering and sorts the entire accumulated result.
- `WikiDisplay.LoadMoreAsync` calls the client `GetAllByCategoryAsync`, downloads every200-row batch, filters same namespace, excludes current slug, sorts UpdatedAt descending, then takes3. Its cache key is category alone, omitting article, collection and locale. `WikiMoreCard` links drop locale and context.
- `GET api/wiki/collections/{collection}/pages?skip&take&category&search&lang` returns summaries plus required `X-Total-Count`. Client `GetCollectionPagesAsync` returns `ApiResult<WikiPageBatch>` and fails honestly on a missing/malformed total. It already enforces collection visibility before search and store-side selection before pagination. Category order is Title ordinal, not newest.
- `WikiNavigationDto` already has collection/category summaries; `WikiCategorySummaryDto.HasPage` is scoped to authorized category pages. No authored landing metadata exists. Use the known category identity for introductions; do not guess a package Learn PF2e landing from a title.
- `WikiCollectionsController.SearchAsync` scans the selected collection in128-row batches to match localized/source titles and localized category labels. `Navigation` reads all category pages with int.MaxValue and calls category counts. Lightning `CountPagesByCategoryAsync` scans the category index. Bounded response pages therefore do not imply bounded whole-request work.

## Concrete decisions

1. Collection URLs: `/wiki/collections/{id}?q=<term>&category=<key>&skip=<offset>&lang=<requested>`. Omit defaults; keep page size fixed50. Clamp negative/invalid offsets to0, align to50, canonicalize with replace; positive out-of-range offsets use one correction request and replace to the final valid page, as the current component already does locally. Search/category changes reset only offset; locale changes retain the requested page until server totals require correction.
2. Article return context: flat fields `browseCollection`, `browseQ`, `browseCategory`, `browseSkip`, with existing `lang`. No free-form return URL. Reconstruct only the fixed local collection route; validate lengths/offsets/known collection via authorized navigation. The context is a return destination, not proof that the article belongs to it. Direct articles use owner collection from `navigation?page=ns/slug` and a separate Game wiki link; never invent a search/category breadcrumb.
3. Category UI presents distinct actions: the category name opens its authored introduction when HasPage is true; a separate labelled browse-members link selects its filter. Categories lacking an authorized body retain a usable members action. Category article members carry the return context and selected collection explicitly; absent context uses owner navigation. Do not change globally rendered canonical wiki identity.
4. Category members show one mixed, server-ordered50-row page with visible Previous/Next and range/total. They may retain subcategory/page visual groups within that page, but labels describe the current page rather than pretending to give whole-category subgroup totals. Stable Title order replaces client-only whole-category recency sorting. The page offset is `membersSkip` on the category article, distinct from `browseSkip` return offset.
5. Preserve related-card same-namespace/newest semantics with a narrow endpoint (Task4), rather than changing what the existing card recommends. Replace the expensive exact “All N” total with “Browse category”; this avoids requiring a full total scan solely for a three-item card. Empty/failed/incomplete recommendations never mean there are no pages in the category.

### Task 1: URL state and recoverable collection results

**Files:** create `SharpMUSH.Client/Models/Wiki/WikiBrowseState.cs` and `WikiJourneyLinks.cs`; modify `Pages/WikiCollection.razor`, `Components/Wiki/WikiCollectionBrowser.razor`, `Components/Wiki/WikiSidebar.razor`, `Components/Widgets/WikiIndexWidget.razor`; tests in `SharpMUSH.Tests.BUnit/Components/Wiki/WikiCollectionBrowserTests.cs`, `WikiCollectionLocaleTests.cs`, `WikiSidebarTests.cs`, and a new `WikiBrowseStateTests.cs` in the same test project.

**Interfaces:** `WikiBrowseState(string Collection, string? Search, string? Category, int Skip, string? Locale)` plus parsing/normalization and URL builders owned in the client model. `WikiCollectionBrowser` adds `Skip` parameter; the route binds it from query. `WikiJourneyLinks.Collection(state)` and `.Article(path,state)` build escaped local paths with no duplicate query keys and preserve fragments. No backend API shape change.

- [ ] Add failing tests: deep link with package scope/q/category/skip500/langfr makes the first rows request skip500/take50 with all filters; Next writes skip550; search/category reset offset; locale retains it; reload renders the same range.
- [ ] Add helper tests: unicode, `&`, `+`, quotes, fragments; duplicate query fields handled deterministically; negative/noninteger/oversized offset normalization; over200-character search fails/normalizes without an oversized request; forged absolute return URLs are never accepted because no such field exists.
- [ ] Make submitted state route-driven. Search typing remains a draft until submit; pager and category actions navigate using full normalized state. User actions push history; canonicalization only replaces. Never erase a newer route when an older request resolves.
- [ ] Cache navigation by collection/locale separately from result offset to avoid rereading the entire directory on every Next. Keep load-version/disposal guards, and use result identity including full state.
- [ ] Route sidebar search to current authorized collection, retain selected category and locale, reset skip. Global search remains an explicitly labelled all-collections action. Route home-widget search to the all-collection browser; retain `/wiki?q=` compatibility by interpreting legacy home search rather than dropping it.
- [ ] Run affected bUnit classes red/green, including existing `CollectionResultsPagePast500AndSearchTheWholeCollection`, locale-only reload, malformed API JSON and stale-result tests. Existing link assertions must now verify the complete structured query rather than only `?lang=`.

### Task 2: Article return links and journey propagation

**Files:** modify `Pages/WikiPage.razor`, `Components/WikiView.razor`, `Components/WikiDisplay.razor`, `Components/Wiki/WikiMoreCard.razor`, `Models/Wiki/WikiLocaleLinks.cs` as needed; focused new bUnit `WikiJourneyNavigationTests.cs`, existing `WikiRoutePageTests.cs`, `WikiDisplayHomeChromeTests.cs`, `WikiDisplayFallbackTests.cs`, locale tests and shared-resource entries.

**Interfaces:** thread an optional `WikiBrowseState` read from the article's browse-prefixed query fields through WikiView/Display and link-producing children. Keep existing `lang` as requested locale, separate from Article.Locale fallback. Category-member offset is passed separately. Build “Back to results” only from validated browse context; direct fallback uses an authorized collection browse link.

- [ ] Add failing tests: result→article→guide→Back to results reconstructs identical collection/q/category/skip/lang; direct article lacks a fabricated result path; unknown/hidden collection produces safe fallback without its private labels; game wiki remains reachable.
- [ ] Apply the journey link builder to client-created category/related/locale links and recognized canonical wiki article anchors in displayed HTML. Reuse the existing WikiDisplay link-target processing, preserving redlink classes and labels; transform only recognized local wiki article URLs, HTML-encode rewritten hrefs, and never mutate stored RenderedHtml/Markdown or external/character/download links. Preserve fragments and existing unrelated supported query values.
- [ ] Ensure query-only navigation changes refresh chrome/context without requiring an article identity change. Existing WikiPage key includes only ns/slug/lang; do not leave return state stale or refetch unrelated bodies purely for offset changes.
- [ ] Test locale switch, fallback locale, TOC anchors, redlink marking, a context-free direct URL, two tabs with different result states, and browser history replay. No cross-tab mutable singleton journey state.
- [ ] Run the route/display/locale/journey bUnit classes red/green. Keep canonical character redirects unaffected.

### Task 3: Authored category introductions and paged members

**Files:** modify `Components/Wiki/WikiCollectionBrowser.razor`, `WikiSidebar.razor`, `WikiCategoryMembers.razor`, `Components/WikiDisplay.razor`, `Pages/WikiPage.razor`; tests in new `WikiCategoryMembersPagingTests.cs`, existing `WikiCollectionLocaleTests.cs`, `WikiSidebarTests.cs`, `WikiApiFake.cs` and relevant resources.

**Interfaces:** member component receives optional validated collection scope, `MembersSkip`, locale and return state; consumes existing `GetCollectionPagesAsync(collection, skip,50,category:key,lang:requested)`. No recursive WikiDisplay inside the browser; opening the category uses the existing category article route and normal authorization.

- [ ] Failing tests: category-name link opens an authorized authored body above the member list; filter action retains independent meaning; HasPagefalse has no fabricated body link; a hidden introduction is not rendered merely because visible members exist.
- [ ] Replace the200-row accumulation loop with one50-row request per requested page. Render range and Previous/Next with membersSkip query state. Group only returned rows; do not fetch remaining rows to compute subgroup counts or reorder globally.
- [ ] Prove611-member category initially requests only skip0/take50, Next requests skip50 once, deep-link membersSkip500 renders that page, and empty/deleted last-page cases make at most one corrective call. Return offset remains unchanged by members paging.
- [ ] Test same-key categories in game/package scope, requested locale on navigation and list calls, failure/retry, stale category/scope change, and absent category page. Never infer collection ownership from translated title.
- [ ] Run category/browser/sidebar/locale bUnit suites and existing controller authorization tests red/green. Exact whole-category counts come only from server totals; never compute them from the current batch.

### Task 4: Bounded related-card transfer, preserving namespace/newest behavior

**Files:** add a related action in `SharpMUSH.Server/Controllers/Wiki/WikiCollectionsController.cs` or focused sibling controller using its ownership/visibility helpers; client `Services/WikiService.cs`, `Components/WikiDisplay.razor`, `Components/Wiki/WikiMoreCard.razor`; ordinary service/store seam in `Library/Services/Interfaces/IWikiService.cs`, `Library/Services/WikiStoreService.cs`, `Library/Stores/IWikiStore.cs`, Lightning implementation and test store; API DTO in `Contracts/API/WikiCollectionModels.cs`; tests in `WikiCollectionsControllerTests.cs`, `WikiCollectionSelectionTests.cs`, new bUnit `WikiRelatedPagesTests.cs`.

**Proposed API:** `GET api/wiki/collections/{collection}/related/{ns}/{slug}?lang=...`, fixed maximum3 summaries, no total. Resolve current page under ordinary read visibility, require its ID belongs to selected scope, derive its first category server-side, filter same namespace and exclude its stable ID before selecting the newest3. Reject unrelated supplied collection scope; the UI uses actual article owner scope, not arbitrary browse-return scope. Return a category key only when authorized; no hidden source-page facts.

**Proposed store seam:** `GetRecentPagesByCategoryAsync(string category, string ns, string excludingPageId, int take, WikiVisibility visibility)` behind the ordinary game-data request/store boundary. Stream the existing recent index in descending order and stop after take admitted matching rows; do not materialize or sort the full category. This preserves newest selection, with an explicit stable tie-breaker. This bounds returned/materialized rows, not worst-case candidate scans; see prerequisites below.

- [ ] Add controller/store tests with >500 rows, newer wrong-namespace rows, current article among the first rows, older eligible rows, hidden/draft/other-collection candidates and equal timestamps. Expected newest3 eligible, no current page, no permission/count leak, no response bodies.
- [ ] Implement one client related request per article/ns/collection/requested-locale identity. Replace category-only cache key, discard late results after article changes, and clear failures on retry. Embedded biographies continue to skip the card.
- [ ] Remove the global client GetAllByCategoryAsync call from LoadMoreAsync. Card “Browse category” uses collection-scoped category URL and keeps locale/return state. No fake siblings.Count+1 total.
- [ ] bUnit asserts exactly one related request, never any legacy category-drain endpoint, at most3 shown, full navigation context, proper unavailable UI and article-switch races. Run store/controller/client tests red/green.

**Smaller alternative, not the default:** one existing collection/category take4 call, remove current ID and display the first3 in title order across all namespaces; label explicitly as a category sample and use returned total for that same scope. This needs no service/store extension but changes current namespace/newest behavior. Use only if root deliberately accepts that product choice; do not accidentally preserve client filtering after take4, which can underfill the sample or disagree with its total.

## Request-work prerequisites and follow-up gate

Tasks1–4 remove client drains and make navigation recoverable. They cannot establish a bounded end-to-end request cost. Before calling this gate performance-ready:

- Instrument or count native rows/translation lookups for collection navigation, localized search, category counts and related candidates in a representative4,720-page native world. Record response bytes/round trips as well as candidate work. A three-row HTTP response does not prove three database reads.
- `SearchAsync` must retain localized-title/category matching. Replacing it with `WikiVisibility.Search` silently loses translation semantics. Options: a dedicated ordinary search request/store projection with locale-aware title/category matching applied before paging; or explicit bounded candidate budget with an incomplete/refine-required result contract. The latter cannot report an exact total/empty result after exhausting its budget. A raw cursor over the existing full scan is not an indexed search solution.
- Category counts currently scan every selected category membership; Navigation also retrieves every category page. Options: separate bounded directory pagination and a single-category count query (narrow category-prefix scan), or durable per-collection/category aggregate indexes with correct permission handling. Never cache admin-visible counts for anonymous users. Existing `WikiNavigationDto` promises a complete directory, so making it paginated requires a distinct DTO/API rather than silent truncation.
- Related streaming over the recent index can inspect every row when the category has few authorized candidates. A category+namespace+recent-order index narrows this; it requires an explicit durable index migration and mutations/rename/ownership/restriction tests. Even that index needs a candidate budget or stronger permission-aware selection evidence for adversarial hidden-row cases. Keep this separate from metadata vocabulary and semantic activation.
- Durable indexes/projections must follow `docs/design/engine-data-trunk.md`: use cache-policy-bearing requests and aggregate store contracts, no controller/provider bypass or transient startup-only reconstruction. If these API/index changes exceed the navigation PR, record a separate prerequisite PR and leave request-budget acceptance explicitly open.

### Task 5: Actual application acceptance and final review

- [ ] Use an isolated clean worktree coordinated by root, after package PR5 content and PR2043 table behavior are available in the test world. No release or production import is needed. Root owns current PR/CI handling.
- [ ] Run focused TUnit and bUnit suites above, formatting twice to convergence, dependent builds, and the client's node tests if wwwroot JavaScript changes. Use `dotnet run --project SharpMUSH.Tests -- --treenode-filter '/*/*/WikiCollectionsControllerTests/*'` and equivalent class filters; bUnit uses `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter '/*/*/WikiCollection*/*'` plus the new category/journey/related classes. Record actual commands/results; do not claim execution from this plan.
- [ ] In the real app at1440/320: open PF2e collection, filter Fighter feats, search, advance page, open authored introduction, follow Learn PF2e→creation→example→encounter→recovery→advancement, use explicit Back to results, browser Back/Forward/reload and locale switch. Assert URLs, result range and inputs restore; category body is actually visible above a single page of members. Verify keyboard operation, focus after paging, table scroll and unaltered link labels/action costs.
- [ ] Repeat anonymous/authorized/revoked access and direct article entry; verify hidden categories/pages, draft translations and cross-collection ownership are not disclosed. Capture request counts showing no client fetch-all loops, and report the server work-budget evidence separately.
- [ ] Review final diff and fresh CI; keep semantic#2031, request-budget prerequisites and full concurrent game-load acceptance open until their own measured gates pass. Do not call this complete PF2e runtime acceptance based solely on package source or standalone article screenshots.

## Unavoidable decisions and risks

- Root must accept the structured URL field names and mixed member-page presentation; the old “all subcategories then every newest page” ordering cannot coexist with ordinary existing title-ordered pagination without a new API.
- Related ordering preservation adds a small ordinary store/API seam; category-sample alternative is simpler but changes behavior. Exact total counts are deliberately omitted from the three-item card.
- Category identities are globally keyed today. Package/global ownership collision handling is governed by existing stable IDs and authorization, not a new namespace scheme. Never invent independent collection canonical identities in this change.
- Browse context is an untrusted return hint. A reader may carry a search origin through articles outside that result set; the UI must distinguish the return destination from article ownership.
- Existing parser-generated article links have no journey query fields; client display rewriting must retain HTML safety/redlink behavior and default requested locale. Tests must cover query-only rerenders and fragments, not just direct result links.
- Pagination is not a snapshot: edits/permissions can move rows between pages. Honest correction and current totals are sufficient for this gate; snapshot continuations or versioned semantic execution belong elsewhere.

## Execution decisions for this continuation

- Root reuses `/home/grave/.codex/worktrees/package-installed-marker/SharpMUSH`, branch `codex/wiki-player-navigation`, exact base `bd34577803be33fac8933d4c1ad1fd4f239c7c22`. The failed fetch left this verified commit available locally; its tree was independently checked against actual remote main. No unrelated app capture refs were changed.
- Execute Tasks1–3 sequentially as one reviewable navigation increment. Task4's backend-selection/index contract requires measured native work before implementation; do not introduce a new potentially unbounded related scan to claim a performance fix. The first PR references #2044 without closing it while any backend/full-app acceptance remains open.
- Root chooses the documented structured URL fields, fixed50 page size and mixed title-ordered member pages. Query fields are untrusted navigation hints, never authority or inferred membership.
- Root owns Git, integration, .NET/native/browser commands, CI and merges. One source implementer per lane, source frozen before root builds. Independent Wizard work is in another repository; the user's standing parallel-work instruction authorizes simultaneous disjoint lanes. No simultaneous source writers within either lane.

## Preflight corrections accepted by root

1. Introduce an independent `membersCollection` query hint. Browser/sidebar links seed it from their explicit authorized browsing selection and carry it through the journey; `browseCollection` remains return origin only. A category page applies the requested member scope through normal API authorization and labels it. Direct visits without it use the existing authorized owner-resolution fallback; absent bodies fall back honestly to game, never a guessed package. `membersSkip` is independent from `browseSkip`.
2. Do not add an indefinite navigation DTO cache. No authorization generation exists on the current directory contract. Revalidate navigation on changed browse state, clear it on failed/revoked reads, and keep existing auth/route refresh behavior. Caching/bounding directory work requires the later reviewed server contract; this increment cannot claim that performance gate.
3. Bind raw query inputs without integer conversion before normalization; repeated recognized fields use their last supplied value. Invalid/negative/overflowing offsets normalize to0; valid offsets align down to50 and must permit checked Next arithmetic. Canonical replacement reuses the just-loaded state and makes no duplicate navigation/result fetch. Search terms are trimmed and limited to200 characters with the canonical URL reflecting the applied value.
4. Canonical redlink resolution precedes journey-link presentation rewriting. Preserve existence keys independent of href queries/fragments; decode HTML attributes once, validate local canonical article URLs, then HTML-encode replacements. Browse-only changes must not cause redundant existence RPCs.
5. First increment acceptance proves removal of the member-list drain only. More remains a known separate drain while Task4 is held; do not assert no whole-page fetch-all loops. Authored collection landing identity/edition/localization metadata is a separate unresolved prerequisite; no Learn PF2e title guess is allowed.
