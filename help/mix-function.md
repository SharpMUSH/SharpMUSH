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

`mix([<object>/]<attribute>, <list1>, <list2>[, ... , <list33>, <delim>])`

This function is similar to MAP(), except that it takes the elements of up to 33 lists, one row at a time, and passes them to the user-defined function as numeric arguments 0 through 32. Arguments 0 through 9 are available as %0 through %9. Use %`<10>`-%`<32>` or r(10,args)-r(32,args) for arguments 10 or higher; v(10) reads the attribute named 10. If the lists are of different sizes, the shorter ones are padded with empty elements. `<delim>` is used to separate elements; if it is not specified, it defaults to a space. If using more than 2 lists, the last argument must be a delimiter. The 33-list limit uses all 35 function arguments: the callback, 33 lists, and the final delimiter.

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
