<!-- help-article
{
  "corpus": "help",
  "id": "translate-function",
  "lookup": "translate()",
  "aliases": [
    "translate"
  ],
  "sections": []
}
-->
# translate()

`translate(<string>, <type>)`

With a *<type>* of `1` or `p`, returns *<string>* written as the softcode that gives it back, colour included, as [decompose()] does. With any other *<type>*, such as `0` or `s`, returns the plain text: colour is removed, each line break and tab becomes a space, and each carriage return is removed.

This function comes from TinyMUX, where type 1 writes `%c` colour codes. SharpMUSH reads `%c` codes only when it imports a database, so it writes colour with [ansi()] instead.

```sharp
> think translate(ansi(r,red)%rline, 0)
red line
```

::: seealso
- [DECOMPOSE()]
- [STRIPANSI()]
:::
