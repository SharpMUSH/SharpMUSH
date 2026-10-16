<!-- help-article
{
  "corpus": "help",
  "id": "mix-function",
  "lookup": "mix()",
  "aliases": [
    "mix"
  ],
  "sections": [
    {
      "id": "parallel-list-examples",
      "heading": "Parallel list examples",
      "lookup": "mix parallel list examples"
    }
  ],
  "redirects": {
    "MIX2": "mix parallel list examples"
  }
}
-->
# mix()

`mix([<object>/]<attribute>, <list1>, <list2>[, ... , <list30>, <delim>])`

This function is similar to MAP(), except that it takes the elements of up to 30 lists, one by one, and passes them to the user-defined function as %0, %1, up to %9, respectively, for elements of `<list1>` to `<list30>`. Use v() to access elements 10 or higher. If the lists are of different sizes, the shorter ones are padded with empty elements. `<delim>` is used to separate elements; if it is not specified, it defaults to a space. If using more than 2 lists, the last argument must be a delimiter.

## Parallel list examples

Examples of mix():

```sharp
> &add_nums me=add(%0, %1)
> say mix(add_nums,1 2 3 4 5, 2 4 6 8 10)
You say, "3 6 9 12 15"
```

```sharp
> &lengths me=strlen(%0) and [strlen(%1)].
> say mix(lengths, some random, words)
You say, "4 and 5. 6 and 0."
```

```sharp
> &add_nums me=lmath(add, %0 %1 %2)
> say mix(add_nums, 1:2:3, 4:5:6, 7:8:9, :)
You say, "12:15:18"
```


::: seealso
- [anonymous attributes]
- [MAP()]
- [STEP()]
:::
