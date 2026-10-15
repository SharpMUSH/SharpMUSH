<!-- help-article
{
  "corpus": "help",
  "id": "attrwatch-function",
  "lookup": "attrwatch()",
  "aliases": [
    "attrwatch"
  ],
  "sections": []
}
-->
# attrwatch()

`attrwatch(<attribute>)`

Returns what `@attribute/watch` made of the standard attribute `<attribute>`: the pattern naming the event handler's attributes that run when a copy of it changes, as `@attribute <attribute>` shows it. It returns nothing when `<attribute>` is in the attribute table but not watched, and `#-1 NOT A STANDARD ATTRIBUTE` when it is not in the table. See [@attribute watch].

Code that sets up a watch of its own uses it to add the attribute to the table only when it is not there yet, and to leave alone a watch someone else set with another pattern.

```sharp
> think attrwatch(HP)
#-1 NOT A STANDARD ATTRIBUTE
> @attribute/access HP=none
> think attrwatch(HP)
> @attribute/watch HP
> think attrwatch(HP)
WATCH`HP WATCH`HP`*
```

Output: HP is not in the table at first. Once it is, it is not watched, so the second think prints nothing. Watched with the default pattern, it returns the pattern.

::: seealso
- [@attribute]
- [events]
:::
