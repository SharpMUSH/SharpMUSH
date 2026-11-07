<!-- help-article
{
  "corpus": "help",
  "id": "sha1-function",
  "lookup": "sha1()",
  "aliases": [],
  "sections": []
}
-->
# sha1()

`sha1([<string>])`

Returns the SHA-1 digest of *<string>* as 40 upper-case hexadecimal digits. Markup is left out, and the text is read as UTF-8. [digest()] gives the same digest in lower case with `digest(sha1, <string>)`.

This function comes from TinyMUX.

```sharp
> think sha1(abc)
A9993E364706816ABA3E25717850C26C9CD0D89D
```

::: seealso
- [DIGEST()]
- [CRC32()]
:::
