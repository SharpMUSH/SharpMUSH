<!-- help-article
{
  "corpus": "help",
  "id": "ulocal-function",
  "lookup": "ulocal()",
  "aliases": [
    "ulocal"
  ],
  "sections": [
    {
      "id": "local-register-examples",
      "heading": "Local register examples",
      "lookup": "ulocal local register examples"
    }
  ],
  "redirects": {
    "ULOCAL2": "ulocal local register examples"
  }
}
-->
# ulocal()

`ulocal([<object>/]<attribute>[, <arg0>[, ... , <arg29>]])`

The ulocal() function is similar to ufun(); it evaluates `<attribute>` on `<object>` (or the caller, if no `<object>` is given), passing up to thirty `<arg>`s. However, before evaluating the attribute, ulocal() stores all the global q-registers (%q0-%q9, %qa-%qz), in the same way as the localize() function, and restores them after the attribute is evaluated. It's useful when you need to evaluate an attribute on an untrusted object which might alter the values of the registers.

## Local register examples

Examples:
```sharp
&FRUIT me=apples bananas oranges pears
&SUB-FUNCTION me=setq(0,v(FRUIT))[extract(%q0,match(%q0,%0),1)]
&TOP-FUNCTION me=setq(0,are delicious!)[ulocal(SUB-FUNCTION,%0)] %q0
say u(TOP-FUNCTION,b*)
You say "bananas are delicious!"
```

If SUB-FUNCTION had been called with u() instead of ulocal():<br>
```sharp
> &TOP-FUNCTION me=setq(0,are delicious!)[u(SUB-FUNCTION,%0)] %q0
> say u(TOP-FUNCTION,b*)
You say "bananas apples bananas oranges pears"
```

In this second example, in SUB-FUNCTION, %q0 was set to "apples bananas oranges pears", so that when the u() "returned" and TOP-FUNCTION evaluated %q0, this is what was printed. In the first example, ulocal() reset the value of %q0 to its original "are delicious!"


::: seealso
- [u()]
- [setq()]
- [LETQ()]
- [R()]
- [LOCALIZE()]
:::
