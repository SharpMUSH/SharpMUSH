<!-- help-article
{
  "corpus": "help",
  "id": "subeval-function",
  "lookup": "subeval()",
  "aliases": [
    "subeval"
  ],
  "sections": []
}
-->
# subeval()

`subeval(<string>)`

Evaluates the `%` substitutions and `\` escapes in *<string>*, and leaves function calls, brackets and braces as written. `%l` is left as written too. Use it with [get()] or [v()] to control when the text in an attribute is evaluated.

This function comes from TinyMUX.

```sharp
> &TEXT me=%n says [add(1,2)]
> think subeval(v(TEXT))
One says [add(1,2)]
```

Output: `%n` became the name of the player who typed the command, and the function call stayed as text.

::: seealso
- [S()]
- [OBJEVAL()]
- [V()]
:::
