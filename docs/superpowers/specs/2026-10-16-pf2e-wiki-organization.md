# PF2e wiki organization proposal

Status: collection browsing foundations are implemented and locally verified; [#1970](https://github.com/SharpMUSH/SharpMUSH/issues/1970) remains open. The foundation preserves existing canonical identity and supplies authorized complete counts, scoped category membership and paginated title/category search. Authored collection landings, readable collection metadata, content-specific discovery and canonical migration are not implemented by that foundation.

The subsequent [research report](../../research/pf2e-wiki-organization.md) and its cited primary sources supersede any suggestion below that moving a flat category directory behind one collection entry completes the exploration problem. The report separates observed reference patterns from SharpMUSH recommendations and records the limits of the research.

## Observed structure

The [owned PF2e package](https://github.com/SharpMUSH/SharpMUSH.Packages/tree/2659a733a4519b76e64a727be2687794afc04f6b/pf2e-remaster) contains 4,711 Markdown wiki pages, including 130 category pages (repository tree inventory, 2026-10-16; the tree was not truncated). Manifest version 1.1.1 files rules articles under Main, categories under Category, and its ORC notice under Help. Four categories are pinned: Character Creation, Rules, Conditions, Difficulty Classes. Names such as `main:blinded`, `category:ancestries` and `category:alchemist_feats` share the game's global identity space.

The category pages already contain useful authored navigation: Ancestries links each ancestry and related heritage/feat indexes; Alchemist Feats groups feats by level. Preserve those relationships rather than generating another flat index.

At the original baseline, `SharpMUSH.Client/Components/Wiki/WikiSidebar.razor` fetched the first 500 page summaries and rendered their category groups globally. That made counts and visible categories depend on a partial sample. The implemented foundation removes this sampling and isolates package-owned page IDs for browsing; the full 4,711-page import has been exercised. This proves scale and selection mechanics, not that the imported reference now has an adequate task-oriented layout.

## Recommended experience

Give the pack one **Pathfinder 2e** collection entry at the wiki home. The default global sidebar contains administrator-selected game categories and collection entries; the full category directory remains searchable from Browse all categories. Imported subcategories do not each become global navigation entries merely because a page names them.

The next authored landing should answer recognizable tasks: Build a character, Play and look up rules, Spells, Equipment, and Run a game, with sources and an exhaustive directory as supporting routes. These are research-backed candidate groups, not a hard-coded PF2e taxonomy or a requirement to rename existing categories. Validate their actual destinations against the installed package and preserve its authored ancestry/heritage and class/feat indexes, including category introductions. A package label, the existing four pins and a flat searchable directory are useful mechanisms but do not supply this landing.

The implemented foundation supplies bounded server-side pagination and counts from complete authorized data. Its search matches localized/source titles and category labels; it does not search article bodies or provide structured gameplay facets. Within a collection, show its context and a visible return route to the game's wiki. Existing canonical article links remain authoritative until a separate migration contract is implemented.

Representative journeys:

| Task | Current obstacle | Proposed route |
| --- | --- | --- |
| Find a dwarf heritage | Many peer categories, no clear entry point | Wiki → Pathfinder 2e → Character creation → Ancestries → Dwarf → Heritages |
| Find an Alchemist feat by level | Requires knowing the category name among many | Pathfinder 2e → Classes and feats → Alchemist → Feats by level |
| Look up Blinded | Generic page title may collide with game content | Scoped PF2e search → Blinded, labeled Pathfinder 2e / Conditions |
| Browse the game's own setting | Rules categories dominate global navigation | Wiki home → selected setting categories; PF2e stays one collection entry |

## Identity and namespacing

Add a collection identity independent of the existing namespace enum. A namespace describes page purpose (Main, Category, Help, etc.); a collection describes ownership and browsing context. Recommended stable identity: `pf2e-remaster`, with an explicitly authored, localized human label such as Pathfinder 2e Remaster. Do not introduce one global enum member per rules system, or encode all collection behavior in display-title prefixes.

Keep article display titles short within the collection. Search and cross-collection links show the collection label as context. Use collection-qualified canonical routes and page keys only after the alias/migration contract exists. The exact route syntax remains a design decision for implementation review; examples here describe navigation, not already-supported URLs.

Package identity is the durable source of imported collection membership. Authorized assignment of manually authored pages to collections is a future contract, not a capability supplied by the ownership-based browsing foundation. Category identity must also be scoped; a PF2e Conditions category and a game's Conditions category must coexist without accidental membership mixing. Collection membership does not grant read/edit permissions.

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

## Current foundation and next bounded milestones

The foundation implements package-ownership selection, complete authorized navigation/counts, scoped category member browsing, paginated collection routes and localized/source title/category-label search. Mechanical storage/API tests and full-scale import evidence support those claims. It retains existing page IDs, global namespace/slug identity and canonical links. It does not introduce new independent canonical keys, a landing manifest or a content facet schema. See the [implementation plan](../plans/2026-10-16-wiki-collections.md) and [operator guide](../../guides/wiki-collections.md) for the narrower runtime support.

### Next: explicit metadata, owned landing and authored task hubs

Add a generic collection presentation contract: a readable localized label, a short snapshot description, and an explicit landing-page reference. Define navigation groups and destinations as authored data, with ownership and read-visibility validation. Do not guess a landing from a title, choose the first page, hard-code PF2e labels in the portal, or manufacture a hierarchy from category spelling. A missing or inaccessible landing must have an honest browsing fallback.

Preserve category introduction text and specialized indexes alongside generated scoped members. Verify the representative ancestry/heritage, class/feat-level, condition lookup and return-to-game journeys on the complete import. The sidebar and landing should expose understandable context without flattening every category into a peer or adding an unnecessary intermediate page for known-item search. [Research rationale and accessibility acceptance](../../research/pf2e-wiki-organization.md#make-the-landing-answer-player-and-gm-questions).

### Then: measure structured metadata before offering facets

Inventory coverage and provenance for content kind, class/ancestry relationships, feat level, spell tradition/rank, item category/level/price, source book/page and version relationships. The current coverage of these fields is an open question. Publish coverage and ambiguity counts before choosing filters. Prefer maintained structured import fields over per-request prose inference, and do not expose a filter that silently excludes valid unclassified records. Body search is another explicit search capability, not something the current title/category-label search implies. [Reference evidence and boundaries](../../research/pf2e-wiki-organization.md#let-search-refine-the-reference-without-hiding-its-structure).

### Later: qualified canonical identity, aliases and category-reference migration

Specify stable collection-qualified page/category identity and durable aliases before changing keys or routes. Produce an operator-reviewable dry run covering collisions, inbound article links, category membership references, pins, translations, requirements, package baselines and local edits. An article redirect alone does not rewrite category membership. Preserve page IDs/history and unambiguous bookmarks; ambiguous old names require explicit resolution. Validate clean install, repeated import and upgrade from the existing package alongside unrelated game pages. [Migration rationale](../../research/pf2e-wiki-organization.md#preserve-identities-permissions-and-provenance-through-updates).

### Decision boundaries

- Collection owns reference context; namespace describes page purpose; category relationships and content facets serve separate navigation jobs.
- Package ownership identifies current membership but does not establish independent canonical names or a permission grant.
- Source/version metadata is distinct from gameplay topics; measure retained alias/version relationships before promising legacy-name discovery.
- Research-informed task groups remain proposals until destinations and user journeys are validated. No fixed number of pins or category rows is evidence of usable exploration.
- Publish this foundation as a scoped improvement. Keep #1970 open until authored exploration, identity migration and relevant acceptance evidence are addressed; do not label it a completed PF2e redesign.

## Acceptance evidence before implementation is called complete

- Run the four journeys above with exact route/screenshot and keyboard-only evidence on a representative full import.
- The global sidebar remains bounded by explicit navigation choices, not page-count caps, while all authorized categories remain discoverable.
- Counts/pagination/search include pages beyond 500 and never expose protected pages or category counts derived from inaccessible content.
- Duplicate display names in different collections resolve predictably; current bookmark/link aliases work after upgrade.
- Repeated imports preserve collection membership, local edits, category relationships, translated display names and attribution.
- Record user feedback on terminology and navigation before promoting the package to normal installations.
