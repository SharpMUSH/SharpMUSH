# Browse wiki collections

The wiki home keeps the game's own pinned categories together. An installed content package has one collection entry, with the number of pages you can read. A large rules reference therefore does not add every imported category to the home sidebar.

Open a collection to browse its pages, choose a pinned topic, or expand **Browse all categories** and filter that directory. Lists show 50 pages at a time and an exact result count. Search matches localized and source titles and category labels across the entire selected collection, including pages beyond the first 500. It does not search article bodies or offer structured rules filters such as spell rank or item price. The wiki home's search covers all collections and labels results with their collection and namespace.

Search, category, result offset and requested language are part of the collection URL. Share that URL or use browser Back/Forward to recover the same results. Type a search and submit it; typing alone leaves the current results in place. Changing a search or category starts at the first page.

A category name opens its introduction when that page is available to you. **Browse members** filters the selected collection instead. On a category article, the member list shows 50 entries at a time with its own Previous/Next controls and range. Paging members keeps the original search results available through **Back to results**. A separate **Game wiki** link remains available. Direct article visits do not invent a previous search.

Opening an imported article or category selects its owning collection in the sidebar. Direct category entry defaults its members to the authorized owner; an explicit member selection keeps its independently authorized collection. The member selection and original search return remain separate. **Wiki home** returns to the game's own navigation. Pages still obey their normal read requirements and draft visibility.

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

Without this key, the collection label is the package ID with hyphens replaced by spaces. An upgrade records the new label; omitting the key on a later upgrade restores that fallback. Rollback restores the label recorded in the selected revision; a revision with no recorded label falls back the same way. A manifest below format 1.13 warns and ignores the key.

## Declaring a landing page

Manifest format 1.14 declares an explicit collection entrance, separate from the collection label and category pins:

```yaml
format: 1.14
package: independent-rules
version: 1.0.0
wiki_landing_page: 'main:guide'
wiki:
  - page: main:guide
    title: Rules guide
    markdown: Start here.
```

`wiki_landing_page` must name exactly one `wiki:` page in this manifest. Use lowercase `main:`, `help:` or `category:` and its exact canonical slug, at most 200 UTF-8 bytes. Surrounding whitespace is trimmed; case, spelling and punctuation are not repaired. An empty or null value, list, mapping, multiline value, undeclared page or duplicate target is rejected. Plugin and UI packages cannot declare it; softcode and application packages can. Formats through 1.13 warn and ignore this key, including malformed values.

Omit the key for no entrance; omitting it on an upgrade removes the entrance. Change the value to explicitly request a binding to another declared page. The installed entrance must retain the owned page's identity, so deleting and recreating a page at the same slug does not silently replace it. The package `homepage` is still an external project/documentation URL. Titles, pins and collection labels never infer a target. These authoring rules are separate from the installed binding and reader lifecycle.

The installer binds the page's actual owned ID after creating, adopting or merging its content. Its review preview describes entrance additions, changes and removals. An update to the same target refuses a replacement page even when the content matches. Deliberately binding a replacement requires a successful upgrade clearing the field followed by one re-adding it, or a change to a different declared target.

Rollback restores an old entrance only if its captured page ID is still owned and live; otherwise it withholds the entrance and adds an administrator note. It leaves current wiki content and ownership unchanged. Uninstall removes entrance metadata even when an edited or translated page survives as game content.

## Authoring an entrance and next steps

Give readers an entrance by authoring a published Category introduction and setting `pin: true`. The collection displays authorized pinned introductions as **Featured topics**, before search and the complete directory. Its title opens the introduction; **Browse members** remains a separate filter. A category without a readable introduction keeps ordinary member browsing without a featured introduction link. Pins are presentation metadata, so curate the featured set rather than relying on a title or slug to sort an entrance first.

For example, a workshop package can author:

```yaml
format: 1.13
package: workshop-manual
version: 1.0.0
wiki_collection_name: Workshop manual
wiki:
  - page: category:workshop_orientation
    title: Start here — Workshop manual
    published: true
    pin: true
    markdown: |
      Read the [[equipment checklist|main:workshop_equipment]], then
      the [[safety preparation|main:workshop_safety]] before starting a procedure.
```

The category key and title are authored content. The engine does not interpret “Start here,” choose a Main article, or derive a lesson sequence from title, modification time or directory order. The collection label names the collection and does not select a landing page. Requirements and draft visibility still apply to every introduction and destination.

Within an instructional article, use an ordinary Markdown container for a named next action and a return link:

```markdown
::: wiki-next-step
### Next step

Continue with [[Prepare the equipment|main:workshop_equipment]].

Return to [[the maintenance manual|category:workshop_orientation]].
:::
```

The portal styles this block in the reading column. It uses normal wiki links, which retain the reader's search return, selected member collection and requested language. Use an explicit branch when readers have a choice; author procedure-to-verification links for a manual or “Continue exploring” for a public tour. Keep private destination titles and secret facts out of public prose. Raw HTML, event handlers, inline styles and unsafe URL schemes remain restricted by the Markdown pipeline.

The **More in …** card is a reference list of related pages, with complete wrapping labels. Its order does not define the next lesson or procedure. Reading order belongs in the authored introduction and next-step links.

## Installation and updates

Collection membership follows the installed package's ownership of each page. It does not depend on matching a title or guessing a prefix. Browsing collections never rewrites page IDs, history, slugs, translations or bookmarks. Reapplying a package uses the installer's merge and conflict rules for local edits.

A manually recreated page does not inherit an old page's package ownership just because its slug is the same. Package removal also follows the normal installer rules: retained, locally edited pages become game content once package ownership is removed.

Page identity is global: two pages cannot share a namespace and slug in separate collections, and the installer reports that conflict. Collection browsing never reassigns a colliding bookmark.

## Start here and visibility

An authorized landing page appears above Featured topics as **Start here**, under the article's live localized title. Its link keeps the requested language and collection browse context, and needs the installed page identity: deleting and recreating the same slug does not satisfy it. A missing, hidden, unowned or changed target gives no entrance. Pins and directory order do not define a reading order.

Switching the active character refreshes article and collection visibility even when the URL stays the same. The entrance, member directory and article all use the active character's permissions, including character-specific overrides.

Text-only category members leave out image placeholders; real images and category, draft and restricted badges stay visible.
