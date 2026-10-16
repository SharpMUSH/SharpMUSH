# PF2e wiki organization proposal

Status: review proposal for [#1970](https://github.com/SharpMUSH/SharpMUSH/issues/1970). Runtime and content migration are subsequent implementation work; this document does not mark the usability issue resolved.

## Observed structure

The [owned PF2e package](https://github.com/SharpMUSH/SharpMUSH.Packages/tree/2659a733a4519b76e64a727be2687794afc04f6b/pf2e-remaster) contains 4,711 Markdown wiki pages, including 130 category pages (repository tree inventory, 2026-10-16; the tree was not truncated). Manifest version 1.1.1 files rules articles under Main, categories under Category, and its ORC notice under Help. Four categories are pinned: Character Creation, Rules, Conditions, Difficulty Classes. Names such as `main:blinded`, `category:ancestries` and `category:alchemist_feats` share the game's global identity space.

The category pages already contain useful authored navigation: Ancestries links each ancestry and related heritage/feat indexes; Alchemist Feats groups feats by level. Preserve those relationships rather than generating another flat index.

`SharpMUSH.Client/Components/Wiki/WikiSidebar.razor` fetches the first 500 page summaries and groups their categories. It renders every resulting named group in the global sidebar. Thus the visible categories and counts depend on a partial page sample, rather than a complete navigation model. A 4,711-page pack exceeds this cap substantially. Raising the cap would increase clutter and transfer cost without making the information architecture coherent.

## Recommended experience

Give the pack one **Pathfinder 2e** collection entry at the wiki home. The default global sidebar contains administrator-selected game categories and collection entries; the full category directory remains searchable from Browse all categories. Imported subcategories do not each become global navigation entries merely because a page names them.

The PF2e landing page exposes seven curated starting points: Character creation, Classes and feats, Spells, Actions and skills, Equipment, Rules, Conditions and DCs. Within the collection, show its own topic navigation and a visible route back to the game's wiki. Preserve links from class and ancestry articles into their specialized feat/heritage indexes. Long result sets need server-side pagination and topic/search filters; counts must come from authorized complete data rather than the first 500 pages.

Representative journeys:

| Task | Current obstacle | Proposed route |
| --- | --- | --- |
| Find a dwarf heritage | Many peer categories, no clear entry point | Wiki → Pathfinder 2e → Character creation → Ancestries → Dwarf → Heritages |
| Find an Alchemist feat by level | Requires knowing the category name among many | Pathfinder 2e → Classes and feats → Alchemist → Feats by level |
| Look up Blinded | Generic page title may collide with game content | Scoped PF2e search → Blinded, labeled Pathfinder 2e / Conditions |
| Browse the game's own setting | Rules categories dominate global navigation | Wiki home → selected setting categories; PF2e stays one collection entry |

## Identity and namespacing

Add a collection identity independent of the existing namespace enum. A namespace describes page purpose (Main, Category, Help, etc.); a collection describes ownership and browsing context. Recommended identity: `pf2e-remaster`, with human label Pathfinder 2e. Do not introduce one global enum member per rules system, or encode all collection behavior in display-title prefixes.

Keep article display titles short within the collection. Search and cross-collection links show the collection label as context. Use collection-qualified canonical routes and page keys only after the alias/migration contract exists. The exact route syntax remains a design decision for implementation review; examples here describe navigation, not already-supported URLs.

Package identity is the durable source of imported collection membership. Manually authored pages can opt into collections with authorization. Category identity must also be scoped; a PF2e Conditions category and a game's Conditions category must coexist without accidental membership mixing. Collection membership does not grant read/edit permissions.

## Alternatives considered

| Approach | Benefit | Cost / reason not selected |
| --- | --- | --- |
| Prefix all titles and categories with PF2e | Small content-only change | Still 130 peer sidebar categories; noisy titles, link churn and future-update conflicts |
| New `pf2e` namespace enum case | Obvious partition | Mixes topic ownership with Main/Category/Help semantics and requires a code change for every future pack |
| Hide most categories with fixed UI caps | Quick visual relief | Arbitrary omissions and inaccessible navigation; misleading counts remain |
| Collections plus curated entry points | Scales to other large packs and preserves page purpose | Requires coordinated API/store/package/UI work and explicit migration |

## Migration and repeat imports

1. Inventory installed package-owned pages, category membership, inbound links, localized titles and manual edits. Produce a dry-run mapping keyed by existing page identity and package ownership, with conflicts reported before writes.
2. Add collection metadata to the data trunk (Mediator requests, stores, cache invalidation) and package manifest schema. Existing pages default to the unscoped game collection. Backfill PF2e membership using installed package ownership, never a title heuristic.
3. Ship collection-aware listing/search/navigation first while existing canonical URLs still work. This gives immediate exploration improvements without destructive key changes.
4. Introduce qualified identities/routes and durable aliases in a separate migration. Preserve existing bookmarks and wiki links. Ambiguous old keys require explicit operator resolution, never reassignment to an arbitrary page. Retain page IDs/history/protection and local edits.
5. Update package manifests/internal links and the ORC notice references together. Re-imports match stable package page keys and collection identity, preserve accepted local changes, and do not recreate old global categories or pins. A clean install and upgrade from 1.1.1 must converge on the same navigation.

No migration deletes categories merely because they originated in the pack: game-authored pages may reference them. Report mixed ownership and require an explicit reassignment plan. Keep licensing/attribution and permissions intact.

## Bounded implementation sequence

1. Complete the representative inventory and usability review with an operator; settle collection route/key and alias contracts.
2. Add collection metadata, authorized full-data count/list/search endpoints, package schema support and explicit backfill migration. Prove cache coherence, permission filtering, upgrade idempotence and collision behavior.
3. Replace sampled global category navigation with curated categories/collections and a paginated searchable category directory. Add collection landing/topic navigation and scoped search with clear context.
4. Publish the coordinated PF2e package update and aliases/migration tooling. Validate clean install, repeated import and upgrade against all 4,711 pages, plus unrelated game pages.

## Acceptance evidence before implementation is called complete

- Run the four journeys above with exact route/screenshot and keyboard-only evidence on a representative full import.
- The global sidebar remains bounded by explicit navigation choices, not page-count caps, while all authorized categories remain discoverable.
- Counts/pagination/search include pages beyond 500 and never expose protected pages or category counts derived from inaccessible content.
- Duplicate display names in different collections resolve predictably; current bookmark/link aliases work after upgrade.
- Repeated imports preserve collection membership, local edits, category relationships, translated display names and attribution.
- Record user feedback on terminology and navigation before promoting the package to normal installations.
