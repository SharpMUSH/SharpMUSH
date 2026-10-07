<!-- help-article
{
  "corpus": "help",
  "id": "regexp-syntax",
  "lookup": "regexp syntax",
  "aliases": [],
  "sections": [
    {
      "id": "pattern-characters",
      "heading": "Pattern characters",
      "lookup": "regexp syntax pattern characters"
    },
    {
      "id": "character-classes",
      "heading": "Character classes",
      "lookup": "regexp syntax character classes"
    },
    {
      "id": "boundary-assertions",
      "heading": "Boundary assertions",
      "lookup": "regexp syntax boundary assertions"
    },
    {
      "id": "quantifiers",
      "heading": "Quantifiers",
      "lookup": "regexp syntax quantifiers"
    },
    {
      "id": "back-references",
      "heading": "Back references",
      "lookup": "regexp syntax back references"
    },
    {
      "id": "lookaround-assertions",
      "heading": "Lookaround assertions",
      "lookup": "regexp syntax lookaround assertions"
    },
    {
      "id": "advanced-syntax-resources",
      "heading": "Advanced syntax resources",
      "lookup": "regexp syntax advanced syntax resources"
    }
  ],
  "redirects": {
    "regexp syntax2": "regexp syntax pattern characters",
    "regexp syntax3": "regexp syntax character classes",
    "regexp syntax4": "regexp syntax boundary assertions",
    "regexp syntax5": "regexp syntax quantifiers",
    "regexp syntax6": "regexp syntax back references",
    "regexp syntax7": "regexp syntax lookaround assertions",
    "regexp syntax8": "regexp syntax advanced syntax resources"
  }
}
-->

# Regular Expression Syntax

SharpMUSH uses `System.Text.RegularExpressions`, the .NET regular expression engine. PennMUSH uses PCRE2. Common patterns work in both, but POSIX bracket classes, Python-style named groups, and some advanced PCRE constructs need changes when porting code.

The patterns in the reference below show what the regex engine receives. MUSHcode evaluates function arguments first: escape square brackets, backslashes, parentheses, braces, and commas as needed. `lit()` preserves regex backslashes and square brackets, but grouping parentheses still need protection from the default function-argument parser. In an evaluated pattern argument, use `%(`/`%)` for literal grouping parentheses (unless `paren_groups` is enabled), `\[`/`\]` for class brackets, and doubled backslashes for regex escapes. For example:

```sharp
> think regmatch(foo_bar,lit(\A\w+\z))
1
> think regmatch(foo bar,lit(\A\w+\z))
0
```

Command and listen attributes with the REGEXP flag use regular expressions; their CASE flag controls case sensitivity. Functions such as `regmatch()`, `regrab()`, `regedit()`, and `reswitch()` are case-sensitive by default and have variants ending in `i` for case-insensitive matching. Matching searches for a substring unless the pattern supplies anchors. See [regexp] and [regmatch()].

Historical attribution: the previous version of this guide excerpted the PCRE manual by Philip Hazel <ph10@cam.ac.uk>, Copyright (c) 1997-1999 University of Cambridge, England. This reference has been rewritten for SharpMUSH's .NET engine.

## Pattern characters

Most characters match themselves. Outside character classes, these characters have special meanings:

