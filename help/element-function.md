<!-- help-article
{
  "corpus": "help",
  "id": "element-function",
  "lookup": "element()",
  "aliases": [
    "MATCH()",
    "MATCHALL()",
    "element"
  ],
  "sections": [
    {
      "id": "wildcard-examples",
      "heading": "Wildcard examples",
      "lookup": "element wildcard examples"
    }
  ],
  "redirects": {
    "MATCH2": "element wildcard examples"
  }
}
-->
# element()

`match(<list>, <pattern>[, <delimiter>])`<br>
`matchall(<list>, <pattern>[, <delimiter>[, <output separator>]])`

match() returns the index of the first element of `<list>` which matches the wildcard pattern `<pattern>`. The first word has an index of 1. If no matches are found, 0 is returned. element() is an alias for match().

matchall() is similar, but returns the indexes of all matching elements. If no elements match, an empty string is returned.

In both cases, elements of `<list>` are separated by `<delimiter>`, if it's given, or a space otherwise. The results of matchall() are separated by `<ouput separator>`, if given, and `<delimiter>` if not.

To get the matching elements, instead of the indexes of where they appear in the list, use grab()/graball(). To see if a single string matches a wildcard pattern, use strmatch().

::: seealso
- [GRAB()]
- [STRMATCH()]
- [MEMBER()]
- [reglmatch()]
- [WILDCARDS]
:::

## Wildcard examples

Examples:
```sharp
say match(I am testing a test, test*)
You say, "3"
```

```sharp
> say matchall(I am testing a test, test*)
You say, "3 5"
```

```sharp
> say match(foo bar baz boing, sprocket)
You say, "0"
```

    >say matchall(foo bar baz boing, sprocket)<br>
    You say, ""
