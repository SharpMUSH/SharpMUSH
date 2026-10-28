<!-- help-article
{
  "corpus": "help",
  "id": "isalpha-function",
  "lookup": "isalpha()",
  "aliases": [
    "isalpha",
    "isalnum()",
    "isalnum",
    "isdigit()",
    "isdigit",
    "islower()",
    "islower",
    "isupper()",
    "isupper",
    "ispunct()",
    "ispunct",
    "isspace()",
    "isspace",
    "isxdigit()",
    "isxdigit"
  ],
  "sections": []
}
-->
# Character Tests

`isalpha(<string>)`<br>
`isalnum(<string>)`<br>
`isdigit(<string>)`<br>
`islower(<string>)`<br>
`isupper(<string>)`<br>
`ispunct(<string>)`<br>
`isspace(<string>)`<br>
`isxdigit(<string>)`

Each returns 1 when the first character of `<string>` is of one kind, and 0 otherwise or when `<string>` is empty. Only ASCII characters count:
- isalpha(): a letter.
- isalnum(): a letter or a digit.
- isdigit(): a digit, 0 to 9.
- islower() and isupper(): a lower-case or capital letter.
- ispunct(): a printable character that is not a letter, a digit or a space.
- isspace(): a space, tab, line break, vertical tab, form feed or carriage return.
- isxdigit(): a hexadecimal digit, 0 to 9 or a to f in either case.

These functions come from RhostMUSH.

```sharp
> think isalpha(a123) [isdigit(a123)] [isxdigit(f00)] [ispunct(!)]
1 0 1 1
```

::: seealso
- [isnum()]
- [isword()]
:::
