<!-- help-article
{
  "corpus": "help",
  "id": "wiki-facts",
  "lookup": "wiki facts",
  "aliases": ["WIKI-FACTS", "@WIKI/FACT", "@WIKI/QUERY", "@WIKI/PROPERTY", "@WIKI/FACETS"],
  "sections": [
    {"id": "values", "heading": "Values", "lookup": "wiki fact values"},
    {"id": "properties", "heading": "Properties", "lookup": "wiki fact properties"},
    {"id": "queries", "heading": "Queries", "lookup": "wiki fact queries"},
    {"id": "conditions", "heading": "Conditions", "lookup": "wiki fact conditions"},
    {"id": "facets", "heading": "Facets", "lookup": "wiki facets"},
    {"id": "index", "heading": "The fact index", "lookup": "wiki fact index"}
  ]
}
-->
# Wiki Facts

- `@wiki/fact <page>`
- `@wiki/fact <page>/<property>=<value>[|<value>...]`
- `@wiki/fact <page>/<property>=`
- `@wiki/query <condition>[, <condition>...]`
- `@wiki/facets <category>[=<condition>[, <condition>...]]`
- `@wiki/fact/index [rebuild]`
- `@wiki/property [<property>]`

A fact is a property of a page and its value, such as a spell's level. Like categories, facts are held by the page, not written in its text, and a translation has the same facts as its page.

`@wiki/fact <page>` shows the page's facts. `@wiki/fact <page>/<property>=` followed by values replaces that property's values; with nothing after the `=` it clears them. Setting a fact is an edit, so the page's requirements apply (see [wiki permissions]). It does not change the text, so it adds no revision.

From softcode, `wikifact(<page>)` lists the properties a page has facts for, and `wikifact(<page>, <property>)` returns one property's values.

- [wiki fact values]: types, and naming a page
- [wiki fact properties]: the properties a page may state, and defining them
- [wiki fact queries]: finding the pages that state a value
- [wiki fact conditions]: what a query's conditions may say
- [wiki facets]: a category's values, counted
- [wiki fact index]: how far the index has come, and rebuilding it

## Values

Each property has a type, and a value of the wrong type is refused:

- `number`: a whole number, like `3`
- `decimal`: a number with a decimal point, like `1.5` (no `1e3` or `1,000`)
- `date`: a day written year-month-day, like `2026-10-23`
- `keyword`: a word from the property's list, like `arcane`
- `text`: any text, kept as written and matched whatever its case
- `boolean`: `true` or `false`
- `page`: a page, written as its title, like `Help:Fireball`

A property takes either one value or several. Separate several values with `|`.

```sharp
> @wiki/fact fireball/pf2e:level=3
WIKI: 'Fireball' pf2e:level: 3.
> @wiki/fact fireball/pf2e:tradition=arcane|primal
WIKI: 'Fireball' pf2e:tradition: arcane | primal.
> @wiki/fact fireball/pf2e:level=three
WIKI: pf2e:level is a whole number, not 'three'.
```

A page shows only the facts you can read. A fact that names a page you may not read is left out.

`@wiki <page>` shows the page's facts below its text, by label, then the pages whose facts name it. Where a client can click, a value runs `@wiki/query` for the other pages that state it.

## Properties

- `@wiki/property`
- `@wiki/property <property>`
- `@wiki/property <property>=<type>`
- `@wiki/property <property>/<field>=<value>`
- `@wiki/property <property>=`

A page states only properties the game defines. `@wiki/property` lists them and `@wiki/property <property>` shows one. Anyone may read them; changing them needs wiki.admin.

A property is named `<group>:<name>`, like `faction:leader`. Its type is one of those in [wiki fact values]. `<property>=<type>` defines it or changes its type, and `<property>=` removes it. Each field sets one thing:

- `label`: the name readers see
- `description`: what the property means
- `values`: the keywords it allows, separated by `|`; empty allows any
- `multiple`: `yes` if a page may state several values, `no` for one

```sharp
> @wiki/property faction:rank=keyword
WIKI: Defined faction:rank: keyword.
> @wiki/property faction:rank/values=captain|sergeant
WIKI: Changed faction:rank. Pages that state it keep their facts.
```

Removing a property, or changing it, leaves the facts pages already state. A page keeps a value its property no longer takes until someone changes that property on it, and a property defined again finds the pages that state it.

## Queries

`@wiki/query` lists the pages whose facts meet every condition, and `wikiquery()` returns them from softcode (see [WIKIQUERY()]). A query takes up to four conditions (see [wiki fact conditions]), and these settings:

- `category=<name>`, `namespace=<name>`: where the pages come from
- `sort=<property>`: order by any property but a page; a page that states none is left out
- `order=desc`: highest first; `order=asc`, the default, lowest first
- `show=<property>[|<property>...]`: up to four properties' values beside each page, in place of the revision and date. Not in `wikiquery()`.

`@wiki/query` lists up to 100 pages and says when more match. A `::: facts <conditions>` block in a page's text lists, each time the page is read, up to 20 of the pages its query finds.

```sharp
> @wiki/query pf2e:tradition=arcane, pf2e:level>1, sort=pf2e:level, show=pf2e:level
```

Output: a table of the matching pages with a Level column, lowest first.

```sharp
> @wiki/query pf2e:level=high
WIKI: pf2e:level is a whole number, not 'high'.
```

## Conditions

- `<property>=<value>`: the page states the value. A page is named by its title.
- `<property>=<value>|<value>`: it states either.
- `<property>!=<value>`: it does not state it.
- `<property>=*`: it states any value; `<property>!=*`, none.
- `pf2e:level>3`, `<3`, `>=3`, `<=3`: a number, decimal or date above, below, at least or at most the value; a later date is above.
- `<property>=<low>..<high>`: from *<low>* to *<high>*, as in `lore:founded=2026-01-01..2026-06-30`. Leave one end off for no limit, as in `pf2e:level=..2`.

A query needs one condition a page meets by stating a value, or a sort: `!=` alone finds no pages.

```sharp
> @wiki/query pf2e:class=*, pf2e:tradition!=divine
```

Output: a table of the pages that name a class and do not state divine.

## Facets

`@wiki/facets <category>` lists, for each property the category's pages state, the values they state and how many pages state each. Many numbers or dates are counted in up to six ranges (of equal width for whole numbers, else of about as many values each), and a property lists its twelve most stated values. After `=`, conditions narrow the pages counted; a property's own conditions do not narrow its counts, so its other values stay listed. Where a client can click, a value narrows the facets, and "List them" runs the query for the pages counted. A category's portal page offers the same values as filters.

```sharp
> @wiki/facets spells=pf2e:tradition=arcane
```

Output: "3 page(s) where pf2e:tradition=arcane.", then a line of values and counts under each property's label.

## The fact index

The game keeps an index of every page's facts, so it can find the pages that state a value. It builds the index by itself in the background when the server starts, and every edit keeps it up to date from then on.

`@wiki/fact/index` shows how far the index has come. `@wiki/fact/index rebuild` empties it and builds it again; that needs wiki.admin.

```sharp
> @wiki/fact/index
WIKI: The fact index is building: 512 of 4711 pages.
> @wiki/fact/index
WIKI: The fact index is ready: 4711 pages.
```

::: seealso
- [WIKIFACT()]
- [WIKIQUERY()]
- [wiki categories]
:::
