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

strdistance() returns how many characters must be added, removed or changed to turn `<source>` into `<target>`. Each of those costs one, so swapping two characters costs two. Case matters, and markup is ignored.

A character is what the screen shows as one: an emoji, or a letter with an accent written as a separate mark, counts once. A letter with its accent built in, `chr(233)`, and the same letter written as `e[chr(769)]` look alike but differ.

Use it to suggest the help topic or keyword a player probably meant, and let them choose it. Never use a near match to pick the target of a command that changes or destroys something.

- [strdistance limits]: identical and empty inputs, and the work bounds
- [strdistance examples]: plain words, markup, wide characters and emoji

## Empty inputs and limits

Two identical inputs return 0. When one is empty, the result is the number of characters in the other.

Each input can be at most 65,536 characters long and hold at most 4,096 characters as the screen counts them, and the two counts multiplied together at most 4,000,000. Past these, strdistance() returns `#-1 STRING DISTANCE WORK LIMIT EXCEEDED`.

## Examples

```sharp
> think strdistance(kitten,sitting)
3
> think strdistance(ab,ba)
2
> think strdistance(ansi(r,[chr(30028)][chr(128512)]),[chr(30028)][chr(128570)])
1
> think strdistance(,e[chr(769)][chr(128512)])
2
```

`chr(30028)` is a wide Chinese character, `chr(128512)` and `chr(128570)` two different emoji (a grinning face and a grinning cat face), and `e[chr(769)]` an e followed by an accent mark.

::: seealso
- [SUGGEST()]
- [GRAPHEMECOUNT()]
- [GRAPHEMES()]
:::
