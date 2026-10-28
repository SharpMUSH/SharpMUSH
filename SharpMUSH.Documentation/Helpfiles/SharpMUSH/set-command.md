<!-- help-article
{
  "corpus": "help",
  "id": "set-command",
  "lookup": "@set",
  "aliases": [],
  "sections": [
    {"id": "flags", "heading": "Setting flags", "lookup": "@set flags"},
    {"id": "attributes", "heading": "Setting attributes", "lookup": "@set attributes"},
    {"id": "attribute-flags", "heading": "Setting attribute flags", "lookup": "@set attribute flags"},
    {"id": "examples", "heading": "Examples", "lookup": "@set examples"}
  ]
}
-->
# @set

`@set <object>=[!]<flag> [[!]<flag> ...]`<br>
`@<pre-defined attribute> <object>=<value>`<br>
`@set <object>=<attribute>:<value>`<br>
`@set <object>/<attribute pattern>=[!]<attrflag> [[!]<attrflag> ...]`

Sets flags on an object, sets its attributes, or sets attribute flags, one form for each. `@set/quiet` leaves out every "Set." line, the QUIET flag's own included; errors still show.

- [@set flags]: the first form, setting and clearing flags
- [@set attributes]: the second and third forms, and how they differ from `&`
- [@set attribute flags]: the fourth form, flags on attributes matching a pattern
- [@set examples]: attribute flags by pattern, and a value with commas

## Setting flags

`@set <object>=[!]<flag> [[!]<flag> ...]`

The first form sets (or unsets) flag(s) on `<object>`. See [flags].<br>
Ex: `@set me=VISUAL`<br>
Flags may be specified by full name (recommended) or by flag character.<br>
Flags are set or reset in the order supplied.

## Setting attributes

`@<pre-defined attribute> <object>=<value>`<br>
`@set <object>=<attribute>:<value>`

The second form sets a pre-defined attribute on `<object>`<br>
Ex: `@fail Heavy Box=You can't pick that up.`

The third form sets an arbitrary attribute with `<value>` on `<object>`. You can also do this with `&<attribute> <object>=<value>`<br>
Ex: `@set Test Object=random:This is a random attribute.`<br>
`&random Test Object=This is a random attribute.`<br>
An important difference between these two forms is that @set will always evaluate the `<value>` before setting it on `<object>`, while the `&<attribute>` form will not evaluate when entered directly by a player in their client (and is usually what you want).

The third form keeps everything after the first colon as the value, commas included.

## Setting attribute flags

`@set <object>/<attribute pattern>=[!]<attrflag> [[!]<attrflag> ...]`

The fourth form sets (or unsets) attribute flags on every attribute of `<object>`'s own that matches `<attribute pattern>` and that you can see. The pattern takes the wildcards `*` and `?`; `**` also matches inside attribute trees. A plain attribute name matches only that attribute. An attribute you may not change is reported and skipped, and the rest are still changed. See [attribute flags] and [wildcards].

## Examples

```sharp
> @set me/CMD`*=no_inherit
```
Output: one "- no_inherit set." line for each attribute under CMD on you.
```sharp
> @set Test Object=list:red,green,blue
```
Output: Test Object/LIST - Set. The value is "red,green,blue".

::: seealso
- [ATTRIB_SET]
- [ATTRIB_SET()]
- [SET()]
:::
