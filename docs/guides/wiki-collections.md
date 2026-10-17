# Browse wiki collections

The wiki home keeps the game's own pinned categories together. An installed content package has one collection entry, with the number of pages you can read. A large rules reference therefore does not add every imported category to the home sidebar.

Open a collection to browse its pages, choose a pinned topic, or expand **Browse all categories** and filter that directory. Lists show 50 pages at a time and an exact result count. Search matches localized and source titles and category labels across the entire selected collection, including pages beyond the first 500. It does not search article bodies or offer structured rules filters such as spell rank or item price. The wiki home's search covers all collections and labels results with their collection and namespace.

Existing article links still open the same pages. Opening an imported article or category selects its owning collection in the sidebar; a category's members stay within that collection. **Wiki home** returns to the game's own navigation. Pages still obey their normal read requirements and draft visibility.

Useful routes:

- `/wiki` — game home and installed collections.
- `/wiki/collections/game` — game pages and their complete category directory.
- `/wiki/collections/package%3Apf2e-remaster` — the installed PF2e reference.
- `/wiki/collections/all?q=Blinded` — search across collections.
- `/wiki/collections/package%3Apf2e-remaster?category=conditions` — a topic within that reference.

## Installation and updates

Collection membership follows the installed package's ownership of each page. It does not depend on matching a title or guessing a prefix. Existing installations gain collection browsing without rewriting page IDs, history, slugs, translations or bookmarks. Reapplying a package continues to use the installer's existing merge and conflict rules for local edits.

A manually recreated page does not inherit an old page's package ownership just because its slug is the same. Package removal also follows the normal installer rules: retained, locally edited pages become game content once package ownership is removed.

This first browsing phase preserves the existing global namespace and slug identity. It does not yet allow two different pages with the same namespace and slug in separate collections. The installer continues to report those conflicts. Qualified page identities, durable aliases and an operator-reviewed migration remain tracked in [#1970](https://github.com/SharpMUSH/SharpMUSH/issues/1970); collection browsing does not silently reassign a colliding bookmark.

## Exploration still being developed

This first phase supplies collection isolation, accurate counts and complete browsing. A readable authored collection label, explicit landing and task-oriented hubs remain subsequent work; the package's pins and complete category directory do not replace that information architecture. Category introductions and existing specialized indexes should remain part of those journeys.

The [PF2e organization research](../research/pf2e-wiki-organization.md) recommends measuring content-kind, relationship, facet and source/version metadata before adding filters. Their coverage in the import is not established by this browsing phase. Qualified identity, durable aliases and category-reference rewrites require a separate dry-run migration. These boundaries remain tracked in [#1970](https://github.com/SharpMUSH/SharpMUSH/issues/1970).

For reproducible full-import checks, use the pinned package snapshot and opt-in commands in the [implementation evidence](../superpowers/plans/2026-10-16-wiki-collections.md#reproduce-the-full-import-acceptance-evidence). The recorded evidence covers actual import/API exports, bUnit component callbacks and separate captured-markup/CSS browser checks; it does not claim an authenticated live application journey.
