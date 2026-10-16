<!-- help-article
{
  "corpus": "help",
  "id": "while-function",
  "lookup": "while()",
  "aliases": [
    "while"
  ],
  "sections": [
    {"id": "examples", "heading": "Examples", "lookup": "while examples"}
  ]
}
-->
# while()

`while([<object>/]<eval attribute>, [<object>/]<condition attribute>, <list>, <stop>[, <delimiter>[, <output separator>]])`

while() is map() that can stop part way through `<list>`. Each item is passed as %0 to `<eval attribute>`, and the result goes into the list while() returns, as with map(). Then the item is passed to `<condition attribute>`; if that gives exactly `<stop>` (case counts, and there are no wildcards), no more items are looked at. The item that stopped the loop keeps its result.

When the two attributes hold the same code, it runs once for each item and that one result is compared with `<stop>`. If either attribute does not exist, while() returns nothing. Either one may be a #lambda, as with map().

Items are separated by `<delimiter>`, or by spaces. Results are separated by `<output separator>`, or by `<delimiter>` if it is not given or is empty.

- [while examples]: stopping at the first four-letter word, with and without delimiters

while() comes from RhostMUSH, which has it from TinyMUSH 3, and takes the same arguments. Its attributes run as map()'s do here, as the object that holds them.

::: seealso
- [MAP()]
- [STEP()]
- [FOREACH()]
- [inf()]
:::

## Examples

```sharp
> &EVAL_FN me=[reverse(%0)]
> &COND_FN me=[strlen(%0)]
> think while(EVAL_FN, COND_FN, foo bar meep flibble baz, 4)
oof rab peem
> think while(EVAL_FN, COND_FN, foo|bar|meep|flibble|baz, 4, |, -)
oof-rab-peem
```
