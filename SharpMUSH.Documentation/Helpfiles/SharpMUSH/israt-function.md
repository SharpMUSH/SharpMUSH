<!-- help-article
{
  "corpus": "help",
  "id": "israt-function",
  "lookup": "israt()",
  "aliases": [
    "israt"
  ],
  "sections": []
}
-->
# israt()

`israt(<string>)`

Returns 1 if *<string>* is a decimal number, and 0 otherwise. The number may have a leading `+` or `-` and spaces around it, and needs at least one digit, before or after an optional decimal point. Unlike [isnum()], an exponent such as `1E20` and the words `NaN`, `Inf` and `Ind` are not decimal numbers.

This function comes from TinyMUX.

```sharp
> think [israt(22223.0000)] [israt(+77)] [israt(1E20)]
1 1 0
```

::: seealso
- [ISINT()]
- [ISNUM()]
:::
