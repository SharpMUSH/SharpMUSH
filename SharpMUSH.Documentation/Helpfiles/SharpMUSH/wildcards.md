<!-- help-article
{
  "corpus": "help",
  "id": "wildcards",
  "lookup": "Wildcards",
  "aliases": ["*", "?", "**"],
  "sections": [
    {"id": "escaping", "heading": "Escaping, ** and regular expressions", "lookup": "wildcards escaping"}
  ]
}
-->
# Wildcards

SharpMUSH has two wildcards, which `$-commands` and many functions understand: an asterisk (`*`) matches any number of characters, none included, and a question mark (`?`) matches exactly one.

Their most common use is to let people pass arguments to a `$-command`. A `wave` command that waves at someone:

```sharp
> &cmd.wave me=$wave *: pose waves.
```

The `*` matches anything, so `wave foo` and `wave bar` both work. `%0` to `%9` hold what the first ten wildcards matched:

```sharp
> &cmd.wave me=$wave *: pose waves to %0.
> wave Muse
Mike waves to Muse.
```

- [wildcards escaping]: matching a literal * or ?, the ** wildcard, and regexps

## Escaping, ** and regular expressions

A backslash (`\`) before `*` or `?` matches a literal asterisk or question mark. You usually have to write `\\`, so that the parser leaves one backslash for the match:

```sharp
> think strmatch(foobar, ?*?)
1
> think strmatch(foobar, \\?*\\?)
0
> think strmatch(?foobar?, \\?*\\?)
1
```

The `**` wildcard matches attribute names across levels of an attribute tree: see [attribute trees browsing].

Regular expressions match strings with more control than wildcards, and more to learn: see [regexp].

::: seealso
- [$-commands]
- [regexp]
- [STACK]
- [registers]
:::
