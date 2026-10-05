<!-- help-article
{
  "corpus": "help",
  "id": "fold-function",
  "lookup": "fold()",
  "aliases": [
    "fold"
  ],
  "sections": [
    {
      "id": "fold-examples",
      "heading": "Fold examples",
      "lookup": "fold examples"
    }
  ],
  "redirects": {
    "FOLD2": "fold examples"
  }
}
-->
# fold()

`fold([<obj>/]<attr>, <list>[, <base case>[, <delimiter>]])`

This function "folds" a list through the user-defined function, set in the specified `<obj>`/`<attribute>`.

If no `<base case>` is provided, fold() passes the first element of `<list>` as %0, and the second element of `<list>` as %1, to the user-defined function. The user-defined function is then called again, with the result of the first evaluation being %0, and the next (third) element of the list as %1. This is repeated until all the elements of the list have been used. The result of the last call of `<obj>`/`<attr>` is returned.

If a base case is provided, it is passed as %0, and the first element of list is passed as %1, to the user-defined function. The process for the no-base-case fold() is then used.

If `<list>` is empty, `<attr>` is never called: fold() returns the `<base case>` when one was given, and nothing when one wasn't. Folding an empty list is the base case — it is the answer when there is nothing to combine into it, not merely a seed for a first call.

The number of times `<attr>` has been called is passed as %2, starting from 0.

Note that it's not possible to pass a `<delimiter>` to fold without also giving a `<base case>`; see the examples for a way around this.

## Fold examples

Examples:
```sharp
&REP_NUM test=%0[repeat(%1,%1)]
say fold(test/rep_num,1 2 3 4 5)
You say, "122333444455555"
say fold(test/rep_num,1 2 3 4 5,List:)
You say, "List:122333444455555"
```

```sharp
> &ADD_NUMS test=add(%0,%1)
> say fold(test/add_nums,1 2 3 4 5)
You say, "15"
```

If your list uses a delimiter, you need to give a `<base case>`. This can be a problem for dynamically generated lists. One solution is to use a register and pop the first element off the list. For example:
```sharp
&GEN_LIST test=lnum(1,rand(5,10),|)
&ADD_NUMS test=add(%0,%1)
say letq(fl, u(gen_list), fold(test/add_nums, rest(%q<fl>,|), first(%q<fl>,|), |))
You say, "36"
```


::: seealso
- [anonymous attributes]
:::
