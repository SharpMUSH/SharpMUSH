<!-- help-article
{
  "corpus": "help",
  "id": "pfun-function",
  "lookup": "pfun()",
  "aliases": [
    "pfun"
  ],
  "sections": [
    {
      "id": "evaluation-permissions",
      "heading": "Evaluation permissions",
      "lookup": "pfun evaluation permissions"
    },
    {
      "id": "branch-inheritance-examples",
      "heading": "Branch inheritance examples",
      "lookup": "pfun branch inheritance examples"
    }
  ],
  "redirects": {
    "PFUN2": "pfun evaluation permissions",
    "PFUN3": "pfun branch inheritance examples"
  }
}
-->
# pfun()

`pfun(<attribute>[, <arg0>[, ... , <arg29>]])`

This function evaluates `<attribute>` from the caller's @parent, passing up to 30 `<arg>`s as %0-%9 and v(10)-v(29). When the caller doesn't have an `<attribute>` attribute set, this is the same as

`ufun(me/<attribute>[, <args>])`

It differs from ufun() when the caller does have the attribute set - pfun() will ignore the attribute on the child, and evaluate the attribute as it would be inherited from the parent.

Example:
```sharp
@create ParentObject
@parent me=ParentObject
&foo me=ChildFoo
&foo ParentObject=ParentFoo
think ufun(me/foo)
ChildFoo
think pfun(foo)
ParentFoo
```

## Evaluation permissions

This function does not have the security problems of using

`ufun(parent(me)/<attribute>)`

as the attribute is inherited (and evaluated by you, not the parent) - you don't need to be able to examine the parent object, and safer_ufun does not stop the evaluation. Note that no_inherit attribute flags are still checked, as with normal attribute inheritence.

This function is particularly useful when you want to inherit an attribute tree from a parent, but add further branches.


::: seealso
- [u()]
- [GET()]
- [PARENT()]
- [ZFUN()]
- [parent]
:::

## Branch inheritance examples

Example:

```sharp
> &root Parent=ParentRoot
> &root`foo parent=ParentRoot`foo
> think ufun(me/root) / [ufun(me/root`foo)]
ParentRoot / ParentRoot`foo
> &root`bar me=ChildRoot`bar
> think ufun(me/root) / [ufun(me/root`foo)]
 / ParentRoot`foo
```

Setting a ``ROOT`FOO`` attribute on the child automatically creates an empty ROOT attribute, which blocks the inherited ROOT attribute. The pfun() function allows you to get around this:

```sharp
> &root me=pfun(root)
> think ufun(me/root) / [ufun(me/root`foo)] / [ufun(me/root`bar)]
ParentRoot / ParentRoot`foo / ChildRoot`bar
```

Good for inherited @chatformats which use ``CHATFORMAT`<channel>`` leaf attrs to store channel-specific formats and the like.
