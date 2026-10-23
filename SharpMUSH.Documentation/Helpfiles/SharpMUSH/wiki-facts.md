<!-- help-article
{
  "corpus": "help",
  "id": "wiki-facts",
  "lookup": "wiki facts",
  "aliases": ["WIKI-FACTS", "@WIKI/FACT"],
  "sections": [
    {"id": "values", "heading": "Values", "lookup": "wiki fact values"}
  ]
}
-->
# Wiki Facts

- `@wiki/fact <page>`
- `@wiki/fact <page>/<property>=<value>[|<value>...]`
- `@wiki/fact <page>/<property>=`

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

::: seealso
- [WIKIFACT()]
- [wiki categories]
:::
