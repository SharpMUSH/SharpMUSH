<!-- help-article
{
  "corpus": "help",
  "id": "strdistance-function",
  "lookup": "strdistance()",
  "aliases": [],
  "sections": [
    {"id": "limits", "heading": "Empty inputs and limits", "lookup": "strdistance limits"},
    {"id": "examples", "heading": "Examples", "lookup": "strdistance examples"}
  ]
}
-->
# strdistance()
`strdistance(<source>, <target>)`

  Returns the minimum number of grapheme insertions, deletions, or substitutions needed to change source into target. Each edit costs one; transposing two graphemes costs two. Comparison is ordinal and case-sensitive. Markup is ignored, and Unicode normalization is not applied: composed `chr(233)` (e with an acute accent) and decomposed `e[chr(769)]` (e followed by a combining acute accent) each contain one grapheme but differ from each other. No optional flags are supported.

  Use the result to offer a spelling suggestion for a help topic or keyword and let the player choose it. Do not use approximate matches to select targets for destructive commands. Existing `suggest()` keeps its case-folded, UTF-16-based ranking; this function does not change that behavior.

- [strdistance limits]: identical and empty inputs, and the work bounds
- [strdistance examples]: plain words, markup, wide characters and emoji

## Empty inputs and limits

  Identical inputs return zero. When one input is empty, the result is the other input's grapheme count. These cases remain subject to the same bounds: each input may contain at most 65,536 UTF-16 code units and 4,096 graphemes, and the product of the two grapheme counts may not exceed 4,000,000. Exceeding any bound returns `#-1 STRING DISTANCE WORK LIMIT EXCEEDED`. The work check happens before allocating comparison rows; row storage grows with the shorter input.

## Examples

  Examples:

```sharp
strdistance(kitten,sitting)
strdistance(ansi(r,[chr(30028)][chr(128512)]),[chr(30028)][chr(128570)])
strdistance(,e[chr(769)][chr(128512)])
```

  These return `3`, `1`, and `2`, respectively. `chr(30028)` is a wide CJK character, `chr(128512)` and `chr(128570)` are two different emoji (a grinning face and a grinning cat face), and `e[chr(769)]` is e with a combining acute accent.

::: seealso
- [SUGGEST()]
- [GRAPHEMECOUNT()]
- [GRAPHEMES()]
:::
