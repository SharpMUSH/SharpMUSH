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

  SharpMUSH has two standard wildcards, which can be used in `$-commands`, as well as a number of softcode functions: an asterisk (*) matches zero or more of any characters, and a question mark (?) matches exactly one character. The most common use of wildcards is to allow people to pass arguments to `$-commands`. For example, let's say you want to have a 'wave' command which allows you to wave to a specific person:

```sharp
    > &cmd.wave me=$wave *: pose waves.
```

  The "*" wildcard will match anything, allowing you to type "wave foo", "wave bar", etc. You can use the %0-%9 substitutions to get the values which matched the first ten wildcards in the command:

```sharp
    > &cmd.wave me=$wave *: pose waves to %0.
    > wave to Muse
    Mike waves to Muse.
```

- [wildcards escaping]: matching a literal * or ?, the ** wildcard, and regexps

## Escaping, ** and regular expressions

  A backslash (\) can be used to escape * and ? if you want to match a literal asterisk or question mark. (Note that you will often have to use \\ so that the softcode parser doesn't evaluate it away!)

```sharp
  > think strmatch(foobar, ?*?)
  1
  > think strmatch(foobar, \\?*\\?)
  0
  > think strmatch(?foobar?, \\?*\\?)
  1
```

  The "**" wildcard is also available for matching attribute names - see HELP attribute trees browsing for more information.

  It's also possible to use regular expressions, rather than wildcards, for matching strings. Regexps allow a lot more control over what is matched, but are therefore somewhat more complex. See [regexp] for details.


::: seealso
- [$-commands]
- [regexp]
- [STACK]
- [registers]
:::
