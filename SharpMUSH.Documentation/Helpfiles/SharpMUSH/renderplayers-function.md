<!-- help-article
{
  "corpus": "help",
  "id": "renderplayers-function",
  "lookup": "renderplayers()",
  "aliases": [
    "renderplayers"
  ],
  "sections": [
    {"id": "matching", "heading": "How names are matched", "lookup": "renderplayers() matching"},
    {"id": "options", "heading": "Options", "lookup": "renderplayers() options"},
    {"id": "examples", "heading": "Examples", "lookup": "renderplayers() examples"}
  ]
}
-->
# renderplayers()

`renderplayers([<object>/]<attribute>, <text>[, <count>[, <players>[, <options>]]])`

  Finds the player names in `<text>` and replaces each one with what `<object>`/`<attribute>` returns for it. Use it to turn names in a pose into links, such as a [cmdlink()] to `+finger`. The rest of the text, and its colour and other markup, is returned as it was.

  The attribute is called with:

- `%0`: the player's dbref.
- `%1`: the name as it was written in `<text>`.
- `%2`: the whole match, with the prefix in front when `<options>` sets one.

  `<count>` is how many times each player is replaced. It is 1 when left out or empty, so only the first time a player is named is replaced. 0 replaces every time. -1 (any negative number) returns `<text>` unchanged without calling the attribute, so a game can turn the feature off with a +config value.

  `<players>` is a list of dbrefs or objids to look for. Only those players are matched; anything in the list that is not a player is skipped. Left out or empty, every player is matched.

- [renderplayers() matching]: whole words, aliases and which name wins
- [renderplayers() options]: no aliases, first names and a prefix such as `@`
- [renderplayers() examples]: linking names in a pose

## How names are matched

  A name matches only as a whole word: the character before it and the one after it must not be a letter, a digit or `_`. Case does not matter. Punctuation next to a name is fine, so `Jim's` and `Jim,` both match `Jim`.

  By default a player is matched by its full name and each of its aliases. Where two names start at the same place, the longest one is used, so `Jim Bob` is matched as one name rather than as `Jim`.

  A name that two players answer to matches neither of them; a shorter name at the same place may match instead.

  A first name (see [renderplayers() options]) is used only when no player has it as a full name, or as an alias when aliases are matched.

## Options

  `<options>` is a list of words, separated by spaces, in any order:

- `noalias`: match full names only, not aliases.
- `first`: for a name with a space in it, also match its first word, so `Jim` matches `Jim Bob`. A first word two players share matches neither.
- `prefix:<text>`: a name matches only right after `<text>`, as in `prefix:@` for `@Jim`. The prefix must start a word, so `mail@Jim` does not match. `%2` holds the prefix and the name, and the attribute's result replaces both.

  Any other word returns `#-1 INVALID OPTION <word>`.

## Examples

```sharp
> &LINK #10=cmdlink(%1,+finger %0)
> think renderplayers(#10/LINK,Jim waves at jim and Sue.)
```

Output: `Jim` and `Sue` become links that run `+finger` for each; the second `jim` stays plain text.

```sharp
> think renderplayers(#10/LINK,Sue hugs Jim. Jim grins.,0,%#)
```

Output: only the executor's own name is a link, every time it is written.

```sharp
> &MENTION #10=ansi(h,%2)
> think renderplayers(#10/MENTION,@jim\, can you help?,1,,prefix:@ first)
```

Output: `@jim, can you help?` with `@jim` in bold, if Jim Bob is a player.

::: seealso
- [cmdlink()]
- [speak()]
- [foreach()]
- [STRING FUNCTIONS]
:::
