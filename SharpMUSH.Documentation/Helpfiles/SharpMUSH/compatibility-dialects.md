<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-dialects",
  "lookup": "compatibility dialects",
  "aliases": [],
  "sections": [
    {
      "id": "rhostmush-substitutions",
      "heading": "RhostMUSH substitutions",
      "lookup": "compatibility dialects rhostmush substitutions"
    },
    {
      "id": "commas-in-one-argument-functions",
      "heading": "Commas in one-argument functions",
      "lookup": "compatibility dialects commas in one argument functions"
    }
  ]
}
-->
# Compatibility Dialects

Code read in the `rhost` or `mux` dialect parses some things as RhostMUSH or TinyMUX does. The dialect is the attribute's `rhost` or `mux` flag, or the `dialect` option for typed input and attributes without one (see [compatibility config]). Elsewhere, each of these reads as on PennMUSH.

- [compatibility dialects rhostmush substitutions]: `%+!`, `%-#`, `%-n` and the rest, and the `}` prefix
- [compatibility dialects commas in one argument functions]: `strlen(a,b)` is 3

## RhostMUSH substitutions

Under the `rhost` dialect, these read as on RhostMUSH. Elsewhere `%-n` is `-n`, and `%+!` is the argument count followed by `!`.

- `%+!` is every argument, joined by spaces, and `%+!<c>!` joins them with `<c>`.
- `%-` on its own is every argument from `%10` on, joined by commas.
- `%-#`, `%-n` and `%-k` are the dbref, name and moniker of the object whose queue entry is running, or of the player who typed the line. A `u()` does not change them, as it changes `%!`.
- `%-m` is the command line a hook is running for, and `%-p` the line the player typed. Both are empty outside a hook.
- `%-h<name>` reads RhostMUSH's attribute cache. SharpMUSH has none, so it is `#-1 INVALID CACHE`.
- `%f` is `%f`, and `%w` is `#-1`, as on a RhostMUSH game that defines no `%f` and checks no twink lock; SharpMUSH has neither. `%wa` is `#-1a`, not attribute `WA`.

A command typed after `}` keeps its runs of spaces, whatever `space_compress` says.

```sharp dialect=rhost
> think ulambda(lit(#lambda/%+!|%+!-!),a,b,c)
a b c|a-b-c
> think %-#|%f|%w
#1|%f|#-1
> }think a   b
a   b
```

## Commas in one-argument functions

Under the `mux` or `rhost` dialect, a function that takes one argument reads the commas in it as text, as TinyMUX and RhostMUSH do: `strlen(a,b)` is 3. Each piece is evaluated as it would be alone, and each comma keeps the spaces after it.

- Both: `capstr()`, `escape()`, `flip()` (and `reverse()`), `lcstr()`, `lit()`, `s()`, `secure()`, `strlen()`, `ucstr()`.
- `mux` only: `sha1()`, `strmem()`, `t()`.
- `rhost` only: `decode64()`, `encode64()`, `ispunct()`, `strlenraw()`.

```sharp dialect=mux
> think strlen(a,b) ucstr(a, b)
3 A, B
```
