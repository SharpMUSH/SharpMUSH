<!-- help-article
{
  "corpus": "help",
  "id": "regexp-classes",
  "lookup": "regexp classes",
  "aliases": [],
  "sections": [
    {
      "id": "replacements",
      "heading": "Replacements for POSIX classes",
      "lookup": "regexp classes replacements"
    },
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

# Regular Expression Classes

POSIX classes such as `[[:digit:]]` and `[[:alpha:]]` work inside a bracket expression and match ASCII characters only, as in PennMUSH. The .NET classes `\d`, `\w` and `\p{L}` match Unicode characters as well. The negated spelling `[[:^word:]]` is not supported; write `[^[:word:]]`.

- [regexp classes replacements]: what each POSIX class matches, and its Unicode counterpart
- [regexp classes portable character classes]: choosing ASCII or Unicode, with examples

## Replacements for POSIX classes

Each class matches the ASCII pattern in its row. The Unicode pattern is the wider class to use when letters or digits from other scripts should match:

| POSIX name | ASCII pattern | Unicode pattern |
| --- | --- | --- |
| `digit` | `[0-9]` | `\d` or `\p{Nd}` |
| `alpha` | `[A-Za-z]` | `\p{L}` |
| `alnum` | `[A-Za-z0-9]` | `[\p{L}\p{Nd}]` |
| `lower` | `[a-z]` | `\p{Ll}` |
| `upper` | `[A-Z]` | `\p{Lu}` |
| `word` | `\w` (Unicode) | `\w` |
| `space` | `[\t\n\v\f\r ]` | `\s` |
| `blank` | `[ \t]` | `[\p{Zs}\t]` |
| `ascii` | `[\x00-\x7F]` | Same ASCII range. |
| `cntrl` | `[\x00-\x1F\x7F]` | `\p{Cc}` |
| `graph` | `[\x21-\x7E]` | Choose the required categories explicitly. |
| `print` | `[\x20-\x7E]` | Choose the required categories explicitly. |
| `punct` | `[\x21-\x2F\x3A-\x40\x5B-\x60\x7B-\x7E]` | `\p{P}` (Unicode punctuation excludes many symbols). |
| `xdigit` | `[A-Fa-f0-9]` | Same range for ASCII hexadecimal input. |

Negate a whole class with a leading `^`, for example `[^0-9]`. Use `\D`, `\S`, `\W`, or `\P{L}` for the complements of their corresponding Unicode classes. To combine letters and digits from any script, write `[\p{L}\p{Nd}]`; `[[:alnum:]]` is ASCII only.

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
> think regmatch(123,lit(\A[[:digit:]]+\z))
1
```

See [regexp syntax] for anchors, escaping, and the other differences from PCRE.
