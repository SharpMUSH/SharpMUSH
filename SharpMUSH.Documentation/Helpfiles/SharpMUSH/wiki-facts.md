<!-- help-article
{
  "corpus": "help",
  "id": "wiki-facts",
  "lookup": "wiki facts",
  "aliases": ["WIKI-FACTS", "@WIKI/FACT", "@WIKI/QUERY"],
  "sections": [
    {"id": "values", "heading": "Values", "lookup": "wiki fact values"},
    {"id": "queries", "heading": "Queries", "lookup": "wiki fact queries"},
    {"id": "index", "heading": "The fact index", "lookup": "wiki fact index"}
  ]
}
-->
# Wiki Facts

- `@wiki/fact <page>`
- `@wiki/fact <page>/<property>=<value>[|<value>...]`
- `@wiki/fact <page>/<property>=`
- `@wiki/query <condition>[, <condition>...]`
- `@wiki/fact/index [rebuild]`

A fact is a property of a page and its value, such as a spell's level. Like
categories, facts are held by the page, not written in its text, and a
translation has the same facts as its page.

`@wiki/fact <page>` shows the page's facts. `@wiki/fact <page>/<property>=`
followed by values replaces that property's values; with nothing after the
`=` it clears them. Setting a fact is an edit, so the page's requirements
apply (see [wiki permissions]). It does not change the text, so it adds no
revision.

From softcode, `wikifact(<page>)` lists the properties a page has facts for,
and `wikifact(<page>, <property>)` returns one property's values.

- [wiki fact values]: types, and naming a page
- [wiki fact queries]: finding the pages that state a value
- [wiki fact index]: how far the index has come, and rebuilding it

## Values

Each property has a type, and a value of the wrong type is refused:

- a whole number, like `3`
- a keyword from the property's list, like `arcane`
- `true` or `false`
- a page, written as its title, like `Help:Fireball`

A property takes either one value or several. Separate several values with
`|`.

```sharp
> @wiki/fact fireball/pf2e:level=3
WIKI: 'Fireball' pf2e:level: 3.
> @wiki/fact fireball/pf2e:tradition=arcane|primal
WIKI: 'Fireball' pf2e:tradition: arcane | primal.
> @wiki/fact fireball/pf2e:level=three
WIKI: pf2e:level is a whole number, not 'three'.
```

A page shows only the facts you can read. A fact that names a page you may
not read is left out.

## Queries

`@wiki/query` lists the pages whose facts meet every condition, and
`wikiquery()` returns them from softcode (see [WIKIQUERY()]). Write each
condition as `<property>=<value>`, or as `<property>=<low>..<high>` for a
range of whole numbers. `category=<name>` and `namespace=<name>` limit where
the pages come from.

```sharp
> @wiki/query pf2e:tradition=arcane, pf2e:level=1..3
```

Output: a table of the matching pages, titled "2 page(s) where pf2e:tradition=arcane, pf2e:level=1..3".

```sharp
> @wiki/query pf2e:level=high
WIKI: pf2e:level is a whole number, not 'high'.
```

## The fact index

The game keeps an index of every page's facts, so it can find the pages that
state a value. It builds the index by itself in the background when the
server starts, and every edit keeps it up to date from then on.

`@wiki/fact/index` shows how far the index has come. `@wiki/fact/index
rebuild` empties it and builds it again; that needs wiki.admin.

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
