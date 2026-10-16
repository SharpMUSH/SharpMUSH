<!-- help-article
{
  "corpus": "help",
  "id": "accent-function",
  "lookup": "accent()",
  "aliases": [
    "accent"
  ],
  "sections": [
    {
      "id": "accent-examples",
      "heading": "Accent examples",
      "lookup": "accent examples"
    }
  ],
  "redirects": {
    "ACCENT2": "accent examples",
    "ACCENTS3": "accent examples"
  }
}
-->
# accent()

`accent(<string>, <template>)`

The accent() function will return `<string>`, with characters in it possibly changed to accented ones according to `<template>`. Both arguments must be the same size.

SharpMUSH stores the resulting characters as Unicode text. Correct display depends on the client, its encoding settings, and its fonts. Legacy clients or connections restricted to ASCII may strip or replace accented characters.

For each character in `<string>`, the corresponding character of `<template>` is checked according to the table in [accents], and a replacement done. If either the current `<string>` or `<template>` characters aren't in the table, the `<string>` character is passed through unchanged.


::: seealso
- [STRIPACCENTS()]
- `[NOACCENTS]`
- [@nameaccent]
- [ACCNAME()]
- [accents]
:::

## Accent examples

Some examples of accent() and what they print. Each result is shown in words, since the accented letters are not ASCII:

```sharp
> think accent(Aule, ---:)
```

Prints "Aule" with a diaeresis (two dots) over the final e.

```sharp
> think accent(The Nina was a ship, The Ni~a was a ship)
```

Prints "The Nina was a ship" with a tilde over the n of "Nina".

```sharp
> think accent(Khazad ai-menu!, Khaz^d ai-m^nu!)
```

Prints "Khazad ai-menu!" with a circumflex over the second a of "Khazad" and over the e of "menu".
