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
      "lookup": "accent accent examples"
    }
  ],
  "redirects": {
    "ACCENT2": "accent accent examples",
    "ACCENTS3": "accent accent examples"
  }
}
-->
# accent()

`accent(<string>, <template>)`

The accent() function will return `<string>`, with characters in it possibly changed to accented ones according to `<template>`. Both arguments must be the same size.

Whether or not the resulting string is actually displayed correctly is client-dependent. Some OSes uses different character sets than the one assumed (Unicode and ISO 8859-1), and some clients strip these 8-bit characters.

For each character in `<string>`, the corresponding character of `<template>` is checked according to the table in [accents], and a replacement done. If either the current `<string>` or `<template>` characters aren't in the table, the `<string>` character is passed through unchanged.


**See Also:**
- [STRIPACCENTS()]
- `[NOACCENTS]`
- [@nameaccent]
- [ACCNAME()]
- [accents]

## Accent examples

Some examples of accent() and their expected outputs:

```sharp
> think accent(Aule, ---:)
`Aul(e-with-diaeresis)`
Aulë
```

```sharp
> think accent(The Nina was a ship, The Ni~a was a ship)
The Ni(n-with-~)a was a ship
The Niña was a ship
```

```sharp
> think accent(Khazad ai-menu!, Khaz^d ai-m^nu!)
Khaz(a-with-^)d ai-m(e-with-^)nu!
Khazâd ai-mênu
```
