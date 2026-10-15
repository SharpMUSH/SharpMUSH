<!-- help-article
{
  "corpus": "help",
  "id": "bang-notation",
  "lookup": "bang notation",
  "aliases": [
    "bangs",
    "!",
    "!!",
    "!$",
    "!^"
  ],
  "sections": [
    {
      "id": "bang-examples",
      "heading": "Bang examples",
      "lookup": "bang notation examples"
    }
  ]
}
-->
# Bang Notation

`!<function>(<args>)`<br>
`!!<function>(<args>)`<br>
`!$<function>(<args>)`, `!!$<function>(<args>)`<br>
`!^<function>(<args>)`, `!!^<function>(<args>)`

A function call with one or two `!` before its name gives 1 or 0 about its result instead of the result. One `!` gives 1 when the test fails, two give 1 when it passes:
- `!` and `!!` test the number the result starts with: `!` is 1 when it is 0 or there is none, `!!` when it is not 0.
- `!$` and `!!$` test for text: `!$` is 1 when the result is empty, `!!$` when it is not.
- `!^` and `!!^` test truth the way `t()` does: `!^` is like `not()`, `!!^` like `t()`.

Output: 1 or 0.

A bang works only where a function call is recognised: at the start of an argument or a command's text, or right after `[`. It works on any function, @functions included. Anywhere else, and before a name that is not a function, the `!` is plain text, so `!wow`, `x!strlen(abc)` and `![strlen(abc)]` stay as they are.

The same bangs work before the name in `%q<name>`, and before the number in `%i<n>`, `%i_<n>`, `%-<n>` and `%d<n>` (or the `L` of `%iL` and `%i_L`): `%q<!!$name>` is 1 when the register is set to something, `%i!0` is 1 when the iteration's element is 0, `%-!!$1` is 1 when there is a second argument.

The idea comes from RhostMUSH. PennMUSH has no bangs: there `!strlen(abc)` is text, and `[!strlen(abc)]` is an unknown function.

::: seealso
- [t()]
- [not()]
- [substitutions]
- [@function]
:::

## Bang examples

```sharp
> think [!strlen(abc)] [!!strlen(abc)] [!strlen()]
0 1 1
```

```sharp
> think add(!!strlen(abc),5)
6
```

```sharp
> think [setq(name,)][if(%q<!$name>,No name.,Hello %q<name>.)]
No name.
```

```sharp
> think iter(0 1 2,%i!!0)
0 1 1
```

```sharp
> think ![strlen(abc)] x!strlen(abc)
!3 x!strlen(abc)
```