| Syntax | Meaning |
| --- | --- |
| `\` | Escape a metacharacter or introduce a regex escape. |
| `.` | Any character except newline; `(?s)` includes newline. |
| `^`, `$` | Start and end anchors; see Boundary assertions. |
| `[abc]` | One character from a class. |
| `a|b` | Either alternative. |
| `(pattern)` | Capture a subexpression. |
| `(?:pattern)` | Group without capturing. |
| `*`, `+`, `?`, `{m,n}` | Repeat the preceding character or group. |

For a literal plus sign use `\+`; for a literal dot use `\.`. Escaping for the MUSH parser is an additional layer, not a change to regex syntax.

## Character classes

`[abc]` matches a, b, or c; `[a-z]` matches a range; `[^abc]` excludes those characters. Escape a literal `]`, `-`, or backslash where its position would otherwise give it a special meaning.

| Syntax | Meaning |
| --- | --- |
| `\d`, `\D` | Unicode decimal digit; its complement. |
| `\s`, `\S` | Unicode whitespace; its complement. |
| `\w`, `\W` | Unicode word character, including letters, decimal digits, combining marks, and connector punctuation; its complement. |
| `\p{L}`, `\P{L}` | Unicode letter; its complement. |
| `[0-9]` | ASCII decimal digit only. |

.NET does not implement POSIX classes such as `[[:digit:]]` or `[[:alpha:]]`. Replace them with explicit ranges or Unicode categories, as described in [regexp classes]. They must not be used for validation: they do not mean the POSIX class in this engine.

## Boundary assertions

`\b` matches a word boundary and `\B` matches a position that is not a word boundary. Within a character class, `\b` means a backspace character instead.

`^` and `$` anchor the start and end of the string by default. `$` can also match before a final newline. `(?m)` makes these anchors apply to individual lines. For strict whole-string validation, use `\A` (absolute start) and `\z` (absolute end), which are unaffected by multiline mode. `\Z` also allows a final newline.

## Quantifiers

| Syntax | Repetitions |
| --- | --- |
| `*` | Zero or more. |
| `+` | One or more. |
| `?` | Zero or one. |
| `{m}` | Exactly m. |
| `{m,}` | At least m. |
| `{m,n}` | Between m and n, inclusive. |

Use `{0,n}` for an upper bound with no lower bound; `{,n}` is not a .NET quantifier. Quantifiers are greedy by default. A following `?` makes them lazy, for example `.*?`. PCRE possessive quantifiers such as `*+` are unsupported; .NET offers atomic groups `(?>pattern)` when backtracking must be prevented. Braces and commas must also survive the MUSH parser.

## Back references

A numeric backreference such as `\1` matches the text captured by a group in the pattern. For example, `(sens|respons)e and \1ibility` matches "sense and sensibility" or "response and responsibility".

Name a capture with `(?<name>pattern)` or `(?'name'pattern)`, and refer to it within the pattern with `\k<name>`. For example, `(?<word>sens|respons)e and \k<word>ibility`. PCRE's `(?P<name>pattern)` and `(?P=name)` forms are unsupported.

There are two numbering rules to distinguish. Within regex patterns, .NET numbers unnamed groups before named groups. SharpMUSH's softcode capture APIs preserve PennMUSH opening-parenthesis order for ordinary groups, named or unnamed. Complex patterns, including duplicate group names or explicitly numbered groups, can fall back to .NET numbering. Prefer named references when mixing group types.

In a REGEXP `$-command`, `%0` is the whole match and `%1` through `%9` are captures; named arguments are available with `r(<name>,args)`. `regmatch()` can copy captures into q-registers with its register-list argument. During a `regedit()` replacement or a matched `reswitch()` body, `$0` is the whole match, `$1` through `$9` are captures, and `$<name>` reads a named capture. `$10` means `$1` followed by a literal 0 in this softcode context. These substitutions are not .NET replacement syntax.

`regreplace()` uses .NET replacement syntax instead: `$1`, `${name}`, `$&` for the whole match, and `$$` for a literal dollar sign. Its numeric references follow .NET numbering, and its replacement argument is evaluated before replacement rather than once for each match.

## Lookaround assertions

Lookarounds test text without consuming it: `(?=pattern)` is positive lookahead, `(?!pattern)` negative lookahead, `(?<=pattern)` positive lookbehind, and `(?<!pattern)` negative lookbehind. For example, `\d+(?= coins)` matches the digits before " coins" without including that suffix.

Lookaround groups themselves do not capture, although capturing groups inside them can. .NET supports variable-length lookbehind; its behavior should be tested when porting a PCRE pattern.

## Advanced syntax resources

Inline options include `(?i)` for case-insensitive matching, `(?m)` for multiline anchors, `(?s)` for dot matching newline, `(?n)` for explicit captures only, and `(?x)` for ignoring pattern whitespace and enabling comments. Options can be scoped, for example `(?i:pattern)`, or disabled with `(?-i:pattern)`.

.NET also supports conditionals, inline comments `(?#comment)`, atomic groups, and balancing groups. It does not support PCRE recursion/subroutine calls, branch-reset groups, backtracking control verbs, `\K`, or `\Q...\E` quoting. Rewrite and test these patterns rather than assuming compatibility.

For the engine reference, see https://learn.microsoft.com/dotnet/standard/base-types/regular-expression-language-quick-reference . This describes engine syntax; MUSH argument escaping and softcode capture substitutions still follow the rules above. Core softcode regex matching is time-bounded and reports `#-1 REGEXP TIMEOUT` when a match exceeds its budget. Invalid-pattern behavior depends on the calling function; see its help.
