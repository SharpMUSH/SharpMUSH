<!-- help-article
{
  "corpus": "help",
  "id": "escapex-function",
  "lookup": "escapex()",
  "aliases": [
    "escapex"
  ],
  "sections": []
}
-->
# escapex()

`escapex(<string>[, <omissions>[, <substitutions>]])`

escapex() is escape(), with some characters left as they are. Each character of `<omissions>` that is not one of the letters below is left alone wherever it appears. The letters leave alone a kind of `%`-substitution, by not putting a backslash before its `%`:
- `a`: colour, `%c` and `%x`.
- `8`: `%<`.
- `s`: spacing, `%b`, `%t` and `%r`.
- `d`: dbrefs, `%#`, `%!`, `%@` and `%l`.
- `p`: pronouns and names, `%n`, `%p`, `%a`, `%s` and `%o`.
- `x`: the substitutions whose letters are in `<substitutions>`.

Like escape(), escapex() puts a backslash at the front, unless the string starts with a character that gets its own backslash or a substitution left alone. `f` leaves it out, and `r` escapes only what the others would leave alone (with `r`, `f` puts the front backslash in).

escapex() comes from RhostMUSH.

```sharp
> think escapex(x\[y\])
\x\[y\]
> think escapex(\[add\(1\,1\)\])
\[add\(1\,1\)\]
> think escapex(\[add\(1\,1\)\], f\[\])
[add\(1\,1\)]
> think escapex(\%b\[x\], fsr)
\%b[x]
```

::: seealso
- [escape()]
- [secure()]
:::
