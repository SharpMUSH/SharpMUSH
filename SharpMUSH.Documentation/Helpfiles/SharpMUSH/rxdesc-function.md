<!-- help-article
{
  "corpus": "help",
  "id": "rxdesc-function",
  "lookup": "rxdesc()",
  "aliases": [
    "rxdesc"
  ],
  "sections": [
    {
      "id": "rxdesc-layer",
      "heading": "One layer",
      "lookup": "rxdesc layer"
    }
  ]
}
-->
# rxdesc()

`rxdesc(<object>)`<br>
`rxdesc(<what>, <layer>)`

RhostMUSH's question about which attribute describes things on each reality level, over [@reality] layers. A layer's description attribute is the one an imported game's level named; a layer with none uses `DESCRIBE`.

The first form returns the description attribute of each layer `<object>` receives, in the order the layers are configured. You must control `<object>`.

The second form answers about one layer; see [rxdesc layer].

```sharp
> think listrlevels()
normal ghost
> think rxdesc(me)
DESCRIBE SPECIAL
```

Output: you receive both layers, and an import gave the `ghost` layer the attribute `SPECIAL`.

::: seealso
- [rxlevel()]
- [@reality]
:::

## One layer

`<layer>` is a layer's name or alias, or the start of one; the first configured layer that matches is used. `<what>` is one of:

- `desc`: the layer's description attribute.
- `adesc`: the attribute its description action is read from: `ADESCRIBE` for `DESCRIBE`, otherwise the description attribute followed by `_ADESC`.
- `value`: the layer's bit, by its place among the configured layers: 1 for the first, 2 for the second, 4 for the third.
- `hex`: the same bit, as `0x` and eight hexadecimal digits.

You may ask about a layer you receive, or about any layer if you hold the guildmaster role or one above it. Otherwise, or for a layer that does not exist, `desc` and `adesc` return `#-1`, `value` returns `0` and `hex` `0x00000000`. Any other `<what>` returns `#-1 UNRECOGNIZED SUBARG '<what>' FOR FUNCTION RXDESC`.

```sharp
> think rxdesc(desc,gh) rxdesc(adesc,ghost) rxdesc(hex,ghost)
SPECIAL SPECIAL_ADESC 0x00000002
```

Output: the `ghost` layer is described by `SPECIAL`, and is the second layer.
