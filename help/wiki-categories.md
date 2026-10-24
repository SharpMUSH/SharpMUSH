<!-- help-article
{
  "corpus": "help",
  "id": "wiki-categories",
  "lookup": "wiki categories",
  "aliases": ["WIKI-CATEGORIES", "@WIKI/CATEGORY"],
  "sections": [
    {"id": "listing", "heading": "Listing a category", "lookup": "wiki category listing"},
    {"id": "category-pages", "heading": "Category pages", "lookup": "wiki category pages"},
    {"id": "pinning", "heading": "Pinning categories", "lookup": "wiki pinning", "aliases": ["@WIKI/PIN", "@WIKI/UNPIN"]}
  ]
}
-->
# Wiki Categories

- `@wiki/category <name>`
- `@wiki/category <page>=<name>[, <name>...]`

Categories work the way they do in MediaWiki, except that they are held by the page rather than written in its text. A page can be in any number of categories; the portal lists them at the page's foot.

`@wiki/category <page>=<names>` replaces the page's categories with a comma-separated list, and `@wiki/category <page>=` takes it out of all of them. It is an edit: the page's requirements apply, and so do the new categories' (see [wiki permissions]). It does not change the text, so it adds no revision.

Every page in the character namespace is in the Character category, whatever its list says. The category comes from the namespace: it is added when the page is written and put back if a list leaves it out, so it cannot be removed.

- [wiki category listing]: listing a category's members
- [wiki category pages]: a category's own page and its translated name
- [wiki pinning]: categories on the portal's wiki home

## Listing a category

`@wiki/category <name>` lists a category's subcategories and pages. Category names are matched as titles are: case and spaces versus underscores do not matter. From softcode, `wikicategory(<name>)` returns a category's pages and `wiki(<page>, categories)` a page's categories.

```sharp
> @wiki/category combat_primer=Rules, Combat
WIKI: 'Combat Primer' categories: Combat, Rules.
> @wiki/category rules
```

The reply opens with a header line, `WIKI: Category 'Rules'` followed by a dash and `1 page(s), 0 subcategory(ies):`, then one line per member:

```sharp
  main:combat_primer             Combat Primer (rev 2, 2026-10-05)
```

## Category pages

Each category has a page of its own, `Category:Lore`, in the category namespace. Filing a page in a category that has no page yet makes one, titled with the name exactly as you typed it, so `Places of Note` keeps its capitals (when you may create pages there; a name typed in lower case, like `lore`, makes none). Writing more on that page is optional: whatever it says is shown above the list of the category's members. A category page that is itself in the Setting category is a subcategory of Setting. `[[Category:Lore]]` in a page's text is an ordinary link to that page.

A translation is in the same categories as its page; only a category's name is translated. The name shown is the title of the category's page, so giving `Category:Lore` a French translation in the portal shows French readers its French title wherever the category appears. A category with no page is shown by its key, with a capital first letter.

## Pinning categories

- `@wiki/pin [<category>]`
- `@wiki/unpin <category>`

The portal's wiki home shows a card for each pinned category, with its newest pages. Other categories are still listed in the wiki sidebar, and a search on the wiki home looks through all of them. A new game pins Character and Help, and the starter wiki pages the setup wizard offers pin each category they create.

`@wiki/pin <category>` pins a category and `@wiki/unpin <category>` unpins it; both need `wiki.admin`. A category need not have a page or any member to be pinned, and a leading `category:` is accepted. `@wiki/pin` alone lists the pinned categories. Each form returns the pinned category keys, separated by spaces. Staff can also pin a category from its page or from the Categories card on the portal's wiki admin page.

```sharp
> @wiki/pin Rules
WIKI: Category 'Rules' is now pinned to the wiki home.
> @wiki/pin
WIKI: Pinned to the wiki home: Character, Help, Rules.
```

::: seealso
- [wiki]
- [wiki editing]
- [WIKICATEGORY()]
:::
