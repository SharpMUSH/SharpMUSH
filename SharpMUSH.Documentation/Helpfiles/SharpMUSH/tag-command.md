<!-- help-article
{
  "corpus": "help",
  "id": "tag-command",
  "lookup": "@tag",
  "aliases": [
    "@ltag",
    "tags"
  ],
  "sections": []
}
-->
# @tag

`@tag <tag>`<br>
`@tag/list [<pattern>]`<br>
`@tag/add <tag>=<object>`<br>
`@tag/remove <tag>`<br>
`@ltag` with the same switches

A tag is a name for an object. Softcode written `#<tag>` or [tagref()] finds the object on any game, where a dbref differs from game to game. `@tag` keeps the world's tags and needs the `config.admin` permission. `@ltag` keeps your own, which only you (and your objects) can use, on objects you control.

With no switch, the command shows the dbref `<tag>` names, or `#-1`. `/list` lists the tags whose names match the wildcard `<pattern>`. A tag name is up to 32 letters, digits, `_` or `-`, not only digits; case does not matter.

A world's tag wins over your own of the same name. An object takes at most 16 tags, and your own only while it has fewer than 5. A tag names its object for that object's lifetime: once the object is destroyed, the tag names nothing.

```sharp
> @tag/add weather=#42
Tag weather added for #42:1793154718015.
> @trigger #weather/update
```

Output: `#weather` is `#42`, so the second line triggers `#42/UPDATE`.

::: seealso
- [tagref()]
:::
