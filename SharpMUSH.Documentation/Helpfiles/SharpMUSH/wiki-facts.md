<!-- help-article
{
  "corpus": "help",
  "id": "wiki-facts",
  "lookup": "wiki facts",
  "aliases": ["WIKI-FACTS", "@WIKI/FACT", "@WIKI/QUERY", "@WIKI/PROPERTY"],
  "sections": [
    {"id": "values", "heading": "Values", "lookup": "wiki fact values"},
    {"id": "properties", "heading": "Properties", "lookup": "wiki fact properties"},
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
- `@wiki/property [<property>]`

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
- [wiki fact properties]: the properties a page may state, and defining them
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

## Properties

- `@wiki/property`
- `@wiki/property <property>`
- `@wiki/property <property>=<type>`
- `@wiki/property <property>/<field>=<value>`
- `@wiki/property <property>=`

A page states only properties the game defines. `@wiki/property` lists them
and `@wiki/property <property>` shows one. Anyone may read them; changing them
needs wiki.admin.

A property is named `<group>:<name>`, like `faction:leader`. Its type is
`number`, `keyword`, `page` or `boolean`. `<property>=<type>` defines it or
changes its type, and `<property>=` removes it. Each field sets one thing:

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

Removing a property, or changing it, leaves the facts pages already state. A
page keeps a value its property no longer takes until someone changes that
property on it, and a property defined again finds the pages that state it.

## Queries

`@wiki/query` lists the pages whose facts meet every condition, and
`wikiquery()` returns them from softcode (see [WIKIQUERY()]). Write each
condition as `<property>=<value>`, or as `<property>=<low>..<high>` for a
range of whole numbers. `category=<name>` and `namespace=<name>` limit where
the pages come from. `@wiki/query` lists up to 100 pages and says when more
match.

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
