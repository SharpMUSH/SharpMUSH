<!-- help-article
{
  "corpus": "help",
  "id": "regexp-classes",
  "lookup": "regexp classes",
  "aliases": [],
  "sections": [
    {
      "id": "portable-character-classes",
      "heading": "Portable character classes",
      "lookup": "regexp classes portable character classes"
    }
  ],
  "redirects": {
    "REGEXP CLASSES2": "regexp classes portable character classes"
  }
}
-->

# regexp classes

SharpMUSH uses .NET character classes and Unicode categories. POSIX bracket classes such as `[[:digit:]]`, `[[:alpha:]]`, and `[[:^word:]]` are not supported. Convert them when porting PennMUSH patterns; .NET can accept some of this text as a different pattern without reporting an error.

The following are replacement choices, not exact equivalences for every PCRE locale or option:

| POSIX name | ASCII pattern | Unicode pattern |
| --- | --- | --- |
| `digit` | `[0-9]` | `\d` or `\p{Nd}` |
| `alpha` | `[A-Za-z]` | `\p{L}` |
| `alnum` | `[A-Za-z0-9]` | `[\p{L}\p{Nd}]` |
| `lower` | `[a-z]` | `\p{Ll}` |
| `upper` | `[A-Z]` | `\p{Lu}` |
| `word` | `[A-Za-z0-9_]` | `\w` |
| `space` | `[\t\n\v\f\r ]` | `\s` |
| `ascii` | `[\x00-\x7F]` | Same ASCII range. |
| `cntrl` | `[\x00-\x1F\x7F]` | `\p{Cc}` |
| `graph` | `[\x21-\x7E]` | Choose the required categories explicitly. |
| `print` | `[\x20-\x7E]` | Choose the required categories explicitly. |
| `punct` | `[\x21-\x2F\x3A-\x40\x5B-\x60\x7B-\x7E]` | `\p{P}` (Unicode punctuation excludes many symbols). |
| `xdigit` | `[A-Fa-f0-9]` | Same range for ASCII hexadecimal input. |

Negate a whole class with a leading `^`, for example `[^0-9]`. Use `\D`, `\S`, `\W`, or `\P{L}` for the complements of their corresponding Unicode classes. To combine letters and digits, write `[\p{L}\p{Nd}]`, not `[[:alnum:]]`.

## Portable character classes

Choose ASCII or Unicode deliberately. `[A-Za-z]` excludes accented letters; `\p{L}` includes letters from other scripts. `\d` includes non-ASCII decimal digits, while `[0-9]` is appropriate for an ASCII-only numeric format. `\w` includes more than ASCII letters, digits, and underscore.

In evaluated MUSH function arguments, `lit()` keeps the character-class brackets and regex backslashes literal. Below, `accent(cafe,___')` is "cafe" with an acute accent on the e, and `chr(1635)` is the Arabic-Indic digit three:

```sharp
> think regmatch(foo_bar,lit(\A\w+\z))
1
> think regmatch(foo bar,lit(\A\w+\z))
0
> think regmatch(accent(cafe,___'),lit(\A\p{L}+\z))
1
> think regmatch(accent(cafe,___'),lit(\A[A-Za-z]+\z))
0
> think regmatch(chr(1635),lit(\A\d\z))
1
> think regmatch(chr(1635),lit(\A[0-9]\z))
0
```

See [regexp syntax] for anchors, escaping, and the other differences from PCRE.
