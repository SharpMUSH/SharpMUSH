# Browse wiki collections

The wiki home keeps the game's own pinned categories together. An installed content package has one collection entry, with the number of pages you can read. A large rules reference therefore does not add every imported category to the home sidebar.

Open a collection to browse its pages, choose a pinned topic, or expand **Browse all categories** and filter that directory. Lists show 50 pages at a time and an exact result count. Search matches localized and source titles and category labels across the entire selected collection, including pages beyond the first 500. It does not search article bodies or offer structured rules filters such as spell rank or item price. The wiki home's search covers all collections and labels results with their collection and namespace.

Search, category, result offset and requested language are part of the collection URL. Share that URL or use browser Back/Forward to recover the same results. Type a search and submit it; typing alone leaves the current results in place. Changing a search or category starts at the first page.

A category name opens its introduction when that page is available to you. **Browse members** filters the selected collection instead. On a category article, the member list shows 50 entries at a time with its own Previous/Next controls and range. Paging members keeps the original search results available through **Back to results**. A separate **Game wiki** link remains available. Direct article visits do not invent a previous search.

Existing article links still open the same pages. Opening an imported article or category selects its owning collection in the sidebar. Direct category entry defaults its members to the authorized owner; an explicit member selection keeps its independently authorized collection. The member selection and original search return remain separate. **Wiki home** returns to the game's own navigation. Pages still obey their normal read requirements and draft visibility.

Useful routes:

- `/wiki` — game home and installed collections.
- `/wiki/collections/game` — game pages and their complete category directory.
- `/wiki/collections/package%3Apf2e-remaster` — the installed PF2e reference.
- `/wiki/collections/all?q=Blinded` — search across collections.
- `/wiki/collections/package%3Apf2e-remaster?category=conditions` — a topic within that reference.
- `/wiki/collections/package%3Apf2e-remaster?q=spell&category=spells&skip=50` — the second page of matching spell results.
- `/wiki/category/spells?membersCollection=package%3Apf2e-remaster&membersSkip=50` — the second page of that reference's Spells members.

## Authoring a collection name

A package can set its public wiki collection label independently of its package ID, starting with manifest format 1.13:

```yaml
format: 1.13
package: independent-rules
version: 1.0.0
wiki_collection_name: Independent Rules
wiki:
  - page: main:guide
    title: Rules Guide
    markdown: Rules.
```

The optional `wiki_collection_name` is trimmed and must contain 1–100 characters, with no control characters or line breaks. It labels collection navigation and search-result context. The package ID, collection URL (`/wiki/collections/package%3Aindependent-rules`) and page ownership remain the same. Edition-specific wording can stay in the collection's main article.

Without this metadata, existing packages keep the package ID with hyphens replaced by spaces as their collection label. An upgrade records the new label; omitting the key on a later upgrade restores that fallback. Rollback restores the label recorded in the selected revision; revisions created before this metadata have no authored label. A manifest below format 1.13 warns and ignores the key.

## Installation and updates

Collection membership follows the installed package's ownership of each page. It does not depend on matching a title or guessing a prefix. Existing installations gain collection browsing without rewriting page IDs, history, slugs, translations or bookmarks. Reapplying a package continues to use the installer's existing merge and conflict rules for local edits.

A manually recreated page does not inherit an old page's package ownership just because its slug is the same. Package removal also follows the normal installer rules: retained, locally edited pages become game content once package ownership is removed.

This first browsing phase preserves the existing global namespace and slug identity. It does not yet allow two different pages with the same namespace and slug in separate collections. The installer continues to report those conflicts. Qualified page identities, durable aliases and an operator-reviewed migration remain tracked in [#1970](https://github.com/SharpMUSH/SharpMUSH/issues/1970); collection browsing does not silently reassign a colliding bookmark.

## Exploration still being developed

This first phase supplies collection isolation, accurate counts and complete browsing. Explicit landing and task-oriented hubs remain subsequent work; the package's pins and complete category directory do not replace that information architecture. Authored category introductions and existing specialized indexes now remain accessible as distinct destinations in those journeys.

The [PF2e organization research](../research/pf2e-wiki-organization.md) recommends measuring content-kind, relationship, facet and source/version metadata before adding filters. Their coverage in the import is not established by this browsing phase. Qualified identity, durable aliases and category-reference rewrites require a separate dry-run migration. These boundaries remain tracked in [#1970](https://github.com/SharpMUSH/SharpMUSH/issues/1970).

For reproducible full-import checks, use the pinned package snapshot and opt-in commands in the [implementation evidence](../superpowers/plans/2026-10-16-wiki-collections.md#reproduce-the-full-import-acceptance-evidence). The recorded evidence covers actual import/API exports, bUnit component callbacks and separate captured-markup/CSS browser checks; it does not claim an authenticated live application journey.

Collection and member responses are paged, but this does not establish bounded server work. Localized search and category counts still inspect candidates, and the article's related-pages panel retains a separate legacy loading path. Request-work measurements and that related-panel replacement remain tracked in [#2044](https://github.com/SharpMUSH/SharpMUSH/issues/2044). Switching the active character refreshes article and collection visibility even when the URL stays the same.

Text-only category members omit image placeholders; real images and category/draft/restricted badges remain visible. See the [running-portal navigation captures](assets/wiki-collections/README.md#full-running-portal-navigation-captures-2026-10-19).
