<!-- help-article
{
  "corpus": "help",
  "id": "foreach-function",
  "lookup": "foreach()",
  "aliases": [
    "foreach"
  ],
  "sections": [
    {
      "id": "character-iteration-examples",
      "heading": "Character iteration examples",
      "lookup": "foreach character iteration examples"
    }
  ],
  "redirects": {
    "FOREACH2": "foreach character iteration examples"
  }
}
-->
# foreach()

`foreach([<object>/]<attribute>, <string>[, <start>[, <end>]])`

This function is similar to map(), but instead of calling the given `<object>`/`<attribute>` for each word in a list, it is called for each character in `<string>`.

For each character in `<string>`, `<object>`/`<attribute>` is called, with the character passed as %0, and its position in the string as %1 (the first character has position 0). The results are concatenated.

If `<start>` is given, everything before the first occurrence of `<start>` is copied as-is, without being passed to the `<object>`/`<attribute>`. If `<end>` is given, everything after the first occurrence of `<end>` is copied as-is. The `<start>` and `<end>` characters themselves are not copied.

## Character iteration examples

Examples:
```sharp
&add_one me=add(%0,1)
say foreach(add_one, 54321)
You say, "65432"
say [foreach(add_one, This is #0# number, #, #)]
You say, "This is 1 number"
```

```sharp
> &upper me=ucstr(%0)
> say foreach(upper, quiet quiet >shout< quiet, >, <)
You say, "quiet quiet SHOUT quiet"
```

```sharp
> &is_alphanum me=regmatch(%0,lit(\A[\p{L}\p{Nd}]\z))%b
> say foreach(is_alphanum,jt1o+)
You say, "1 1 1 1 0 "
```


::: seealso
- [MAP()]
- [anonymous attributes]
:::
